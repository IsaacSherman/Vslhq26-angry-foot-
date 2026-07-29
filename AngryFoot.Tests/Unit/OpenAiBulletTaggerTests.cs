using AngryFoot.ApiService.Ai;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

public class OpenAiBulletTaggerTests
{
    private static OpenAiBulletTagger CreateSut(Moq.Mock<Microsoft.Extensions.AI.IChatClient> chatClient)
        => new(chatClient.Object, NullLogger<OpenAiBulletTagger>.Instance);

    [Fact]
    public async Task TagAsync_WithValidAiJson_ReturnsNormalizedAiResult()
    {
        var json = """
            {"skills":[" API Design ","api design"],"technologies":[".NET"],"tags":["Impact"],"jobCategories":["Backend Engineering"],"impact":["30%"]}
            """;
        var chatClient = ChatClientMocks.ReturningText(json);
        var sut = CreateSut(chatClient);

        var result = await sut.TagAsync("Improved API throughput by 30% using .NET.", CancellationToken.None);

        result.Skills.Should().ContainSingle().Which.Should().Be("API Design", "values are trimmed and deduplicated case-insensitively");
        result.Technologies.Should().ContainSingle().Which.Should().Be(".NET");
        result.Tags.Should().ContainSingle().Which.Should().Be("Impact");
        result.JobCategories.Should().ContainSingle().Which.Should().Be("Backend Engineering");
        result.Impact.Should().ContainSingle().Which.Should().Be("30%");
    }

    [Fact]
    public async Task TagAsync_WithAiJsonWrappedInCodeFence_StillParses()
    {
        var fenced = """
            ```json
            {"skills":["Automation"],"technologies":[],"tags":[],"jobCategories":[],"impact":[]}
            ```
            """;
        var chatClient = ChatClientMocks.ReturningText(fenced);
        var sut = CreateSut(chatClient);

        var result = await sut.TagAsync("Automated the deployment pipeline.", CancellationToken.None);

        result.Skills.Should().Contain("Automation");
    }

    [Fact]
    public async Task TagAsync_WithUnparseableResponse_FallsBackToHeuristics()
    {
        var chatClient = ChatClientMocks.ReturningText("Sorry, I cannot help with that.");
        var sut = CreateSut(chatClient);

        var result = await sut.TagAsync("Implemented automated testing in C# and Azure, reducing defects by 40%.", CancellationToken.None);

        result.Technologies.Should().Contain(["c#", "azure"], "heuristics detect known technologies");
        result.Skills.Should().Contain("Automation").And.Contain("Testing");
        result.Tags.Should().Contain("Quantified Results", "the bullet contains a percentage");
        result.Impact.Should().Contain("40%");
    }

    [Fact]
    public async Task TagAsync_WithEmptyAiMetadata_FallsBackToHeuristics()
    {
        var emptyJson = """{"skills":[],"technologies":[],"tags":[],"jobCategories":[],"impact":[]}""";
        var chatClient = ChatClientMocks.ReturningText(emptyJson);
        var sut = CreateSut(chatClient);

        var result = await sut.TagAsync("Lead performance reviews for the backend team.", CancellationToken.None);

        result.Skills.Should().Contain("Leadership", "an empty AI result is discarded in favor of heuristics");
        result.JobCategories.Should().Contain("Backend Engineering");
    }

    [Fact]
    public async Task TagAsync_WithAiJsonMissingFields_TreatsMissingListsAsEmpty()
    {
        var partialJson = """{"skills":["Delivery"]}""";
        var chatClient = ChatClientMocks.ReturningText(partialJson);
        var sut = CreateSut(chatClient);

        var result = await sut.TagAsync("Shipped the thing.", CancellationToken.None);

        result.Skills.Should().ContainSingle().Which.Should().Be("Delivery");
        result.Technologies.Should().BeEmpty();
    }

    [Fact]
    public async Task TagAsync_WhenAiCallFails_FallsBackToHeuristics()
    {
        var chatClient = ChatClientMocks.Throwing(new HttpRequestException("boom"));
        var sut = CreateSut(chatClient);

        var result = await sut.TagAsync("Designed the API architecture in .NET.", CancellationToken.None);

        result.Technologies.Should().Contain(".net");
        result.Skills.Should().Contain("API Design").And.Contain("Architecture");
    }

    [Fact]
    public async Task TagAsync_WhenAiTimesOutButCallerNotCancelled_FallsBackToHeuristics()
    {
        // An OperationCanceledException while the caller's token is still live is the timeout path.
        var chatClient = ChatClientMocks.Throwing(new OperationCanceledException());
        var sut = CreateSut(chatClient);

        var result = await sut.TagAsync("Mentored junior developers.", CancellationToken.None);

        result.Skills.Should().Contain("Mentorship");
    }

    [Fact]
    public async Task TagAsync_WhenCallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var chatClient = ChatClientMocks.Throwing(new OperationCanceledException(cts.Token));
        var sut = CreateSut(chatClient);

        var act = () => sut.TagAsync("Anything.", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("caller cancellation must not be swallowed into a fallback");
    }

    [Fact]
    public async Task TagAsync_NormalizesOversizedAiLists_ToTwentyFiveValues()
    {
        var skills = string.Join(",", Enumerable.Range(1, 40).Select(i => $"\"Skill {i}\""));
        var json = $$"""{"skills":[{{skills}}],"technologies":[],"tags":[],"jobCategories":[],"impact":[]}""";
        var chatClient = ChatClientMocks.ReturningText(json);
        var sut = CreateSut(chatClient);

        var result = await sut.TagAsync("A bullet.", CancellationToken.None);

        result.Skills.Should().HaveCount(25, "values are capped at 25 per list");
    }
}
