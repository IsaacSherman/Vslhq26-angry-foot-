using AngryFoot.ApiService.Application.Bullets;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

public class BulletRewriteAssistantTests
{
    private static BulletRewriteAssistant CreateSut(Moq.Mock<Microsoft.Extensions.AI.IChatClient> chatClient)
        => new(chatClient.Object, NullLogger<BulletRewriteAssistant>.Instance);

    [Fact]
    public async Task RewriteAsync_WithValidAiPayload_ReturnsRewriteAndDedupedSuggestions()
    {
        var json = """
            {"rewrittenText":" Delivered the platform migration. ","suggestions":["Add metrics"," add metrics ","Name the stack"]}
            """;
        var sut = CreateSut(ChatClientMocks.ReturningText(json));

        var result = await sut.RewriteAsync("did the migration", CancellationToken.None);

        result.RewrittenText.Should().Be("Delivered the platform migration.");
        result.Suggestions.Should().HaveCount(2, "suggestions are trimmed and deduplicated case-insensitively");
    }

    [Fact]
    public async Task RewriteAsync_WithBlankAiRewrittenText_KeepsOriginalText()
    {
        var json = """{"rewrittenText":"  ","suggestions":["Something helpful"]}""";
        var sut = CreateSut(ChatClientMocks.ReturningText(json));

        var result = await sut.RewriteAsync("my original bullet", CancellationToken.None);

        result.RewrittenText.Should().Be("my original bullet");
        result.Suggestions.Should().ContainSingle().Which.Should().Be("Something helpful");
    }

    [Fact]
    public async Task RewriteAsync_WithEmptyAiSuggestions_FallsBackToHeuristicSuggestions()
    {
        var json = """{"rewrittenText":"Better text.","suggestions":[]}""";
        var sut = CreateSut(ChatClientMocks.ReturningText(json));

        var result = await sut.RewriteAsync("worked on stuff", CancellationToken.None);

        result.RewrittenText.Should().Be("Better text.");
        result.Suggestions.Should().NotBeEmpty("heuristic suggestions fill in when the AI offers none");
    }

    [Fact]
    public async Task RewriteAsync_WithUnparseableResponse_ReturnsHeuristicFallback()
    {
        var sut = CreateSut(ChatClientMocks.ReturningText("plain text, no json"));

        var result = await sut.RewriteAsync("worked on stuff", CancellationToken.None);

        result.RewrittenText.Should().Be("Worked on stuff.", "the fallback capitalizes and adds a period");
        result.Suggestions.Should().Contain(s => s.Contains("measurable result"), "no metric is present in the bullet");
        result.Suggestions.Should().Contain(s => s.Contains("business impact"), "no outcome keyword is present");
        result.Suggestions.Should().Contain(s => s.Contains("tools/technologies"), "no known technology is present");
    }

    [Fact]
    public async Task RewriteAsync_FallbackSkipsSuggestionsAlreadySatisfiedByTheBullet()
    {
        var sut = CreateSut(ChatClientMocks.Throwing(new HttpRequestException("down")));

        var result = await sut.RewriteAsync("Improved API response time by 40% using .NET", CancellationToken.None);

        result.Suggestions.Should().NotContain(s => s.Contains("measurable result"), "the bullet already has 40%");
        result.Suggestions.Should().NotContain(s => s.Contains("business impact"), "the bullet says 'Improved'");
        result.Suggestions.Should().NotContain(s => s.Contains("tools/technologies"), "the bullet names .NET and API");
    }

    [Fact]
    public async Task RewriteAsync_WithWhitespaceInput_ReturnsFallbackWithoutCallingAi()
    {
        var chatClient = ChatClientMocks.ReturningText("should never be used");
        var sut = CreateSut(chatClient);

        var result = await sut.RewriteAsync("   ", CancellationToken.None);

        result.RewrittenText.Should().BeEmpty();
        chatClient.Verify(
            x => x.GetResponseAsync(
                Moq.It.IsAny<IEnumerable<Microsoft.Extensions.AI.ChatMessage>>(),
                Moq.It.IsAny<Microsoft.Extensions.AI.ChatOptions?>(),
                Moq.It.IsAny<CancellationToken>()),
            Moq.Times.Never);
    }
}
