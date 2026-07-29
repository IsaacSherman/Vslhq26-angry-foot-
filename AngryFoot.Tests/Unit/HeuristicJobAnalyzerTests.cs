using AngryFoot.ApiService.Application.Generation;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AngryFoot.Tests.Unit;

public class HeuristicJobAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_WithValidAiJson_ReturnsNormalizedAnalysis()
    {
        var json = """
            {"requiredSkills":["C#"," c# ","ASP.NET Core"],"preferredSkills":["Azure"],"technologies":["blazor"],"keywords":["resume"],"experienceThemes":["Delivery"],"inferredTitle":" Senior Engineer ","inferredSeniority":"Senior"}
            """;
        var chatClient = ChatClientMocks.ReturningText(json);
        var sut = new HeuristicJobAnalyzer(chatClient.Object, NullLogger<HeuristicJobAnalyzer>.Instance);

        var result = await sut.AnalyzeAsync("Senior .NET engineer role.", CancellationToken.None);

        result.RequiredSkills.Should().BeEquivalentTo("C#", "ASP.NET Core");
        result.PreferredSkills.Should().BeEquivalentTo("Azure");
        result.Technologies.Should().BeEquivalentTo("blazor");
        result.InferredTitle.Should().Be("Senior Engineer");
        result.InferredSeniority.Should().Be("Senior");
    }

    [Fact]
    public async Task AnalyzeAsync_WithAiJsonMissingArrays_TreatsThemAsEmptyInsteadOfThrowing()
    {
        var partial = """{"requiredSkills":["C#"],"inferredSeniority":"Staff"}""";
        var chatClient = ChatClientMocks.ReturningText(partial);
        var sut = new HeuristicJobAnalyzer(chatClient.Object, NullLogger<HeuristicJobAnalyzer>.Instance);

        var result = await sut.AnalyzeAsync("Staff engineer.", CancellationToken.None);

        result.RequiredSkills.Should().BeEquivalentTo("C#");
        result.PreferredSkills.Should().BeEmpty();
        result.Keywords.Should().BeEmpty();
        result.InferredSeniority.Should().Be("Staff");
    }

    [Fact]
    public async Task AnalyzeAsync_WithUnparseableResponse_FallsBackToHeuristicsAndLogs()
    {
        var logger = new Mock<ILogger<HeuristicJobAnalyzer>>();
        var chatClient = ChatClientMocks.ReturningText("no json here");
        var sut = new HeuristicJobAnalyzer(chatClient.Object, logger.Object);

        var jobDescription = """
            Senior Backend Engineer
            Requirements: must have C# and SQL experience
            Nice to have: Docker is a plus
            Lead architecture and automation initiatives.
            """;

        var result = await sut.AnalyzeAsync(jobDescription, CancellationToken.None);

        result.Technologies.Should().Contain(["c#", "sql", "docker"]);
        result.ExperienceThemes.Should().Contain(["Leadership", "Architecture", "Automation"]);
        result.InferredSeniority.Should().Be("Senior");
        result.InferredTitle.Should().Be("Senior Backend Engineer");

        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("could not be parsed")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "an unparseable AI response must be visible in the logs");
    }

    [Fact]
    public async Task AnalyzeAsync_WhenAiCallFails_FallsBackToHeuristicsAndLogsException()
    {
        var logger = new Mock<ILogger<HeuristicJobAnalyzer>>();
        var chatClient = ChatClientMocks.Throwing(new HttpRequestException("connection refused"));
        var sut = new HeuristicJobAnalyzer(chatClient.Object, logger.Object);

        var result = await sut.AnalyzeAsync("Principal engineer role requiring python.", CancellationToken.None);

        result.Technologies.Should().Contain("python");
        result.InferredSeniority.Should().Be("Principal");

        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.Is<Exception?>(ex => ex is HttpRequestException),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "the AI failure must be logged with its exception");
    }

    [Fact]
    public async Task AnalyzeAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var chatClient = ChatClientMocks.Throwing(new OperationCanceledException(cts.Token));
        var sut = new HeuristicJobAnalyzer(chatClient.Object, NullLogger<HeuristicJobAnalyzer>.Instance);

        var act = () => sut.AnalyzeAsync("A role.", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AnalyzeAsync_HeuristicKeywords_ExcludeStopwordsAndShortTokens()
    {
        var chatClient = ChatClientMocks.ReturningText("not json");
        var sut = new HeuristicJobAnalyzer(chatClient.Object, NullLogger<HeuristicJobAnalyzer>.Instance);

        var result = await sut.AnalyzeAsync("You will work with the team on microservices and api design", CancellationToken.None);

        result.Keywords.Should().NotContain(x => x.Length < 4, "short tokens are filtered");
        result.Keywords.Should().NotContain("will").And.NotContain("with", "stopwords are filtered");
        result.Keywords.Should().Contain("microservices");
    }
}
