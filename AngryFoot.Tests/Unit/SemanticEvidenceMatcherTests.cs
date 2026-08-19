using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class SemanticEvidenceMatcherTests
{
    /// <summary>
    /// Unit vectors whose cosine similarity is the number in the name, so a test can say which side
    /// of <see cref="SemanticEvidenceMatcher.MinimumConfidence"/> a pair falls on and be read without
    /// working out any arithmetic.
    /// </summary>
    private static readonly float[] Reference = [1f, 0f];

    private static readonly float[] Similar060 = [0.6f, 0.8f];
    private static readonly float[] Similar028 = [0.28f, 0.96f];

    private static readonly Requirement Mentoring =
        new("Technical leadership, mentoring, and team collaboration", RequirementKindDto.Required, 2);

    private static Bullet MentoringBullet() => new()
    {
        Id = Guid.NewGuid(),
        BulletText = "Mentored two interns through weekly 1:1s, pair programming, and code reviews."
    };

    private readonly FakeTextEmbedder _embedder = new();

    private SemanticEvidenceMatcher CreateSut() => new(_embedder);

    private void Script(Requirement requirement, float[] requirementVector, Bullet bullet, float[] bulletVector)
    {
        _embedder.Vectors[SemanticEvidenceMatcher.QueryTextFor(requirement)] = requirementVector;
        _embedder.Vectors[BulletEmbeddingText.For(bullet)] = bulletVector;
    }

    [Fact]
    public async Task MatchAsync_ScoresARequirementAgainstABulletThatNeverNamesIt()
    {
        var bullet = MentoringBullet();
        Script(Mentoring, Reference, bullet, Similar060);

        var index = await CreateSut().MatchAsync([Mentoring], [bullet], TestContext.Current.CancellationToken);

        index.For(Mentoring.Term, bullet.Id).Should().BeApproximately(0.6, 0.001);
    }

    [Fact]
    public async Task MatchAsync_IgnoresAPairBelowTheConfidenceThreshold()
    {
        var bullet = MentoringBullet();
        Script(Mentoring, Reference, bullet, Similar028);

        var index = await CreateSut().MatchAsync([Mentoring], [bullet], TestContext.Current.CancellationToken);

        index.For(Mentoring.Term, bullet.Id).Should().BeNull("a weak resemblance is not evidence");
        index.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task MatchAsync_WithNoEmbeddingDeployment_ReturnsNothingRatherThanFailing()
    {
        var bullet = MentoringBullet();
        Script(Mentoring, Reference, bullet, Similar060);
        _embedder.IsAvailable = false;

        var index = await CreateSut().MatchAsync([Mentoring], [bullet], TestContext.Current.CancellationToken);

        index.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task MatchAsync_WhenEmbeddingFails_ReturnsNothingRatherThanFailing()
    {
        var bullet = MentoringBullet();

        // Nothing scripted: the embedder answers null, exactly as the real one does on a failed call.
        var index = await CreateSut().MatchAsync([Mentoring], [bullet], TestContext.Current.CancellationToken);

        index.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task MatchAsync_EmbedsEverythingInOneCall()
    {
        var first = MentoringBullet();
        var second = MentoringBullet();
        Script(Mentoring, Reference, first, Similar060);
        _embedder.Vectors[BulletEmbeddingText.For(second)] = Similar028;

        await CreateSut().MatchAsync([Mentoring], [first, second], TestContext.Current.CancellationToken);

        _embedder.Batches.Should().ContainSingle("a requirement set and a bullet library are one batch, not one call each")
            .Which.Should().HaveCount(3);
    }

    [Fact]
    public async Task MatchAsync_WithNoRequirements_DoesNotCallTheEmbedder()
    {
        await CreateSut().MatchAsync([], [MentoringBullet()], TestContext.Current.CancellationToken);

        _embedder.Batches.Should().BeEmpty();
    }

    [Fact]
    public async Task MatchAsync_WithNoBullets_DoesNotCallTheEmbedder()
    {
        await CreateSut().MatchAsync([Mentoring], [], TestContext.Current.CancellationToken);

        _embedder.Batches.Should().BeEmpty();
    }

    [Fact]
    public async Task MatchAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => CreateSut().MatchAsync([Mentoring], [MentoringBullet()], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task MatchAsync_EmbedsABulletTheSameWayTheVectorIndexDoes()
    {
        var bullet = MentoringBullet();
        bullet.Skills = ["Mentoring"];
        bullet.Technologies = ["Python"];
        Script(Mentoring, Reference, bullet, Similar060);

        await CreateSut().MatchAsync([Mentoring], [bullet], TestContext.Current.CancellationToken);

        // A threshold measured against indexed vectors only transfers if both sides write the bullet
        // out identically, enrichment included.
        _embedder.Batches.Single().Should().Contain(
            "Mentored two interns through weekly 1:1s, pair programming, and code reviews.. Skills: Mentoring. Technologies: Python");
    }
}
