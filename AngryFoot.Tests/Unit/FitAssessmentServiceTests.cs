using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AwesomeAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AngryFoot.Tests.Unit;

public class FitAssessmentServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private FitAssessmentService CreateSut(Mock<IChatClient> chatClient)
        => new(_database.Context, chatClient.Object, NullLogger<FitAssessmentService>.Instance);

    private void SeedBullets(params Bullet[] bullets)
    {
        _database.Context.Bullets.AddRange(bullets);
        _database.Context.SaveChanges();
    }

    private static Bullet Bullet(string text, string[]? skills = null, string[]? technologies = null)
        => new()
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            Skills = (skills ?? []).ToList(),
            Technologies = (technologies ?? []).ToList()
        };

    private static JobAnalysisDto Analysis(
        string[]? required = null,
        string[]? preferred = null,
        string[]? technologies = null)
        => new(required ?? [], preferred ?? [], technologies ?? [], [], [], null, null);

    [Fact]
    public async Task AssessAsync_WithValidAiPayload_ReturnsNormalizedAssessment()
    {
        SeedBullets(Bullet("Built C# services."));
        var json = """
            {"fitScore":150,"verdict":" Strong candidate. ","strengths":["C# - five bullets"," c# - five bullets "],"gaps":["Kubernetes"],"bulletSuggestions":["Show Kubernetes work"]}
            """;
        var sut = CreateSut(ChatClientMocks.ReturningText(json));

        var result = await sut.AssessAsync("A role.", Analysis(required: ["c#"]), CancellationToken.None);

        result.FitScore.Should().Be(100, "scores are clamped to 0-100");
        result.Verdict.Should().Be("Strong candidate.");
        result.Strengths.Should().ContainSingle("values are trimmed and deduplicated case-insensitively");
        result.Gaps.Should().ContainSingle().Which.Should().Be("Kubernetes");
        result.BulletSuggestions.Should().ContainSingle().Which.Should().Contain("Kubernetes");
    }

    [Fact]
    public async Task AssessAsync_WithUnparseableAiResponse_FallsBackToCoverageHeuristic()
    {
        SeedBullets(
            Bullet("Optimized the data layer.", skills: ["C#"]),
            Bullet("Improved deployment speed by 40% using c# tooling."));
        var sut = CreateSut(ChatClientMocks.ReturningText("no json here"));

        var result = await sut.AssessAsync("A role.", Analysis(required: ["c#", "kubernetes"]), CancellationToken.None);

        result.FitScore.Should().Be(50, "one of two equally-weighted requirements is covered");
        result.Strengths.Should().ContainSingle().Which.Should().Contain("c#").And.Contain("2 bullets");
        result.Gaps.Should().ContainSingle().Which.Should().Contain("kubernetes");
        result.BulletSuggestions.Should().ContainSingle().Which.Should().Contain("kubernetes");
    }

    [Fact]
    public async Task AssessAsync_WhenAiFails_FallsBackToCoverageHeuristic()
    {
        SeedBullets(Bullet("Shipped Azure workloads.", technologies: ["Azure"]));
        var sut = CreateSut(ChatClientMocks.Throwing(new HttpRequestException("down")));

        var result = await sut.AssessAsync("A role.", Analysis(technologies: ["azure"]), CancellationToken.None);

        result.FitScore.Should().Be(100);
        result.Verdict.Should().Contain("Strong match");
    }

    [Fact]
    public async Task AssessAsync_WhenCancelled_PropagatesCancellation()
    {
        SeedBullets(Bullet("Anything."));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var sut = CreateSut(ChatClientMocks.Throwing(new OperationCanceledException(cts.Token)));

        var act = () => sut.AssessAsync("A role.", Analysis(required: ["c#"]), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AssessAsync_WithEmptyBulletLibrary_ReturnsZeroScoreWithoutCallingAi()
    {
        var chatClient = ChatClientMocks.ReturningText("should never be used");
        var sut = CreateSut(chatClient);

        var result = await sut.AssessAsync("A role.", Analysis(required: ["c#", "azure"]), CancellationToken.None);

        result.FitScore.Should().Be(0);
        result.Verdict.Should().Contain("empty", "the verdict must tell the user why there is nothing to assess");
        result.Gaps.Should().HaveCount(2);
        result.BulletSuggestions.Should().HaveCount(2);
        chatClient.Verify(
            x => x.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "there is no evidence for the AI to assess");
    }

    [Fact]
    public async Task AssessAsync_HeuristicWeighsRequiredSkillsDoublePreferred()
    {
        // Covered: one preferred (weight 1). Uncovered: one required (weight 2). Score = 1/3.
        SeedBullets(Bullet("Used Docker daily.", technologies: ["Docker"]));
        var sut = CreateSut(ChatClientMocks.ReturningText("not json"));

        var result = await sut.AssessAsync("A role.", Analysis(required: ["kubernetes"], preferred: ["docker"]), CancellationToken.None);

        result.FitScore.Should().Be(33, "preferred skills weigh half of required skills");
        result.Gaps.First().Should().Contain("kubernetes", "gaps are ordered by requirement weight");
    }

    [Fact]
    public async Task AssessAsync_HeuristicWithNoExtractedRequirements_ReturnsNeutralUnscoredVerdict()
    {
        SeedBullets(Bullet("A bullet."));
        var sut = CreateSut(ChatClientMocks.ReturningText("not json"));

        var result = await sut.AssessAsync("A role.", Analysis(), CancellationToken.None);

        result.FitScore.Should().Be(50);
        result.Verdict.Should().Contain("cannot be scored");
        result.Strengths.Should().BeEmpty();
        result.Gaps.Should().BeEmpty();
    }

    [Fact]
    public async Task AssessAsync_WithEmptyAiPayload_FallsBackRatherThanReturningBlankAssessment()
    {
        SeedBullets(Bullet("Built C# services."));
        var json = """{"fitScore":null,"verdict":"","strengths":[],"gaps":[],"bulletSuggestions":[]}""";
        var sut = CreateSut(ChatClientMocks.ReturningText(json));

        var result = await sut.AssessAsync("A role.", Analysis(required: ["c#"]), CancellationToken.None);

        result.Verdict.Should().Contain("Strong match", "an empty AI payload is discarded in favor of the heuristic");
        result.FitScore.Should().Be(100);
    }
}
