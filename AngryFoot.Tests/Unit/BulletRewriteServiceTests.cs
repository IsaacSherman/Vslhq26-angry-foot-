using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

public class BulletRewriteServiceTests
{
    private static readonly JobAnalysisDto EmptyAnalysis = new([], [], [], [], [], null, null);

    private static RankedBullet Ranked(Guid id, string text) =>
        new(new Bullet { Id = id, BulletText = text }, Score: 1);

    [Fact]
    public async Task RewriteAsync_WithEmptySelection_ReturnsEmptyWithoutCallingAi()
    {
        var chatClient = ChatClientMocks.ReturningText("should never be used");
        var sut = new BulletRewriteService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<BulletRewriteService>.Instance);

        var result = (await sut.RewriteAsync(EmptyAnalysis, [], deepReview: false, CancellationToken.None)).Recommended;

        result.Should().BeEmpty();
        chatClient.Verify(
            x => x.GetResponseAsync(
                Moq.It.IsAny<IEnumerable<Microsoft.Extensions.AI.ChatMessage>>(),
                Moq.It.IsAny<Microsoft.Extensions.AI.ChatOptions?>(),
                Moq.It.IsAny<CancellationToken>()),
            Moq.Times.Never);
    }

    [Fact]
    public async Task RewriteAsync_AppliesRewritesByBulletId_AndKeepsOriginalForUnknownIds()
    {
        var rewrittenId = Guid.NewGuid();
        var untouchedId = Guid.NewGuid();
        var json = $$"""[{"bulletId":"{{rewrittenId}}","rewritten":" Polished text. "}]""";
        var chatClient = ChatClientMocks.ReturningText(json);
        var sut = new BulletRewriteService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<BulletRewriteService>.Instance);

        var selected = new[] { Ranked(rewrittenId, "original one"), Ranked(untouchedId, "original two") };
        var result = (await sut.RewriteAsync(EmptyAnalysis, selected, deepReview: false, CancellationToken.None)).Recommended;

        result.Should().HaveCount(2);
        result[0].Text.Should().Be("Polished text.", "the AI rewrite is trimmed and applied by id");
        result[1].Text.Should().Be("original two", "bullets the AI did not address keep their original text");
    }

    [Fact]
    public async Task RewriteAsync_IgnoresRewritesWithEmptyGuidOrBlankText()
    {
        var id = Guid.NewGuid();
        var json = $$"""
            [{"bulletId":"{{Guid.Empty}}","rewritten":"junk"},{"bulletId":"{{id}}","rewritten":"   "}]
            """;
        var chatClient = ChatClientMocks.ReturningText(json);
        var sut = new BulletRewriteService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<BulletRewriteService>.Instance);

        var result = (await sut.RewriteAsync(EmptyAnalysis, [Ranked(id, "original")], deepReview: false, CancellationToken.None)).Recommended;

        result.Should().ContainSingle().Which.Text.Should().Be("original");
    }

    [Fact]
    public async Task RewriteAsync_WithUnparseableResponse_ReturnsOriginals()
    {
        var chatClient = ChatClientMocks.ReturningText("not json at all");
        var sut = new BulletRewriteService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<BulletRewriteService>.Instance);

        var result = (await sut.RewriteAsync(EmptyAnalysis, [Ranked(Guid.NewGuid(), "keep me")], deepReview: false, CancellationToken.None)).Recommended;

        result.Should().ContainSingle().Which.Text.Should().Be("keep me");
    }

    [Fact]
    public async Task RewriteAsync_WhenAiCallFails_ReturnsOriginals()
    {
        var chatClient = ChatClientMocks.Throwing(new HttpRequestException("boom"));
        var sut = new BulletRewriteService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<BulletRewriteService>.Instance);

        var result = (await sut.RewriteAsync(EmptyAnalysis, [Ranked(Guid.NewGuid(), "keep me")], deepReview: false, CancellationToken.None)).Recommended;

        result.Should().ContainSingle().Which.Text.Should().Be("keep me");
    }

    [Fact]
    public async Task RewriteAsync_WithDeepReview_ParsesEveryVersionBackIntoARewriteSet()
    {
        var id = Guid.NewGuid();
        var pipeline = new FakeRefinementPipeline(new RefinementDto(
            DraftVersionLabels.Synthesis,
            "Vague.",
            [
                new DraftVersionDto(DraftVersionLabels.InitialDraft, "Initial draft", "", $$"""[{"bulletId":"{{id}}","rewritten":"Draft text."}]"""),
                new DraftVersionDto(DraftVersionLabels.Synthesis, "Synthesis", "", $$"""[{"bulletId":"{{id}}","rewritten":"Merged text."}]""")
            ]));
        var chatClient = ChatClientMocks.ReturningText($$"""[{"bulletId":"{{id}}","rewritten":"Draft text."}]""");
        var sut = new BulletRewriteService(chatClient.Object, pipeline, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(EmptyAnalysis, [Ranked(id, "original")], deepReview: true, CancellationToken.None);

        result.Recommended.Should().ContainSingle().Which.Text.Should().Be("Merged text.");
        result.VersionBullets.Keys.Should().BeEquivalentTo([DraftVersionLabels.InitialDraft, DraftVersionLabels.Synthesis]);
        result.VersionBullets[DraftVersionLabels.InitialDraft][0].Text.Should().Be("Draft text.");
        pipeline.Requests.Should().ContainSingle("the whole set is refined as one draft, not once per bullet");
    }

    [Fact]
    public async Task RewriteAsync_WithDeepReview_DropsVersionsThatAreNotParseableRewriteSets()
    {
        var id = Guid.NewGuid();
        var pipeline = new FakeRefinementPipeline(new RefinementDto(
            DraftVersionLabels.Synthesis,
            "Vague.",
            [
                new DraftVersionDto(DraftVersionLabels.InitialDraft, "Initial draft", "", $$"""[{"bulletId":"{{id}}","rewritten":"Draft text."}]"""),
                new DraftVersionDto(DraftVersionLabels.AuthorRevision, "Author's revision", "", $$"""[{"bulletId":"{{id}}","rewritten":"Revised text."}]"""),
                new DraftVersionDto(DraftVersionLabels.Synthesis, "Synthesis", "", "the arbiter answered in prose")
            ]));
        var chatClient = ChatClientMocks.ReturningText($$"""[{"bulletId":"{{id}}","rewritten":"Draft text."}]""");
        var sut = new BulletRewriteService(chatClient.Object, pipeline, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(EmptyAnalysis, [Ranked(id, "original")], deepReview: true, CancellationToken.None);

        result.Refinement!.Versions.Select(x => x.Label).Should().Equal(
            DraftVersionLabels.InitialDraft, DraftVersionLabels.AuthorRevision);
        result.Refinement.RecommendedLabel.Should().Be(
            DraftVersionLabels.InitialDraft,
            "the recommended version was dropped, and v1's JSON is the one we wrote ourselves");
        result.Recommended[0].Text.Should().Be("Draft text.");
    }

    [Fact]
    public async Task RewriteAsync_WithDeepReview_KeepsTheDraftWhenNoAlternativeSurvives()
    {
        var id = Guid.NewGuid();
        var pipeline = new FakeRefinementPipeline(new RefinementDto(
            DraftVersionLabels.InitialDraft,
            "Vague.",
            [
                new DraftVersionDto(DraftVersionLabels.InitialDraft, "Initial draft", "", $$"""[{"bulletId":"{{id}}","rewritten":"Draft text."}]"""),
                new DraftVersionDto(DraftVersionLabels.CriticAlternative, "Reviewer's alternative", "", "prose, not a rewrite set")
            ]));
        var chatClient = ChatClientMocks.ReturningText($$"""[{"bulletId":"{{id}}","rewritten":"Draft text."}]""");
        var sut = new BulletRewriteService(chatClient.Object, pipeline, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(EmptyAnalysis, [Ranked(id, "original")], deepReview: true, CancellationToken.None);

        result.Refinement.Should().BeNull("one surviving version is not a choice");
        result.Recommended[0].Text.Should().Be("Draft text.");
    }

    [Fact]
    public async Task RewriteAsync_WithDeepReview_SkipsTheRefinementPassWhenTheRewriteFellBack()
    {
        var pipeline = new FakeRefinementPipeline();
        var chatClient = ChatClientMocks.ReturningText("not json at all");
        var sut = new BulletRewriteService(chatClient.Object, pipeline, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(EmptyAnalysis, [Ranked(Guid.NewGuid(), "keep me")], deepReview: true, CancellationToken.None);

        pipeline.Requests.Should().BeEmpty();
        result.Recommended.Should().ContainSingle().Which.Text.Should().Be("keep me");
    }

    [Fact]
    public async Task RewriteAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var chatClient = ChatClientMocks.Throwing(new OperationCanceledException(cts.Token));
        var sut = new BulletRewriteService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<BulletRewriteService>.Instance);

        var act = () => sut.RewriteAsync(EmptyAnalysis, [Ranked(Guid.NewGuid(), "text")], deepReview: false, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
