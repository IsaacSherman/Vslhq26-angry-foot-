using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class BulletDuplicateDetectorTests : IDisposable
{
    private const string ExistingText = "Reduced deployment time from forty minutes to under four minutes.";
    private const string SimilarText = "Cut deployment time from 40 minutes down to under 4 minutes.";
    private const string UnrelatedText = "Ran the annual accessibility audit for the marketing site.";

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeBulletVectorStore _vectorStore = new();

    private BulletDuplicateDetector CreateSut() => new(_database.CreateContext(), _vectorStore);

    public void Dispose() => _database.Dispose();

    private async Task<Guid> SeedBulletAsync(string text)
    {
        var bullet = new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            CreatedDate = DateTime.UtcNow,
            ModifiedDate = DateTime.UtcNow
        };

        _database.Context.Bullets.Add(bullet);
        await _database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return bullet.Id;
    }

    private async Task IgnorePairAsync(Guid first, Guid second)
    {
        var (a, b) = BulletDuplicatePair.Canonical(first, second);
        _database.Context.IgnoredBulletDuplicatePairs.Add(new IgnoredBulletDuplicatePair
        {
            Id = Guid.NewGuid(),
            BulletIdA = a,
            BulletIdB = b,
            Similarity = 0.95,
            CreatedDate = DateTime.UtcNow
        });

        await _database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DetectAsync_WhenSemanticScoreExceedsThreshold_FlagsExistingBullet()
    {
        var existingId = await SeedBulletAsync(ExistingText);
        _vectorStore.Embeddings[SimilarText] = [1f, 0f];
        _vectorStore.SearchResults = [new BulletSimilarityMatch(existingId, 0.84f)];
        var sut = CreateSut();

        var result = await sut.DetectAsync([new DuplicateSubject(0, null, SimilarText)], TestContext.Current.CancellationToken);

        result.Mode.Should().Be(DuplicateDetectionModeDto.Semantic);
        var warning = result.WarningsByIndex[0].Should().ContainSingle().Subject;
        warning.Kind.Should().Be(DuplicateWarningKindDto.ExistingBullet);
        warning.ExistingBulletId.Should().Be(existingId);
        warning.MatchedText.Should().Be(ExistingText);
        warning.Similarity.Should().BeApproximately(0.84, 0.0001);
    }

    [Fact]
    public async Task DetectAsync_WhenSemanticScoreIsBelowThreshold_DoesNotFlag()
    {
        var existingId = await SeedBulletAsync(ExistingText);
        _vectorStore.Embeddings[UnrelatedText] = [1f, 0f];
        _vectorStore.SearchResults = [new BulletSimilarityMatch(existingId, 0.79f)];
        var sut = CreateSut();

        var result = await sut.DetectAsync([new DuplicateSubject(0, null, UnrelatedText)], TestContext.Current.CancellationToken);

        result.WarningsByIndex[0].Should().BeEmpty("scores at or below the semantic threshold are not duplicates");
    }

    [Fact]
    public async Task DetectAsync_WhenTwoCandidatesInTheBatchAreClose_FlagsBothSides()
    {
        _vectorStore.Embeddings[ExistingText] = [1f, 0f];
        _vectorStore.Embeddings[SimilarText] = [0.95f, 0.3f];
        var sut = CreateSut();

        var result = await sut.DetectAsync(
            [new DuplicateSubject(0, null, ExistingText), new DuplicateSubject(1, null, SimilarText)],
            TestContext.Current.CancellationToken);

        result.Mode.Should().Be(DuplicateDetectionModeDto.Semantic);
        result.WarningsByIndex[0].Should().ContainSingle()
            .Which.CandidateIndex.Should().Be(1);
        result.WarningsByIndex[1].Should().ContainSingle()
            .Which.CandidateIndex.Should().Be(0);
    }

    [Fact]
    public async Task DetectAsync_WhenRetrievalUnavailable_FallsBackToLexicalComparison()
    {
        var existingId = await SeedBulletAsync(ExistingText);
        _vectorStore.IsAvailable = false;
        var sut = CreateSut();

        // Same words, only punctuation and casing differ: unambiguous by text alone.
        var result = await sut.DetectAsync(
            [new DuplicateSubject(0, null, "reduced deployment time from forty minutes to under four minutes")],
            TestContext.Current.CancellationToken);

        result.Mode.Should().Be(DuplicateDetectionModeDto.Lexical);
        result.Message.Should().NotBeNullOrWhiteSpace();
        result.WarningsByIndex[0].Should().ContainSingle()
            .Which.ExistingBulletId.Should().Be(existingId);
    }

    [Fact]
    public async Task DetectAsync_WhenEmbeddingFails_ReportsLexicalModeInsteadOfSilentlyUnderReporting()
    {
        await SeedBulletAsync(ExistingText);
        _vectorStore.IsAvailable = true;
        var sut = CreateSut();

        var result = await sut.DetectAsync([new DuplicateSubject(0, null, SimilarText)], TestContext.Current.CancellationToken);

        result.Mode.Should().Be(DuplicateDetectionModeDto.Lexical);
        result.Message.Should().Contain("text only");
    }

    [Fact]
    public async Task DetectAsync_WhenAnExistingBulletIsNotIndexed_StillComparesItByText()
    {
        // Same words as the existing bullet, differing only in punctuation, so the text comparison
        // catches it even though the vector search returns nothing.
        const string candidate = "Reduced deployment time from forty minutes to under four minutes";

        // In the DB but never indexed, so the vector search cannot see it.
        var existingId = await SeedBulletAsync(ExistingText);
        _vectorStore.Embeddings[candidate] = [1f, 0f];
        _vectorStore.SearchResults = [];
        var sut = CreateSut();

        var result = await sut.DetectAsync(
            [new DuplicateSubject(0, null, candidate)],
            TestContext.Current.CancellationToken);

        result.Mode.Should().Be(DuplicateDetectionModeDto.Semantic);
        result.WarningsByIndex[0].Should().ContainSingle()
            .Which.ExistingBulletId.Should().Be(existingId);
        result.Message.Should().Contain("Index All Missing", "the user needs to know coverage was incomplete");
    }

    [Fact]
    public async Task DetectAsync_WhenEveryExistingBulletIsIndexed_ReportsNoIndexGap()
    {
        var existingId = await SeedBulletAsync(ExistingText);
        await _vectorStore.UpsertAsync(new Bullet { Id = existingId, BulletText = ExistingText }, TestContext.Current.CancellationToken);
        _vectorStore.Embeddings[SimilarText] = [1f, 0f];
        var sut = CreateSut();

        var result = await sut.DetectAsync([new DuplicateSubject(0, null, SimilarText)], TestContext.Current.CancellationToken);

        result.Mode.Should().Be(DuplicateDetectionModeDto.Semantic);
        result.Message.Should().BeNull();
    }

    [Fact]
    public async Task DetectAsync_WhenPairIsIgnored_OmitsTheWarning()
    {
        var existingId = await SeedBulletAsync(ExistingText);
        var subjectId = await SeedBulletAsync(SimilarText);
        await IgnorePairAsync(subjectId, existingId);
        _vectorStore.Embeddings[SimilarText] = [1f, 0f];
        _vectorStore.SearchResults = [new BulletSimilarityMatch(existingId, 0.97f)];
        var sut = CreateSut();

        var result = await sut.DetectAsync([new DuplicateSubject(0, subjectId, SimilarText)], TestContext.Current.CancellationToken);

        result.WarningsByIndex[0].Should().BeEmpty("the user already declared these two distinct");
    }

    [Fact]
    public async Task DetectAsync_NeverFlagsAnExistingBulletAgainstItself()
    {
        var existingId = await SeedBulletAsync(ExistingText);
        _vectorStore.Embeddings[ExistingText] = [1f, 0f];
        _vectorStore.SearchResults = [new BulletSimilarityMatch(existingId, 1.0f)];
        var sut = CreateSut();

        var result = await sut.DetectAsync([new DuplicateSubject(0, existingId, ExistingText)], TestContext.Current.CancellationToken);

        result.WarningsByIndex[0].Should().BeEmpty();
    }
}
