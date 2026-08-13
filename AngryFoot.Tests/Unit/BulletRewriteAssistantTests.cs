using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

public class BulletRewriteAssistantTests
{
    private static BulletRewriteAssistant CreateSut(
        Moq.Mock<Microsoft.Extensions.AI.IChatClient> chatClient,
        FakeRefinementPipeline? pipeline = null)
        => new(chatClient.Object, pipeline ?? new FakeRefinementPipeline(), NullLogger<BulletRewriteAssistant>.Instance);

    [Fact]
    public async Task RewriteAsync_WithValidAiPayload_ReturnsRewriteAndDedupedSuggestions()
    {
        var json = """
            {"rewrittenText":" Delivered the platform migration. ","suggestions":["Add metrics"," add metrics ","Name the stack"]}
            """;
        var sut = CreateSut(ChatClientMocks.ReturningText(json));

        var result = await sut.RewriteAsync("did the migration", deepReview: false, CancellationToken.None);

        result.RewrittenText.Should().Be("Delivered the platform migration.");
        result.Suggestions.Should().HaveCount(2, "suggestions are trimmed and deduplicated case-insensitively");
    }

    [Fact]
    public async Task RewriteAsync_WithBlankAiRewrittenText_KeepsOriginalText()
    {
        var json = """{"rewrittenText":"  ","suggestions":["Something helpful"]}""";
        var sut = CreateSut(ChatClientMocks.ReturningText(json));

        var result = await sut.RewriteAsync("my original bullet", deepReview: false, CancellationToken.None);

        result.RewrittenText.Should().Be("my original bullet");
        result.Suggestions.Should().ContainSingle().Which.Should().Be("Something helpful");
    }

    [Fact]
    public async Task RewriteAsync_WithEmptyAiSuggestions_FallsBackToHeuristicSuggestions()
    {
        var json = """{"rewrittenText":"Better text.","suggestions":[]}""";
        var sut = CreateSut(ChatClientMocks.ReturningText(json));

        var result = await sut.RewriteAsync("worked on stuff", deepReview: false, CancellationToken.None);

        result.RewrittenText.Should().Be("Better text.");
        result.Suggestions.Should().NotBeEmpty("heuristic suggestions fill in when the AI offers none");
    }

    [Fact]
    public async Task RewriteAsync_WithUnparseableResponse_ReturnsHeuristicFallback()
    {
        var sut = CreateSut(ChatClientMocks.ReturningText("plain text, no json"));

        var result = await sut.RewriteAsync("worked on stuff", deepReview: false, CancellationToken.None);

        result.RewrittenText.Should().Be("Worked on stuff.", "the fallback capitalizes and adds a period");
        result.Suggestions.Should().Contain(s => s.Contains("measurable result"), "no metric is present in the bullet");
        result.Suggestions.Should().Contain(s => s.Contains("business impact"), "no outcome keyword is present");
        result.Suggestions.Should().Contain(s => s.Contains("tools/technologies"), "no known technology is present");
    }

    [Fact]
    public async Task RewriteAsync_FallbackSkipsSuggestionsAlreadySatisfiedByTheBullet()
    {
        var sut = CreateSut(ChatClientMocks.Throwing(new HttpRequestException("down")));

        var result = await sut.RewriteAsync("Improved API response time by 40% using .NET", deepReview: false, CancellationToken.None);

        result.Suggestions.Should().NotContain(s => s.Contains("measurable result"), "the bullet already has 40%");
        result.Suggestions.Should().NotContain(s => s.Contains("business impact"), "the bullet says 'Improved'");
        result.Suggestions.Should().NotContain(s => s.Contains("tools/technologies"), "the bullet names .NET and API");
    }

    [Fact]
    public async Task RewriteAsync_WithoutDeepReview_NeverRunsTheRefinementPass()
    {
        var pipeline = new FakeRefinementPipeline();
        var sut = CreateSut(ChatClientMocks.ReturningText("""{"rewrittenText":"Good.","suggestions":["x"]}"""), pipeline);

        var result = await sut.RewriteAsync("worked on stuff", deepReview: false, CancellationToken.None);

        pipeline.Requests.Should().BeEmpty();
        result.Refinement.Should().BeNull();
        result.RewrittenText.Should().Be("Good.");
    }

    [Fact]
    public async Task RewriteAsync_WithDeepReview_ReturnsVersionsAndRecommendsOneAsTheRewrite()
    {
        var refinement = new RefinementDto(
            DraftVersionLabels.Synthesis,
            "Needs a metric.",
            [
                new DraftVersionDto(DraftVersionLabels.InitialDraft, "Initial draft", "", "Good."),
                new DraftVersionDto(DraftVersionLabels.Synthesis, "Synthesis", "", "Best of both.")
            ]);
        var pipeline = new FakeRefinementPipeline(refinement);
        var sut = CreateSut(ChatClientMocks.ReturningText("""{"rewrittenText":"Good.","suggestions":["x"]}"""), pipeline);

        var result = await sut.RewriteAsync("worked on stuff", deepReview: true, CancellationToken.None);

        result.Refinement.Should().BeSameAs(refinement);
        result.RewrittenText.Should().Be("Best of both.", "the recommended version becomes the headline rewrite");

        var request = pipeline.Requests.Should().ContainSingle().Subject;
        request.Draft.Should().Be("Good.");
        request.GroundingQuery.Should().Be("worked on stuff", "grounding follows the candidate's own words, not the AI's");
    }

    [Fact]
    public async Task RewriteAsync_WithDeepReview_SkipsTheRefinementPassOnTheHeuristicFallback()
    {
        var pipeline = new FakeRefinementPipeline();
        var sut = CreateSut(ChatClientMocks.ReturningText("plain text, no json"), pipeline);

        var result = await sut.RewriteAsync("worked on stuff", deepReview: true, CancellationToken.None);

        pipeline.Requests.Should().BeEmpty("there is no AI draft to critique");
        result.Refinement.Should().BeNull();
        result.RewrittenText.Should().Be("Worked on stuff.");
    }

    [Fact]
    public async Task RewriteAsync_WithDeepReview_KeepsTheDraftWhenTheRefinementPassProducesNothing()
    {
        var sut = CreateSut(ChatClientMocks.ReturningText("""{"rewrittenText":"Good.","suggestions":["x"]}"""));

        var result = await sut.RewriteAsync("worked on stuff", deepReview: true, CancellationToken.None);

        result.RewrittenText.Should().Be("Good.");
        result.Refinement.Should().BeNull();
    }

    [Fact]
    public async Task RewriteAsync_WithWhitespaceInput_ReturnsFallbackWithoutCallingAi()
    {
        var chatClient = ChatClientMocks.ReturningText("should never be used");
        var sut = CreateSut(chatClient);

        var result = await sut.RewriteAsync("   ", deepReview: false, CancellationToken.None);

        result.RewrittenText.Should().BeEmpty();
        chatClient.Verify(
            x => x.GetResponseAsync(
                Moq.It.IsAny<IEnumerable<Microsoft.Extensions.AI.ChatMessage>>(),
                Moq.It.IsAny<Microsoft.Extensions.AI.ChatOptions?>(),
                Moq.It.IsAny<CancellationToken>()),
            Moq.Times.Never);
    }
}
