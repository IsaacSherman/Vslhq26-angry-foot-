using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// Scoring wording that has not been saved, and reusing the enrichment it paid for. Two of the six
/// quality signals come from the tagger rather than the text, so a full score costs an AI call -
/// and running that on save alone meant a score could not be seen until after committing to it.
/// </summary>
public class BulletAssessmentTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly CountingTagger _tagger = new();

    public void Dispose() => _database.Dispose();

    private BulletService CreateSut()
        => new(
            _database.Context,
            _tagger,
            new FakeBulletVectorStore { IsAvailable = false },
            NullLogger<BulletService>.Instance);

    /// <summary>Records how often enrichment ran, which is the whole point of reusing it.</summary>
    private sealed class CountingTagger : IBulletTagger
    {
        public int Calls { get; private set; }

        public Task<BulletTagging> TagAsync(string bulletText, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new BulletTagging(["delivery"], ["Migration"], ["Azure"], ["Engineering"], []));
        }
    }

    [Fact]
    public async Task AssessAsync_ScoresWithoutPersistingAnything()
    {
        var assessment = await CreateSut().AssessAsync(
            new AssessBulletRequest("Cut Azure spend by 30%."),
            TestContext.Current.CancellationToken);

        assessment.Quality.Score.Should().BeGreaterThan(0);
        (await _database.CreateContext().Bullets.CountAsync(TestContext.Current.CancellationToken))
            .Should().Be(0, "assessing is not saving");
    }

    [Fact]
    public async Task AssessAsync_ReturnsTheEnrichmentItUsedAgainstTheTextItUsedItFor()
    {
        var assessment = await CreateSut().AssessAsync(
            new AssessBulletRequest("  Cut Azure spend by 30%.  "),
            TestContext.Current.CancellationToken);

        assessment.Tagging.ForText.Should().Be("Cut Azure spend by 30%.");
        assessment.Tagging.Technologies.Should().Contain("Azure");
        assessment.Tagging.JobCategories.Should().Contain("Engineering");
    }

    [Fact]
    public async Task AssessAsync_ScoresTheEnrichmentDerivedSignalsItJustComputed()
    {
        var assessment = await CreateSut().AssessAsync(
            new AssessBulletRequest("Cut spend by 30%."),
            TestContext.Current.CancellationToken);

        assessment.Quality.Signals.Single(x => x.Name == BulletQualitySignals.RoleRelevance).Earned
            .Should().BeTrue("the assess call ran the tagger, so the score is a full one");
    }

    [Fact]
    public async Task AssessAsync_HonoursSettledSignalsWithoutNeedingASavedBullet()
    {
        var assessment = await CreateSut().AssessAsync(
            new AssessBulletRequest("We cut Azure spend by 30%.", [BulletQualitySignals.Ownership]),
            TestContext.Current.CancellationToken);

        assessment.Quality.Signals.Single(x => x.Name == BulletQualitySignals.Ownership)
            .Should().Match<BulletQualitySignalDto>(x => x.Earned && x.IsDeclared);
    }

    [Fact]
    public async Task CreateAsync_WithTaggingForTheSameText_DoesNotRunTheTaggerAgain()
    {
        var sut = CreateSut();
        var token = TestContext.Current.CancellationToken;

        var assessment = await sut.AssessAsync(new AssessBulletRequest("Cut Azure spend by 30%."), token);
        _tagger.Calls.Should().Be(1);

        var created = await sut.CreateAsync(
            new CreateBulletRequest("Cut Azure spend by 30%.", null, assessment.Tagging),
            token);

        _tagger.Calls.Should().Be(1, "the caller already paid for this enrichment");
        created.Technologies.Should().Contain("Azure");
        created.EnrichmentState.Should().Be(EnrichmentStateDto.Enriched);
    }

    [Fact]
    public async Task CreateAsync_WithTaggingForDifferentText_TagsFromScratch()
    {
        var sut = CreateSut();
        var token = TestContext.Current.CancellationToken;

        var assessment = await sut.AssessAsync(new AssessBulletRequest("Cut Azure spend by 30%."), token);

        await sut.CreateAsync(
            new CreateBulletRequest("Something else entirely.", null, assessment.Tagging),
            token);

        _tagger.Calls.Should().Be(2,
            "tagging that describes other wording would file the bullet under skills it never mentions");
    }

    [Fact]
    public async Task UpdateAsync_WithTaggingForTheSameText_DoesNotRunTheTaggerAgain()
    {
        var sut = CreateSut();
        var token = TestContext.Current.CancellationToken;

        var created = await sut.CreateAsync(new CreateBulletRequest("Original wording."), token);
        var assessment = await sut.AssessAsync(new AssessBulletRequest("Cut Azure spend by 30%."), token);
        var callsBefore = _tagger.Calls;

        await sut.UpdateAsync(
            created.Id,
            new UpdateBulletRequest("Cut Azure spend by 30%.", null, assessment.Tagging),
            token);

        _tagger.Calls.Should().Be(callsBefore);
    }

    [Fact]
    public async Task SetQualityAcknowledgementsAsync_PersistsAndScoresTheSettledSignal()
    {
        var sut = CreateSut();
        var token = TestContext.Current.CancellationToken;
        var created = await sut.CreateAsync(new CreateBulletRequest("We cut Azure spend by 30%."), token);

        created.Quality!.Signals.Single(x => x.Name == BulletQualitySignals.Ownership).Earned
            .Should().BeFalse();

        var settled = await sut.SetQualityAcknowledgementsAsync(
            created.Id, [BulletQualitySignals.Ownership], token);

        settled!.Quality!.Signals.Single(x => x.Name == BulletQualitySignals.Ownership)
            .Should().Match<BulletQualitySignalDto>(x => x.Earned && x.IsDeclared);

        var reloaded = await _database.CreateContext().Bullets.SingleAsync(token);
        reloaded.AcknowledgedQualitySignals.Should().Equal(BulletQualitySignals.Ownership);
    }

    [Fact]
    public async Task SetQualityAcknowledgementsAsync_CanReopenASettledSignal()
    {
        var sut = CreateSut();
        var token = TestContext.Current.CancellationToken;
        var created = await sut.CreateAsync(new CreateBulletRequest("We cut Azure spend by 30%."), token);

        await sut.SetQualityAcknowledgementsAsync(created.Id, [BulletQualitySignals.Ownership], token);
        var reopened = await sut.SetQualityAcknowledgementsAsync(created.Id, [], token);

        reopened!.Quality!.Signals.Single(x => x.Name == BulletQualitySignals.Ownership).Earned
            .Should().BeFalse();
    }

    [Fact]
    public async Task SetQualityAcknowledgementsAsync_ForAnUnknownBullet_ReturnsNull()
    {
        (await CreateSut().SetQualityAcknowledgementsAsync(
            Guid.NewGuid(), [BulletQualitySignals.Ownership], TestContext.Current.CancellationToken))
            .Should().BeNull();
    }
}
