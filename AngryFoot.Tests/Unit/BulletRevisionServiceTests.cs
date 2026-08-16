using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// The rule this whole feature rests on: a revision never overwrites the bullet it came from, and
/// promoting one is the only thing that changes the bullet's text.
/// </summary>
public class BulletRevisionServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IBulletRewriteAssistant> _assistant = new();

    public void Dispose() => _database.Dispose();

    private BulletRevisionService CreateSut()
    {
        var bulletService = new BulletService(
            _database.CreateContext(),
            new NoOpBulletTagger(),
            new FakeBulletVectorStore { IsAvailable = false },
            NullLogger<BulletService>.Instance);

        return new BulletRevisionService(_database.Context, bulletService, _assistant.Object);
    }

    private void AssistantReturns(string text, string? rationale = "Tightened the verb.")
    {
        _assistant
            .Setup(x => x.RewriteAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<BulletRevisionModeDto>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new RewriteBulletResponse(text, [], null, rationale));
    }

    private Bullet SeedBullet(string text = "Worked on the deployment pipeline.")
    {
        var bullet = new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            CreatedDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow
        };

        _database.Context.Bullets.Add(bullet);
        _database.Context.SaveChanges();
        return bullet;
    }

    private static CreateBulletRevisionRequest Request(
        BulletRevisionModeDto mode = BulletRevisionModeDto.StrongerWording)
        => new(mode);

    [Fact]
    public async Task CreateAsync_LeavesTheBulletTextUntouched()
    {
        var bullet = SeedBullet();
        AssistantReturns("Rebuilt the deployment pipeline, cutting releases to minutes.");

        await CreateSut().CreateAsync(bullet.Id, Request(), TestContext.Current.CancellationToken);

        var reloaded = await _database.CreateContext().Bullets.SingleAsync(TestContext.Current.CancellationToken);
        reloaded.BulletText.Should().Be("Worked on the deployment pipeline.",
            "a rewrite is a suggestion until the person who did the work says otherwise");
    }

    [Fact]
    public async Task CreateAsync_SnapshotsTheWordingItWasWrittenFrom()
    {
        var bullet = SeedBullet();
        AssistantReturns("Rebuilt the deployment pipeline.");

        var revision = await CreateSut().CreateAsync(bullet.Id, Request(), TestContext.Current.CancellationToken);

        revision!.SourceText.Should().Be("Worked on the deployment pipeline.");
        revision.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_NumbersVersionsWithinAModeAndNotAcrossThem()
    {
        var bullet = SeedBullet();
        AssistantReturns("A revision.");
        var sut = CreateSut();
        var token = TestContext.Current.CancellationToken;

        var first = await sut.CreateAsync(bullet.Id, Request(BulletRevisionModeDto.Ats), token);
        var second = await sut.CreateAsync(bullet.Id, Request(BulletRevisionModeDto.Ats), token);
        var otherMode = await sut.CreateAsync(bullet.Id, Request(BulletRevisionModeDto.Star), token);

        first!.Version.Should().Be(1);
        second!.Version.Should().Be(2);
        otherMode!.Version.Should().Be(1, "each mode keeps its own history");
    }

    [Fact]
    public async Task CreateAsync_PassesTheRequestedModeAndGuidanceToTheWriter()
    {
        var bullet = SeedBullet();
        AssistantReturns("A revision.");

        await CreateSut().CreateAsync(
            bullet.Id,
            new CreateBulletRevisionRequest(BulletRevisionModeDto.Executive, DeepReview: true, Guidance: "internal platform"),
            TestContext.Current.CancellationToken);

        _assistant.Verify(x => x.RewriteAsync(
            bullet.BulletText,
            true,
            It.IsAny<CancellationToken>(),
            BulletRevisionModeDto.Executive,
            "internal platform"));
    }

    [Fact]
    public async Task CreateAsync_WithoutARationale_IsRecordedAsNotAiGenerated()
    {
        var bullet = SeedBullet();
        AssistantReturns("Tidied text.", rationale: null);

        var revision = await CreateSut().CreateAsync(bullet.Id, Request(), TestContext.Current.CancellationToken);

        revision!.IsAiGenerated.Should().BeFalse(
            "the heuristic fallback explains itself through suggestions, not a rationale");
    }

    [Fact]
    public async Task CreateAsync_ForAnUnknownBullet_ReturnsNull()
    {
        AssistantReturns("A revision.");

        var revision = await CreateSut().CreateAsync(Guid.NewGuid(), Request(), TestContext.Current.CancellationToken);

        revision.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_ScoresTheRevisionsOwnWording()
    {
        var bullet = SeedBullet();
        AssistantReturns("Rebuilt the Apollo deployment pipeline, cutting releases by 80%.");

        var revision = await CreateSut().CreateAsync(bullet.Id, Request(), TestContext.Current.CancellationToken);

        revision!.Quality.Should().NotBeNull();
        revision.Quality!.Signals.Single(x => x.Name == BulletQualitySignals.MeasurableImpact).Earned
            .Should().BeTrue("the revision has a figure even though the bullet does not");
    }

    [Fact]
    public async Task PromoteAsync_MakesTheRevisionTheBulletText()
    {
        var bullet = SeedBullet();
        AssistantReturns("Rebuilt the deployment pipeline, cutting releases to minutes.");
        var sut = CreateSut();
        var token = TestContext.Current.CancellationToken;

        var revision = await sut.CreateAsync(bullet.Id, Request(), token);
        var result = await sut.PromoteAsync(bullet.Id, revision!.Id, token);

        result!.Bullet.BulletText.Should().Be("Rebuilt the deployment pipeline, cutting releases to minutes.");

        var reloaded = await _database.CreateContext().Bullets.SingleAsync(token);
        reloaded.BulletText.Should().Be("Rebuilt the deployment pipeline, cutting releases to minutes.");
    }

    [Fact]
    public async Task PromoteAsync_KeepsTheReplacedWordingOnTheRevision()
    {
        var bullet = SeedBullet();
        AssistantReturns("Rebuilt the deployment pipeline.");
        var sut = CreateSut();
        var token = TestContext.Current.CancellationToken;

        var revision = await sut.CreateAsync(bullet.Id, Request(), token);
        var result = await sut.PromoteAsync(bullet.Id, revision!.Id, token);

        result!.Revisions.Should().ContainSingle()
            .Which.SourceText.Should().Be("Worked on the deployment pipeline.",
                "the wording the promotion replaced is still recoverable");
    }

    [Fact]
    public async Task PromoteAsync_DoesNotMarkThePromotedRevisionOutOfDate()
    {
        var bullet = SeedBullet();
        AssistantReturns("Rebuilt the deployment pipeline.");
        var sut = CreateSut();
        var token = TestContext.Current.CancellationToken;

        var revision = await sut.CreateAsync(bullet.Id, Request(), token);
        var result = await sut.PromoteAsync(bullet.Id, revision!.Id, token);

        result!.Revisions.Single().IsStale.Should().BeFalse(
            "it is the current wording, even though the text it was written from is not");
    }

    [Fact]
    public async Task PromoteAsync_MarksTheOtherVersionsOutOfDate()
    {
        var bullet = SeedBullet();
        var sut = CreateSut();
        var token = TestContext.Current.CancellationToken;

        AssistantReturns("The ATS wording.");
        var ats = await sut.CreateAsync(bullet.Id, Request(BulletRevisionModeDto.Ats), token);
        AssistantReturns("The executive wording.");
        await sut.CreateAsync(bullet.Id, Request(BulletRevisionModeDto.Executive), token);

        var result = await sut.PromoteAsync(bullet.Id, ats!.Id, token);

        result!.Revisions.Single(x => x.Mode == BulletRevisionModeDto.Executive).IsStale
            .Should().BeTrue("it rewords text the bullet no longer carries");
    }

    [Fact]
    public async Task PromoteAsync_ForAnUnknownRevision_ReturnsNull()
    {
        var bullet = SeedBullet();

        var result = await CreateSut().PromoteAsync(bullet.Id, Guid.NewGuid(), TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_OnlyRemovesARevisionOfTheNamedBullet()
    {
        var bullet = SeedBullet();
        var other = SeedBullet("Another bullet.");
        AssistantReturns("A revision.");
        var sut = CreateSut();
        var token = TestContext.Current.CancellationToken;

        var revision = await sut.CreateAsync(bullet.Id, Request(), token);

        (await sut.DeleteAsync(other.Id, revision!.Id, token)).Should().BeFalse();
        (await sut.DeleteAsync(bullet.Id, revision.Id, token)).Should().BeTrue();
    }

    [Fact]
    public async Task DeletingABulletTakesItsRevisionsWithIt()
    {
        var bullet = SeedBullet();
        AssistantReturns("A revision.");
        var token = TestContext.Current.CancellationToken;
        await CreateSut().CreateAsync(bullet.Id, Request(), token);

        await _database.Context.Bullets.Where(x => x.Id == bullet.Id).ExecuteDeleteAsync(token);

        (await _database.CreateContext().BulletRevisions.CountAsync(token)).Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_DistinguishesAnUnknownBulletFromOneWithNoRevisions()
    {
        var bullet = SeedBullet();
        var sut = CreateSut();
        var token = TestContext.Current.CancellationToken;

        (await sut.GetAsync(Guid.NewGuid(), token)).Should().BeNull();
        (await sut.GetAsync(bullet.Id, token)).Should().BeEmpty();
    }
}
