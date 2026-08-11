using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AngryFoot.Tests.Unit;

public class ResumeBulletImportServiceTests : IDisposable
{
    private const string ResumeText = """
        EXPERIENCE

        Contoso Ltd
        • Rolled out feature flags across the platform, cutting rollback frequency in half.
        • Replaced a nightly batch export with an incremental sync used by four teams.
        """;

    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IBulletTagger> _tagger = new();
    private readonly FakeBulletVectorStore _vectorStore = new();

    public ResumeBulletImportServiceTests()
    {
        _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulletTagging(["Impact"], ["Delivery"], [], [], []));
        _vectorStore.IsAvailable = false;
    }

    private ResumeBulletImportService CreateSut()
    {
        var context = _database.CreateContext();
        var bulletService = new BulletService(context, _tagger.Object, _vectorStore, NullLogger<BulletService>.Instance);
        return new ResumeBulletImportService(context, bulletService, new BulletDuplicateDetector(context, _vectorStore));
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task PreviewAsync_ExtractsCandidatesWithoutPersistingAnything()
    {
        var sut = CreateSut();

        var preview = await sut.PreviewAsync(ResumeText, TestContext.Current.CancellationToken);

        preview.Candidates.Should().HaveCount(2);
        preview.Candidates[0].SuggestedEmployer.Should().Be("Contoso Ltd");
        preview.Candidates[0].Index.Should().Be(0);

        _database.Context.Bullets.Should().BeEmpty("preview is a read-only dry run");
        _vectorStore.Upserted.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfirmAsync_CreatesOnlySelectedCandidatesThroughTheNormalBulletPath()
    {
        var sut = CreateSut();
        var preview = await sut.PreviewAsync(ResumeText, TestContext.Current.CancellationToken);

        var selected = preview.Candidates[0];
        var result = await sut.ConfirmAsync(
            new ConfirmResumeImportRequest([new ImportBulletItem(selected.Index, selected.BulletText, "Contoso Ltd", [])]),
            TestContext.Current.CancellationToken);

        result.Created.Should().ContainSingle();
        result.Created[0].SourceEmployer.Should().Be("Contoso Ltd");
        result.Created[0].EnrichmentState.Should().Be(EnrichmentStateDto.Enriched, "import reuses the create/enrich path");

        var persisted = await _database.CreateContext().Bullets.ToListAsync(TestContext.Current.CancellationToken);
        persisted.Should().ContainSingle().Which.BulletText.Should().Be(selected.BulletText);
        _vectorStore.Upserted.Should().ContainSingle("imported bullets go through the same indexing as hand-typed ones");
    }

    [Fact]
    public async Task ConfirmAsync_RecordsIgnoredExistingPairInCanonicalOrder()
    {
        var sut = CreateSut();
        var existing = await sut.ConfirmAsync(
            new ConfirmResumeImportRequest([new ImportBulletItem(0, "An earlier bullet about deployment automation.", null, [])]),
            TestContext.Current.CancellationToken);
        var existingId = existing.Created[0].Id;

        var result = await sut.ConfirmAsync(
            new ConfirmResumeImportRequest([
                new ImportBulletItem(0, "A newly imported bullet about deployment automation.", null,
                    [new IgnoredDuplicateDecision(existingId, null, 0.93)])
            ]),
            TestContext.Current.CancellationToken);

        result.IgnoredPairCount.Should().Be(1);

        var pair = await _database.CreateContext().IgnoredBulletDuplicatePairs.SingleAsync(TestContext.Current.CancellationToken);
        var (expectedA, expectedB) = BulletDuplicatePair.Canonical(existingId, result.Created[0].Id);
        pair.BulletIdA.Should().Be(expectedA);
        pair.BulletIdB.Should().Be(expectedB);
        pair.BulletIdA.CompareTo(pair.BulletIdB).Should().BeLessThanOrEqualTo(0);
        pair.Similarity.Should().BeApproximately(0.93, 0.0001);
    }

    [Fact]
    public async Task ConfirmAsync_ResolvesIgnoredBatchPairToTheIdsItJustCreated()
    {
        var sut = CreateSut();

        var result = await sut.ConfirmAsync(
            new ConfirmResumeImportRequest([
                new ImportBulletItem(0, "Led the rollout of the new billing platform.", null,
                    [new IgnoredDuplicateDecision(null, 1, 0.92)]),
                new ImportBulletItem(1, "Led the rollout of the replacement billing system.", null, [])
            ]),
            TestContext.Current.CancellationToken);

        result.Created.Should().HaveCount(2);
        result.IgnoredPairCount.Should().Be(1);

        var pair = await _database.CreateContext().IgnoredBulletDuplicatePairs.SingleAsync(TestContext.Current.CancellationToken);
        new[] { pair.BulletIdA, pair.BulletIdB }.Should().BeEquivalentTo(result.Created.Select(x => x.Id));
    }

    [Fact]
    public async Task ConfirmAsync_SkipsIgnoredBatchPairWhenTheOtherCandidateWasNotImported()
    {
        var sut = CreateSut();

        var result = await sut.ConfirmAsync(
            new ConfirmResumeImportRequest([
                new ImportBulletItem(0, "Led the rollout of the new billing platform.", null,
                    [new IgnoredDuplicateDecision(null, 1, 0.92)])
            ]),
            TestContext.Current.CancellationToken);

        result.IgnoredPairCount.Should().Be(0, "there is no second bullet for the pair to reference");
        _database.CreateContext().IgnoredBulletDuplicatePairs.Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewAsync_WithTextThatHasNoBullets_ReturnsNoCandidates()
    {
        var sut = CreateSut();

        var preview = await sut.PreviewAsync("SKILLS\nC#\nSQL\n", TestContext.Current.CancellationToken);

        preview.Candidates.Should().BeEmpty();
    }
}
