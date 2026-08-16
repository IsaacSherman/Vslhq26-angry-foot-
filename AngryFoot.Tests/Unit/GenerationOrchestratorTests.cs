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
            new GenericBulletRankingService(),
            new TargetTitleRelevanceService(
                new EmptyOccupationDataset(), _vectorStore, NullLogger<TargetTitleRelevanceService>.Instance),
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

    /// <summary>
    /// No occupations, so a target title steers nothing here. Title relevance is exercised against
    /// the real shipped data in <see cref="TargetTitleRelevanceServiceTests"/>; these tests are
    /// about what the orchestrator does with the result.
    /// </summary>
    private sealed class EmptyOccupationDataset : AngryFoot.ApiService.Application.Benchmarks.IOccupationBenchmarkDataset
    {
        public AngryFoot.ApiService.Application.Benchmarks.OccupationBenchmarkData Data { get; } =
            AngryFoot.ApiService.Application.Benchmarks.OccupationBenchmarkData.Empty;
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

    /// <summary>
    /// Retrieval has to surface more bullets than the resume will hold, or nothing is ever left off
    /// and "why isn't this bullet here" has no answer - including the answer worth having, that a
    /// bullet just below the cut was the only one evidencing something the resume now misses.
    /// </summary>
    [Fact]
    public async Task GenerateAsync_ExplainsTheRunnersUpAndNotOnlyTheBulletsItKept()
    {
        SeedProfileAndBullets(
            "Cut Azure spend by 30%.",
            "Migrated 40 services to Azure.",
            "Tuned Kubernetes autoscaling, halving cold starts.",
            "Organised the team offsite.");
        _analyzer.Setup(x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JobAnalysisDto(["azure"], [], [], [], [], null, null));
        var sut = CreateSut();

        var result = await sut.GenerateAsync(
            new GenerationRequest("A role.", null, null, MaxBullets: 1),
            TestContext.Current.CancellationToken);

        var decisions = result.Explanation!.Decisions;
        decisions.Should().HaveCountGreaterThan(1, "the runners-up are the point of the panel");
        decisions.Should().Contain(x => x.Kind.HasFlag(BulletDecisionKindDto.Omitted));
        decisions.Count(x => !x.Kind.HasFlag(BulletDecisionKindDto.Omitted)).Should().Be(1);
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

    [Fact]
    public async Task GenerateGenericAsync_NeedsNoJobDescription_AndStillProducesAResume()
    {
        SeedProfileAndBullets("Cut Azure spend by 30%.", "Rebuilt the Postgres pipeline, cutting query time 60%.");
        var sut = CreateSut();

        var result = await sut.GenerateGenericAsync(
            new GenericGenerationRequest(ResumeAudienceDto.Recruiter, MaxBullets: 5),
            TestContext.Current.CancellationToken);

        result.ArtifactId.Should().NotBeEmpty();
        result.ResumeMarkdown.Should().NotBeNullOrWhiteSpace();
        result.SelectedBulletIds.Should().HaveCount(2);
        _analyzer.Verify(
            x => x.AnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "there is no posting to analyze");
    }

    [Fact]
    public async Task GenerateGenericAsync_PersistsAGenericArtifactWithNoLetterAndNoCoverage()
    {
        SeedProfileAndBullets("Cut Azure spend by 30%.");
        var sut = CreateSut();

        var result = await sut.GenerateGenericAsync(
            new GenericGenerationRequest(ResumeAudienceDto.TechnicalLeader, TargetTitle: "  Staff Engineer  "),
            TestContext.Current.CancellationToken);

        result.CoverLetterMarkdown.Should().BeEmpty();
        result.CoverLetterRefinement.Should().BeNull();
        result.Coverage.Should().BeNull("coverage is a statement about a posting, and there is none");

        var artifact = _database.Context.GenerationArtifacts.Single();
        artifact.IsGeneric.Should().BeTrue();
        artifact.Audience.Should().Be(nameof(ResumeAudienceDto.TechnicalLeader));
        artifact.JobTitle.Should().Be("Staff Engineer", "the target title is trimmed before persisting");
        artifact.Company.Should().BeNull();
        artifact.JobDescription.Should().BeEmpty();
        artifact.CoverLetterMarkdown.Should().BeEmpty();
        artifact.EvidenceCoverageJson.Should().BeNull();
        artifact.GenerationExplanationJson.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateGenericAsync_PrefersBreadthOverASecondBulletFromOneCluster()
    {
        SeedProfileAndBullets(
            "Tuned Kubernetes autoscaling, halving cold starts at Contoso.",
            "Hardened Kubernetes ingress, dropping 5xx rates 30% at Contoso.",
            "Rebuilt the Postgres reporting pipeline, cutting query time 60% at Contoso.");
        var sut = CreateSut();

        var result = await sut.GenerateGenericAsync(
            new GenericGenerationRequest(ResumeAudienceDto.Recruiter, MaxBullets: 2),
            TestContext.Current.CancellationToken);

        var selected = _database.Context.Bullets
            .Where(x => result.SelectedBulletIds.Contains(x.Id))
            .Select(x => x.BulletText)
            .ToArray();

        selected.Should().Contain(x => x.Contains("Postgres"));
        selected.Should().ContainSingle(x => x.Contains("Kubernetes"));
    }

    [Fact]
    public async Task GenerateGenericAsync_ExplainsItselfWithoutReferringToRequirements()
    {
        SeedProfileAndBullets(
            "Cut Azure spend by 30%.",
            "Rebuilt the Postgres pipeline, cutting query time 60%.",
            "Organised the team offsite.");
        var sut = CreateSut();

        var result = await sut.GenerateGenericAsync(
            new GenericGenerationRequest(ResumeAudienceDto.HiringManager, MaxBullets: 1),
            TestContext.Current.CancellationToken);

        var explanation = result.Explanation!;
        explanation.Summary.Should().Contain("no target title");
        explanation.Decisions.Should().HaveCountGreaterThan(1, "the runners-up are the point of the panel");
        explanation.Decisions.Should().Contain(x => x.Kind.HasFlag(BulletDecisionKindDto.Omitted));
        explanation.Decisions.Should().AllSatisfy(decision =>
            decision.Why.Reasoning.Should().NotContain("posting", "there was no posting to reason about"));
        explanation.Decisions.Should().AllSatisfy(decision =>
            decision.Why.SupportingEvidence.Should().ContainSingle()
                .Which.Because.Should().Contain("of 100 on how it is written"));
    }

    [Fact]
    public async Task GenerateGenericAsync_TellsTheRewriteWhoTheResumeIsFor()
    {
        SeedProfileAndBullets("Cut Azure spend by 30%.");
        var pipeline = new FakeRefinementPipeline(TwoVersionsOf);
        var sut = CreateSut(DeepReviewChatClient(), pipeline);

        await sut.GenerateGenericAsync(
            new GenericGenerationRequest(
                ResumeAudienceDto.Executive, TargetTitle: "Director of Engineering", DeepReview: true),
            TestContext.Current.CancellationToken);

        var request = pipeline.Requests.Should()
            .ContainSingle(x => x.ArtifactKind == "ordered set of resume bullets").Subject;
        request.SourceMaterial.Should().Contain("There is no job posting.");
        request.SourceMaterial.Should().Contain("Director of Engineering");
        request.SourceMaterial.Should().Contain("an executive");
    }

    [Fact]
    public async Task GenerateGenericAsync_WithDeepReview_RefinesTheResumeAndNoCoverLetter()
    {
        SeedProfileAndBullets("Built C# services.");
        var pipeline = new FakeRefinementPipeline(TwoVersionsOf);
        var sut = CreateSut(DeepReviewChatClient(), pipeline);

        var result = await sut.GenerateGenericAsync(
            new GenericGenerationRequest(ResumeAudienceDto.Recruiter, DeepReview: true),
            TestContext.Current.CancellationToken);

        pipeline.Requests.Should().ContainSingle("a generic generation has no cover letter to refine");
        result.ResumeRefinement!.Versions.Should().HaveCount(2);
        result.ResumeRefinement.Versions.Should().AllSatisfy(
            x => x.Text.Should().Contain("Ada", "each version is a whole rendered resume, not a JSON payload"));
        result.CoverLetterRefinement.Should().BeNull();
    }

    [Fact]
    public async Task GenerateGenericAsync_WithVerbatim_MakesNoAiCallAndKeepsYourWording()
    {
        SeedProfileAndBullets("Cut Azure spend by 30%.", "Rebuilt the Postgres pipeline.");
        var chatClient = ChatClientMocks.Throwing(new InvalidOperationException("must not be called"));
        var pipeline = new FakeRefinementPipeline(TwoVersionsOf);
        var sut = CreateSut(chatClient.Object, pipeline);

        // Deep review asked for and ignored: there are no rewrites for it to critique.
        var result = await sut.GenerateGenericAsync(
            new GenericGenerationRequest(ResumeAudienceDto.Verbatim, DeepReview: true, MaxBullets: 2),
            TestContext.Current.CancellationToken);

        chatClient.Verify(
            x => x.GetResponseAsync(
                It.IsAny<IEnumerable<Microsoft.Extensions.AI.ChatMessage>>(),
                It.IsAny<Microsoft.Extensions.AI.ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        pipeline.Requests.Should().BeEmpty();
        result.ResumeRefinement.Should().BeNull();
        result.ResumeMarkdown.Should().Contain("Cut Azure spend by 30%.");

        result.Explanation!.Decisions.Should().AllSatisfy(decision =>
            decision.Kind.Should().NotHaveFlag(BulletDecisionKindDto.Revised));
        result.Explanation.Summary.Should().Contain("printed exactly as you wrote them");
    }

    [Fact]
    public async Task PreviewGenericAsync_SelectsWithoutAiAndPersistsNothing()
    {
        SeedProfileAndBullets("Cut Azure spend by 30%.", "Rebuilt the Postgres pipeline.", "Organised the offsite.");
        var chatClient = ChatClientMocks.Throwing(new InvalidOperationException("must not be called"));
        var sut = CreateSut(chatClient.Object, new FakeRefinementPipeline());

        var preview = await sut.PreviewGenericAsync(
            new GenericGenerationRequest(ResumeAudienceDto.Recruiter, MaxBullets: 2),
            TestContext.Current.CancellationToken);

        preview.SelectedBulletIds.Should().HaveCount(2);
        preview.Explanation.Decisions.Should().HaveCountGreaterThan(2, "the runners-up are shown too");
        preview.Explanation.Decisions.Should().AllSatisfy(decision =>
            decision.Kind.Should().NotHaveFlag(BulletDecisionKindDto.Revised),
            "a preview reports the candidate's own wording, since nothing has been rewritten");

        _database.Context.GenerationArtifacts.Should().BeEmpty("a preview is not a generation");
    }

    /// <summary>
    /// The promise the preview makes: what it lists is what a generation would build from. If these
    /// two could disagree, the button would be worse than not having one.
    /// </summary>
    [Fact]
    public async Task PreviewGenericAsync_SelectsTheSameBulletsAGenerationWould()
    {
        SeedProfileAndBullets(
            "Cut Azure spend by 30%.",
            "Rebuilt the Postgres pipeline, cutting query time 60%.",
            "Tuned Kubernetes autoscaling, halving cold starts.",
            "Organised the offsite.");
        var sut = CreateSut();
        var request = new GenericGenerationRequest(ResumeAudienceDto.Verbatim, MaxBullets: 2);

        var preview = await sut.PreviewGenericAsync(request, TestContext.Current.CancellationToken);
        var generated = await sut.GenerateGenericAsync(request, TestContext.Current.CancellationToken);

        generated.SelectedBulletIds.Should().Equal(preview.SelectedBulletIds);
    }

    [Fact]
    public async Task GenerateGenericAsync_PrefersBulletsFromTheMostRecentEmployer()
    {
        var profile = new Profile { Id = Guid.NewGuid(), Name = "Ada" };
        profile.WorkHistory.Add(new WorkHistory { Id = Guid.NewGuid(), Employer = "Contoso", SortOrder = 0 });
        profile.WorkHistory.Add(new WorkHistory { Id = Guid.NewGuid(), Employer = "Initech", SortOrder = 1 });
        _database.Context.Profiles.Add(profile);
        _database.Context.Bullets.AddRange(
            new Bullet
            {
                Id = Guid.NewGuid(),
                BulletText = "Shipped the billing rewrite, cutting disputes 25%.",
                SourceEmployer = "Contoso",
                ModifiedDate = DateTime.UtcNow
            },
            new Bullet
            {
                Id = Guid.NewGuid(),
                BulletText = "Delivered the audit remediation, closing 25% of findings.",
                SourceEmployer = "Initech",
                ModifiedDate = DateTime.UtcNow
            });
        _database.Context.SaveChanges();
        var sut = CreateSut();

        var result = await sut.GenerateGenericAsync(
            new GenericGenerationRequest(ResumeAudienceDto.Verbatim, MaxBullets: 1),
            TestContext.Current.CancellationToken);

        var selected = _database.Context.Bullets.Single(x => x.Id == result.SelectedBulletIds[0]);
        selected.SourceEmployer.Should().Be("Contoso");
    }

    [Fact]
    public async Task GenerateGenericAsync_OnAnEmptyLibrary_StillProducesAResume()
    {
        var sut = CreateSut();

        var result = await sut.GenerateGenericAsync(
            new GenericGenerationRequest(ResumeAudienceDto.Recruiter),
            TestContext.Current.CancellationToken);

        result.SelectedBulletIds.Should().BeEmpty();
        result.ResumeMarkdown.Should().Contain("Candidate Name", "an empty profile renders placeholders");
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
