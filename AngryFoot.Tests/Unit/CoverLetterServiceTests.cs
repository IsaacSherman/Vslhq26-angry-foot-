using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

public class CoverLetterServiceTests
{
    private static readonly JobAnalysisDto EmptyAnalysis = new([], [], [], [], [], null, null);

    private static Profile CreateProfile(string name = "Ada Lovelace", string summary = "I build engines.")
        => new() { Id = Guid.NewGuid(), Name = name, ProfessionalSummary = summary };

    private static RewrittenBullet Bullet(string text)
        => new(new Bullet { Id = Guid.NewGuid(), BulletText = text }, text);

    [Fact]
    public async Task BuildCoverLetterAsync_WithAiResponse_ReturnsTrimmedAiText()
    {
        var chatClient = ChatClientMocks.ReturningText("  Dear Team, I am thrilled.  ");
        var sut = new CoverLetterService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<CoverLetterService>.Instance);

        var result = (await sut.BuildCoverLetterAsync(
            CreateProfile(),
            new CoverLetterContext("Engineer", "Contoso", EmptyAnalysis, []),
            guidance: null,
            deepReview: false,
            CancellationToken.None)).Markdown;

        result.Should().Be("Dear Team, I am thrilled.");
    }

    [Fact]
    public async Task BuildCoverLetterAsync_WithEmptyAiResponse_UsesTemplateFallback()
    {
        var chatClient = ChatClientMocks.ReturningText("   ");
        var sut = new CoverLetterService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<CoverLetterService>.Instance);

        var result = (await sut.BuildCoverLetterAsync(
            CreateProfile(),
            new CoverLetterContext("Staff Engineer", "Contoso", EmptyAnalysis, [Bullet("Did a thing."), Bullet("Did another."), Bullet("Third."), Bullet("Fourth - should be cut.")]),
            guidance: null,
            deepReview: false,
            CancellationToken.None)).Markdown;

        result.Should().StartWith("Dear Contoso Hiring Team,");
        result.Should().Contain("Staff Engineer");
        result.Should().Contain("I build engines.");
        result.Should().Contain("- Did a thing.");
        result.Should().NotContain("Fourth", "the fallback includes at most three highlights");
        result.Should().EndWith("Ada Lovelace");
    }

    [Fact]
    public async Task BuildCoverLetterAsync_FallbackWithNoCompanyNameAndEmptyProfile_UsesGenericPlaceholders()
    {
        var chatClient = ChatClientMocks.Throwing(new HttpRequestException("down"));
        var sut = new CoverLetterService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<CoverLetterService>.Instance);

        var result = (await sut.BuildCoverLetterAsync(
            new Profile { Id = Guid.NewGuid() },
            new CoverLetterContext(null, null, EmptyAnalysis, []),
            guidance: null,
            deepReview: false,
            CancellationToken.None)).Markdown;

        result.Should().StartWith("Dear Hiring Team,");
        result.Should().Contain("this role");
        result.Should().Contain("- I align execution with business goals", "with no bullets a stock highlight is used");
        result.Should().EndWith("Candidate");
    }

    [Fact]
    public async Task BuildCoverLetterAsync_WithDeepReview_ReturnsVersionsAndTheRecommendedLetter()
    {
        var refinement = new RefinementDto(
            DraftVersionLabels.Synthesis,
            "Too generic.",
            [
                new DraftVersionDto(DraftVersionLabels.InitialDraft, "Initial draft", "", "Dear Team, I am thrilled."),
                new DraftVersionDto(DraftVersionLabels.Synthesis, "Synthesis", "", "Dear Team, here is the evidence.")
            ]);
        var pipeline = new FakeRefinementPipeline(refinement);
        var chatClient = ChatClientMocks.ReturningText("Dear Team, I am thrilled.");
        var sut = new CoverLetterService(chatClient.Object, pipeline, NullLogger<CoverLetterService>.Instance);

        var result = await sut.BuildCoverLetterAsync(
            CreateProfile(),
            new CoverLetterContext("Engineer", "Contoso", EmptyAnalysis, [Bullet("Shipped billing.")]),
            guidance: null,
            deepReview: true,
            CancellationToken.None);

        result.Markdown.Should().Be("Dear Team, here is the evidence.");
        result.Refinement.Should().BeSameAs(refinement);
        pipeline.Requests.Should().ContainSingle().Which.GroundingQuery.Should().Be("Shipped billing.");
    }

    [Fact]
    public async Task BuildCoverLetterAsync_WithDeepReview_SkipsTheRefinementPassOnTheTemplateFallback()
    {
        var pipeline = new FakeRefinementPipeline();
        var chatClient = ChatClientMocks.Throwing(new HttpRequestException("down"));
        var sut = new CoverLetterService(chatClient.Object, pipeline, NullLogger<CoverLetterService>.Instance);

        var result = await sut.BuildCoverLetterAsync(
            CreateProfile(),
            new CoverLetterContext("Engineer", "Contoso", EmptyAnalysis, []),
            guidance: null,
            deepReview: true,
            CancellationToken.None);

        pipeline.Requests.Should().BeEmpty("there is no AI draft to critique");
        result.Refinement.Should().BeNull();
        result.Markdown.Should().StartWith("Dear Contoso Hiring Team,");
    }

    [Fact]
    public async Task BuildCoverLetterAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var chatClient = ChatClientMocks.Throwing(new OperationCanceledException(cts.Token));
        var sut = new CoverLetterService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<CoverLetterService>.Instance);

        var act = () => sut.BuildCoverLetterAsync(
            CreateProfile(),
            new CoverLetterContext(null, null, EmptyAnalysis, []),
            guidance: null,
            deepReview: false,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
