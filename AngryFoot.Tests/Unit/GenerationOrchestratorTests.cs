using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Application.Refinement;
using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.AI;
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
        return CreateSut(deadChatClient.Object, new FakeRefinementPipeline());
    }

    private GenerationOrchestrator CreateSut(IChatClient chatClient, FakeRefinementPipeline pipeline)
    {
        return new GenerationOrchestrator(
            _database.Context,
            _analyzer.Object,
            new BulletRetrievalService(_vectorStore),
            new BulletRankingService(),
            new BulletRewriteService(chatClient, pipeline, NullLogger<BulletRewriteService>.Instance),
            new ResumeMarkdownService(),
            new CoverLetterService(chatClient, pipeline, NullLogger<CoverLetterService>.Instance),
            CreateCoverageAnalyzer());
    }

    /// <summary>
    /// The real service with no diagnostic analyzers: the generation path only ever uses its
    /// deterministic half, so faking the service would test the fake rather than what ships.
    /// </summary>
    private EvidenceCoverageService CreateCoverageAnalyzer()
    {
        return new EvidenceCoverageService(
            _database.Context,
            new FakeEvidenceReviewer(),
            [],
            NullLogger<EvidenceCoverageService>.Instance);
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
    public async Task GenerateAsync_WithoutDeepReview_ReturnsAndPersistsNoVersions()
    {
        SeedProfileAndBullets("Built C# services.");
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto([], [], [], [], [], null, null));
        var pipeline = new FakeRefinementPipeline();
        var sut = CreateSut(DeepReviewChatClient(), pipeline);

        var result = await sut.GenerateAsync(new GenerationRequest("A role.", null, null, null), CancellationToken.None);

        pipeline.Requests.Should().BeEmpty();
        result.ResumeRefinement.Should().BeNull();
        result.CoverLetterRefinement.Should().BeNull();

        var artifact = _database.Context.GenerationArtifacts.Single();
        artifact.ResumeRefinementJson.Should().BeNull();
        artifact.CoverLetterRefinementJson.Should().BeNull();
    }

    [Fact]
    public async Task GenerateAsync_WithDeepReview_RendersEachBulletSetVersionAsAWholeResume()
    {
        SeedProfileAndBullets("Built C# services.");
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto([], [], [], [], [], null, null));
        var sut = CreateSut(DeepReviewChatClient(), new FakeRefinementPipeline(TwoVersionsOf));

        var result = await sut.GenerateAsync(
            new GenerationRequest("A role.", null, null, null, DeepReview: true),
            CancellationToken.None);

        var resumeVersions = result.ResumeRefinement!.Versions;
        resumeVersions.Should().HaveCount(2);
        resumeVersions.Should().AllSatisfy(x => x.Text.Should().Contain("Ada", "each version is a whole rendered resume, not a JSON payload"));
        resumeVersions[0].Text.Should().Contain("Built C# services.");
        resumeVersions[1].Text.Should().Contain("Synthesized bullet.");
        result.ResumeMarkdown.Should().Contain("Synthesized bullet.", "the recommended version is the one that gets stored");

        result.CoverLetterRefinement!.Versions.Should().HaveCount(2);
        result.CoverLetterMarkdown.Should().Be("Synthesized letter.");

        var artifact = _database.Context.GenerationArtifacts.Single();
        artifact.ResumeMarkdown.Should().Be(result.ResumeMarkdown);
        artifact.ResumeRefinementJson.Should().Contain("Synthesized bullet.");
        artifact.CoverLetterRefinementJson.Should().Contain("Synthesized letter.");
    }

    [Fact]
    public async Task GenerateAsync_WithDeepReview_OffersTheRefinementBulletsTheRankerLeftOut()
    {
        SeedProfileAndBullets("Built C# services.", "Watered the office plants.", "Ran the office move.");
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto(["c#"], [], [], [], [], null, null));
        var pipeline = new FakeRefinementPipeline(TwoVersionsOf);
        var sut = CreateSut(DeepReviewChatClient(), pipeline);

        await sut.GenerateAsync(
            new GenerationRequest("C# role.", null, null, MaxBullets: 1, DeepReview: true),
            CancellationToken.None);

        var request = pipeline.Requests.Should()
            .ContainSingle(x => x.ArtifactKind == "ordered set of resume bullets").Subject;
        request.SourceMaterial.Should().Contain("Built C# services.", "the selected bullet is on the resume");
        request.SourceMaterial.Should().Contain(
            "office", "the runner-up bullets have to be offered for deep review to swap one in");
    }

    [Fact]
    public async Task GenerateAsync_WithoutDeepReview_RetrievesNoBench()
    {
        SeedProfileAndBullets("Built C# services.", "Watered the office plants.");
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto(["c#"], [], [], [], [], null, null));
        var sut = CreateSut(DeepReviewChatClient(), new FakeRefinementPipeline());

        var result = await sut.GenerateAsync(
            new GenerationRequest("C# role.", null, null, MaxBullets: 1),
            CancellationToken.None);

        result.SelectedBulletIds.Should().ContainSingle("bullet selection is untouched when deep review is off");
    }

    [Fact]
    public async Task GenerateAsync_PassesGuidanceToBothServices()
    {
        SeedProfileAndBullets("Built C# services.");
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto([], [], [], [], [], null, null));
        var pipeline = new FakeRefinementPipeline(TwoVersionsOf);
        var sut = CreateSut(DeepReviewChatClient(), pipeline);

        await sut.GenerateAsync(
            new GenerationRequest("A role.", null, null, null, DeepReview: true, Guidance: "  ACME was a startup  "),
            CancellationToken.None);

        pipeline.Requests.Should().HaveCount(2, "the bullet set and the cover letter are each refined");
        pipeline.Requests.Should().AllSatisfy(x => x.UserGuidance.Should().Be("ACME was a startup"));
    }

    /// <summary>
    /// An AI that answers both generation calls. The rewrite reply is an empty set, which parses
    /// cleanly and leaves every bullet on its original text - enough for deep review to engage
    /// without the test having to predict the seeded bullet ids.
    /// </summary>
    private static IChatClient DeepReviewChatClient() => new ScriptedChatClient((messages, _) =>
        string.Join("\n", messages.Select(x => x.Text)).Contains("cover letter")
            ? "Dear Team, I am thrilled."
            : """{"bullets":[]}""");

    private static RefinementDto TwoVersionsOf(RefinementRequest request)
    {
        var synthesis = request.ArtifactKind == "cover letter"
            ? "Synthesized letter."
            : request.Draft.Replace("Built C# services.", "Synthesized bullet.");

        return new RefinementDto(
            DraftVersionLabels.Synthesis,
            "Needs specifics.",
            [
                new DraftVersionDto(DraftVersionLabels.InitialDraft, "Initial draft", "", request.Draft),
                new DraftVersionDto(DraftVersionLabels.Synthesis, "Synthesis", "", synthesis)
            ]);
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

    [Fact]
    public async Task GenerateAsync_WhenSemanticMatchesAreWeak_FallsBackToKeywordRanking()
    {
        _database.Context.Profiles.Add(new Profile { Id = Guid.NewGuid(), Name = "Ada" });
        var keywordMatch = new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = "Built C# services.",
            ModifiedDate = DateTime.UtcNow
        };
        var weakSemanticMatch = new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = "Watered the office plants.",
            ModifiedDate = DateTime.UtcNow.AddMinutes(1)
        };
        _database.Context.Bullets.AddRange(keywordMatch, weakSemanticMatch);
        _database.Context.SaveChanges();

        _vectorStore.IsAvailable = true;
        _vectorStore.SearchResults = [new BulletSimilarityMatch(weakSemanticMatch.Id, 0.10f)];
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto(["c#"], [], [], [], [], null, null));
        var sut = CreateSut();

        var result = await sut.GenerateAsync(
            new GenerationRequest("C# role.", null, null, MaxBullets: 1),
            CancellationToken.None);

        result.SelectedBulletIds.Should().ContainSingle().Which.Should().Be(keywordMatch.Id);
    }

    [Fact]
    public async Task GenerateAsync_WhenRetrievalReturnsFewerStrongMatches_TopsOffWithKeywordRanking()
    {
        _database.Context.Profiles.Add(new Profile { Id = Guid.NewGuid(), Name = "Ada" });
        var semanticMatch = new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = "Led distributed systems design.",
            ModifiedDate = DateTime.UtcNow
        };
        var keywordMatch = new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = "Built C# services.",
            ModifiedDate = DateTime.UtcNow
        };
        var unrelated = new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = "Watered the office plants.",
            ModifiedDate = DateTime.UtcNow.AddMinutes(1)
        };
        _database.Context.Bullets.AddRange(semanticMatch, keywordMatch, unrelated);
        _database.Context.SaveChanges();

        _vectorStore.IsAvailable = true;
        _vectorStore.SearchResults = [new BulletSimilarityMatch(semanticMatch.Id, 0.87f)];
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto(["c#"], [], [], [], [], null, null));
        var sut = CreateSut();

        var result = await sut.GenerateAsync(
            new GenerationRequest("C# distributed systems role.", null, null, MaxBullets: 2),
            CancellationToken.None);

        result.SelectedBulletIds.Should().Equal(semanticMatch.Id, keywordMatch.Id);
    }
}
