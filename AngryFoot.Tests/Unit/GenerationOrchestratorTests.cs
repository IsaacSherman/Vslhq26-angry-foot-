using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AngryFoot.Tests.Unit;

public class GenerationOrchestratorTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IJobAnalyzer> _analyzer = new();
    private readonly FakeBulletVectorStore _vectorStore = new() { IsAvailable = false };

    public void Dispose() => _database.Dispose();

    private GenerationOrchestrator CreateSut()
    {
        // AI is down for every downstream service: the orchestrator must still produce output.
        var deadChatClient = ChatClientMocks.Throwing(new HttpRequestException("AI unavailable"));

        return new GenerationOrchestrator(
            _database.Context,
            _analyzer.Object,
            new BulletRetrievalService(_vectorStore),
            new BulletRankingService(),
            new BulletRewriteService(deadChatClient.Object, NullLogger<BulletRewriteService>.Instance),
            new ResumeMarkdownService(),
            new CoverLetterService(deadChatClient.Object, NullLogger<CoverLetterService>.Instance));
    }

    private void SeedProfileAndBullets(params string[] bulletTexts)
    {
        _database.Context.Profiles.Add(new Profile { Id = Guid.NewGuid(), Name = "Ada" });
        foreach (var text in bulletTexts)
        {
            _database.Context.Bullets.Add(new Bullet
            {
                Id = Guid.NewGuid(),
                BulletText = text,
                ModifiedDate = DateTime.UtcNow
            });
        }

        _database.Context.SaveChanges();
    }

    [Fact]
    public async Task GenerateAsync_WithBlankJobDescription_Throws()
    {
        var sut = CreateSut();

        var act = () => sut.GenerateAsync(new GenerationRequest("   ", null, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GenerateAsync_PersistsArtifactAndReturnsResult_EvenWhenAiIsDown()
    {
        SeedProfileAndBullets("Built C# services.", "Wrote documentation.");
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto(["c#"], [], ["c#"], [], [], "Engineer", null));
        var sut = CreateSut();

        var result = await sut.GenerateAsync(
            new GenerationRequest("C# engineer role.", "  Engineer  ", "  Contoso  ", MaxBullets: 5),
            CancellationToken.None);

        result.ArtifactId.Should().NotBeEmpty();
        result.ResumeMarkdown.Should().NotBeNullOrWhiteSpace();
        result.CoverLetterMarkdown.Should().NotBeNullOrWhiteSpace();
        result.SelectedBulletIds.Should().HaveCount(2);

        var artifact = _database.Context.GenerationArtifacts.Single();
        artifact.Id.Should().Be(result.ArtifactId);
        artifact.JobTitle.Should().Be("Engineer", "the title is trimmed before persisting");
        artifact.Company.Should().Be("Contoso");
        artifact.JobAnalysisJson.Should().Contain("c#");
    }

    [Fact]
    public async Task GenerateAsync_RanksTheBestMatchingBulletFirst()
    {
        SeedProfileAndBullets("Built C# services.", "Watered the office plants.");
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto(["c#"], [], [], [], [], null, null));
        var sut = CreateSut();

        var result = await sut.GenerateAsync(
            new GenerationRequest("C# role.", null, null, MaxBullets: 1),
            CancellationToken.None);

        result.SelectedBulletIds.Should().ContainSingle();
        var selected = _database.Context.Bullets.Single(b => b.Id == result.SelectedBulletIds[0]);
        selected.BulletText.Should().Be("Built C# services.");
    }

    [Fact]
    public async Task GenerateAsync_ClampsMaxBulletsToTwenty()
    {
        SeedProfileAndBullets(Enumerable.Range(0, 30).Select(i => $"Bullet {i}").ToArray());
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto([], [], [], [], [], null, null));
        var sut = CreateSut();

        var result = await sut.GenerateAsync(
            new GenerationRequest("Role.", null, null, MaxBullets: 500),
            CancellationToken.None);

        result.SelectedBulletIds.Should().HaveCount(20);
    }

    [Fact]
    public async Task GenerateAsync_OnEmptyDatabase_CreatesAProfileRatherThanFailing()
    {
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto([], [], [], [], [], null, null));
        var sut = CreateSut();

        var result = await sut.GenerateAsync(
            new GenerationRequest("A role.", null, null, null),
            CancellationToken.None);

        result.ResumeMarkdown.Should().Contain("Candidate Name", "an empty profile renders placeholders");
        _database.Context.Profiles.Should().ContainSingle();
    }

    [Fact]
    public async Task GenerateAsync_WhenRetrievalIsAvailable_UsesRetrievedBulletsInsteadOfKeywordRanking()
    {
        SeedProfileAndBullets("Watered the office plants.");
        var semanticMatch = new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = "Built distributed systems in C#.",
            ModifiedDate = DateTime.UtcNow
        };
        _database.Context.Bullets.Add(semanticMatch);
        _database.Context.SaveChanges();

        _vectorStore.IsAvailable = true;
        _vectorStore.SearchResults = [new BulletSimilarityMatch(semanticMatch.Id, 0.87f)];
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto([], [], [], [], [], null, null));
        var sut = CreateSut();

        var result = await sut.GenerateAsync(
            new GenerationRequest("C# distributed systems role.", null, null, MaxBullets: 1),
            CancellationToken.None);

        result.SelectedBulletIds.Should().ContainSingle().Which.Should().Be(semanticMatch.Id, "the semantically retrieved bullet should win over the keyword-scored one");
    }

    [Fact]
    public async Task GenerateAsync_WhenRetrievalIsAvailableButReturnsNoMatches_FallsBackToKeywordRanking()
    {
        SeedProfileAndBullets("Built C# services.");
        _vectorStore.IsAvailable = true;
        _vectorStore.SearchResults = [];
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto(["c#"], [], [], [], [], null, null));
        var sut = CreateSut();

        var result = await sut.GenerateAsync(
            new GenerationRequest("C# role.", null, null, MaxBullets: 1),
            CancellationToken.None);

        result.SelectedBulletIds.Should().ContainSingle();
    }
}
