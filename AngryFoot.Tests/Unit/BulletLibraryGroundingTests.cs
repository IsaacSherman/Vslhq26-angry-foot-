using AngryFoot.ApiService.Application.Refinement;
using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Domain;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

public class BulletLibraryGroundingTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly FakeBulletVectorStore _vectorStore = new() { IsAvailable = false };

    public void Dispose() => _database.Dispose();

    private BulletLibraryGrounding CreateSut()
        => new(_database.Context, _vectorStore, NullLogger<BulletLibraryGrounding>.Instance);

    private Guid SeedBullet(string text, params string[] technologies)
    {
        var bullet = new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            Technologies = [.. technologies],
            ModifiedDate = DateTime.UtcNow
        };

        _database.Context.Bullets.Add(bullet);
        _database.Context.SaveChanges();
        return bullet.Id;
    }

    [Fact]
    public async Task BuildContextAsync_WithVectorStore_ReturnsMatchesInRelevanceOrder()
    {
        _vectorStore.IsAvailable = true;
        var first = SeedBullet("Migrated billing to Azure.");
        var second = SeedBullet("Cut deployment time by 40%.");
        _vectorStore.SearchResults = [new BulletSimilarityMatch(second, 0.9f), new BulletSimilarityMatch(first, 0.5f)];

        var context = await CreateSut().BuildContextAsync("anything", CancellationToken.None);

        context.Should().Be("- Cut deployment time by 40%." + Environment.NewLine + "- Migrated billing to Azure.");
    }

    [Fact]
    public async Task BuildContextAsync_WithoutVectorStore_FallsBackToTermOverlap()
    {
        SeedBullet("Migrated the billing platform to Azure.");
        SeedBullet("Ran the office move.");

        var context = await CreateSut().BuildContextAsync("Migrated billing workloads onto Azure", CancellationToken.None);

        context.Should().Contain("Migrated the billing platform to Azure.");
        context.Should().NotContain("office move", "no significant term in the query appears in that bullet");
    }

    [Fact]
    public async Task BuildContextAsync_FallbackMatchesWordPrefixesAndBulletMetadata()
    {
        SeedBullet("Deployed the release pipeline.", "Kubernetes");

        var context = await CreateSut().BuildContextAsync("deploy work on kubernetes", CancellationToken.None);

        context.Should().Contain("Deployed the release pipeline.",
            "'deploy' matches 'Deployed' by word start, and the technology list counts as evidence");
    }

    [Fact]
    public async Task BuildContextAsync_WithNoRelevantBullets_ReturnsEmpty()
    {
        SeedBullet("Ran the office move.");

        var context = await CreateSut().BuildContextAsync("quantum photonics research", CancellationToken.None);

        context.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildContextAsync_WhenTheVectorStoreReturnsNothing_FallsBackToTermOverlap()
    {
        _vectorStore.IsAvailable = true;
        _vectorStore.SearchResults = [];
        SeedBullet("Migrated the billing platform to Azure.");

        var context = await CreateSut().BuildContextAsync("Migrated billing to Azure", CancellationToken.None);

        context.Should().Contain("Migrated the billing platform to Azure.");
    }

    [Fact]
    public async Task BuildContextAsync_WithBlankQuery_ReturnsEmpty()
    {
        SeedBullet("Migrated the billing platform to Azure.");

        var context = await CreateSut().BuildContextAsync("   ", CancellationToken.None);

        context.Should().BeEmpty();
    }
}
