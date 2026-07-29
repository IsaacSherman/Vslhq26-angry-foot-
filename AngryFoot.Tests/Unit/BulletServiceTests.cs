using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.Contracts;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AngryFoot.Tests.Unit;

public class BulletServiceTests : IDisposable
{
    private static readonly BulletTagging RichTagging = new(
        Tags: ["Impact"],
        Skills: ["Automation", " automation ", "Testing"],
        Technologies: ["c#"],
        JobCategories: ["Backend Engineering"],
        Impact: ["30%"]);

    private static readonly BulletTagging EmptyTagging = new([], [], [], [], []);

    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IBulletTagger> _tagger = new();

    private BulletService CreateSut()
        => new(_database.Context, _tagger.Object, NullLogger<BulletService>.Instance);

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task CreateAsync_WithSuccessfulTagging_PersistsEnrichedBullet()
    {
        _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RichTagging);
        var sut = CreateSut();

        var result = await sut.CreateAsync(new CreateBulletRequest("  Did the thing.  ", "  Acme Corp "), CancellationToken.None);

        result.BulletText.Should().Be("Did the thing.");
        result.SourceEmployer.Should().Be("Acme Corp");
        result.EnrichmentState.Should().Be(EnrichmentStateDto.Enriched);
        result.Skills.Should().BeEquivalentTo(["Automation", "Testing"], "values are trimmed, deduplicated, and sorted");

        var persisted = await sut.GetByIdAsync(result.Id, CancellationToken.None);
        persisted.Should().NotBeNull();
        persisted!.EnrichmentState.Should().Be(EnrichmentStateDto.Enriched);
    }

    [Fact]
    public async Task CreateAsync_WhenTaggerReturnsNoMetadata_MarksFailedButStillSaves()
    {
        _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyTagging);
        var sut = CreateSut();

        var result = await sut.CreateAsync(new CreateBulletRequest("A bullet."), CancellationToken.None);

        result.EnrichmentState.Should().Be(EnrichmentStateDto.Failed);
        (await sut.GetByIdAsync(result.Id, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_WhenTaggerThrows_MarksFailedButStillSaves()
    {
        _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("tagger exploded"));
        var sut = CreateSut();

        var result = await sut.CreateAsync(new CreateBulletRequest("A bullet."), CancellationToken.None);

        result.EnrichmentState.Should().Be(EnrichmentStateDto.Failed, "a tagging failure must not lose the bullet");
        (await sut.GetByIdAsync(result.Id, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_WhenCallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));
        var sut = CreateSut();

        var act = () => sut.CreateAsync(new CreateBulletRequest("A bullet."), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CreateAsync_WithBlankEmployer_StoresNull()
    {
        _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyTagging);
        var sut = CreateSut();

        var result = await sut.CreateAsync(new CreateBulletRequest("A bullet.", "   "), CancellationToken.None);

        result.SourceEmployer.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_WithUnknownId_ReturnsNull()
    {
        var sut = CreateSut();

        var result = await sut.UpdateAsync(Guid.NewGuid(), new UpdateBulletRequest("new text"), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ReplacesTextAndEmployer_AndRetags()
    {
        _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyTagging);
        var sut = CreateSut();
        var created = await sut.CreateAsync(new CreateBulletRequest("old text", "Old Corp"), CancellationToken.None);

        _tagger.Setup(x => x.TagAsync("new text", It.IsAny<CancellationToken>()))
            .ReturnsAsync(RichTagging);

        var updated = await sut.UpdateAsync(created.Id, new UpdateBulletRequest("new text", "New Corp"), CancellationToken.None);

        updated.Should().NotBeNull();
        updated!.BulletText.Should().Be("new text");
        updated.SourceEmployer.Should().Be("New Corp");
        updated.EnrichmentState.Should().Be(EnrichmentStateDto.Enriched);
        _tagger.Verify(x => x.TagAsync("new text", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnrichAsync_WithUnknownId_ReturnsNull()
    {
        var sut = CreateSut();

        (await sut.EnrichAsync(Guid.NewGuid(), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task EnrichAsync_RetagsExistingBullet()
    {
        _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyTagging);
        var sut = CreateSut();
        var created = await sut.CreateAsync(new CreateBulletRequest("A bullet."), CancellationToken.None);
        created.EnrichmentState.Should().Be(EnrichmentStateDto.Failed);

        _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RichTagging);

        var enriched = await sut.EnrichAsync(created.Id, CancellationToken.None);

        enriched!.EnrichmentState.Should().Be(EnrichmentStateDto.Enriched);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsTrueThenFalse()
    {
        _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyTagging);
        var sut = CreateSut();
        var created = await sut.CreateAsync(new CreateBulletRequest("A bullet."), CancellationToken.None);

        (await sut.DeleteAsync(created.Id, CancellationToken.None)).Should().BeTrue();
        (await sut.DeleteAsync(created.Id, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task SearchAsync_FiltersEachFacetCaseInsensitively()
    {
        _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RichTagging);
        var sut = CreateSut();
        await sut.CreateAsync(new CreateBulletRequest("Automated the Widget pipeline."), CancellationToken.None);

        _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyTagging);
        await sut.CreateAsync(new CreateBulletRequest("Unrelated bullet."), CancellationToken.None);

        (await sut.SearchAsync("WIDGET", null, null, null, null, CancellationToken.None)).Should().ContainSingle();
        (await sut.SearchAsync(null, "impact", null, null, null, CancellationToken.None)).Should().ContainSingle();
        (await sut.SearchAsync(null, null, "AUTOMATION", null, null, CancellationToken.None)).Should().ContainSingle();
        (await sut.SearchAsync(null, null, null, "C#", null, CancellationToken.None)).Should().ContainSingle();
        (await sut.SearchAsync(null, null, null, null, "backend engineering", CancellationToken.None)).Should().ContainSingle();
        (await sut.SearchAsync(null, null, null, null, null, CancellationToken.None)).Should().HaveCount(2, "no filters returns everything");
        (await sut.SearchAsync("nomatch", null, null, null, null, CancellationToken.None)).Should().BeEmpty();
    }
}
