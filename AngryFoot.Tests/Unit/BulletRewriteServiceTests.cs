using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

public class BulletRewriteServiceTests
{
    /// <summary>
    /// A posting with nothing extracted from it. These tests are about the rewrite mechanics, which
    /// are the same whichever target the caller passes.
    /// </summary>
    private static readonly RewriteTarget PostingTarget =
        RewriteTarget.ForPosting(new JobAnalysisDto([], [], [], [], [], null, null));

    /// <summary>
    /// Wraps a bullet array in the object envelope the first draft call now returns; a strict JSON
    /// schema must be rooted in an object. The refinement stages still exchange bare arrays.
    /// </summary>
    private static string Envelope(string bulletArrayJson) => $$"""{"bullets":{{bulletArrayJson}}}""";

    private static RankedBullet Ranked(Guid id, string text) =>
        new(new Bullet { Id = id, BulletText = text }, Score: 1);

    [Fact]
    public async Task RewriteAsync_WithEmptySelection_ReturnsEmptyWithoutCallingAi()
    {
        var chatClient = ChatClientMocks.ReturningText("should never be used");
        var sut = new BulletRewriteService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<BulletRewriteService>.Instance);

        var result = (await sut.RewriteAsync(PostingTarget, [], bench: [], guidance: null, deepReview: false, CancellationToken.None)).Recommended;

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
        var chatClient = ChatClientMocks.ReturningText(Envelope(json));
        var sut = new BulletRewriteService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<BulletRewriteService>.Instance);

        var selected = new[] { Ranked(rewrittenId, "original one"), Ranked(untouchedId, "original two") };
        var result = (await sut.RewriteAsync(PostingTarget, selected, bench: [], guidance: null, deepReview: false, CancellationToken.None)).Recommended;

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
        var chatClient = ChatClientMocks.ReturningText(Envelope(json));
        var sut = new BulletRewriteService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<BulletRewriteService>.Instance);

        var result = (await sut.RewriteAsync(PostingTarget, [Ranked(id, "original")], bench: [], guidance: null, deepReview: false, CancellationToken.None)).Recommended;

        result.Should().ContainSingle().Which.Text.Should().Be("original");
    }

    [Fact]
    public async Task RewriteAsync_WithUnparseableResponse_ReturnsOriginals()
    {
        var chatClient = ChatClientMocks.ReturningText("not json at all");
        var sut = new BulletRewriteService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<BulletRewriteService>.Instance);

        var result = (await sut.RewriteAsync(PostingTarget, [Ranked(Guid.NewGuid(), "keep me")], bench: [], guidance: null, deepReview: false, CancellationToken.None)).Recommended;

        result.Should().ContainSingle().Which.Text.Should().Be("keep me");
    }

    [Fact]
    public async Task RewriteAsync_WhenAiCallFails_ReturnsOriginals()
    {
        var chatClient = ChatClientMocks.Throwing(new HttpRequestException("boom"));
        var sut = new BulletRewriteService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<BulletRewriteService>.Instance);

        var result = (await sut.RewriteAsync(PostingTarget, [Ranked(Guid.NewGuid(), "keep me")], bench: [], guidance: null, deepReview: false, CancellationToken.None)).Recommended;

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
        var chatClient = ChatClientMocks.ReturningText(Envelope($$"""[{"bulletId":"{{id}}","rewritten":"Draft text."}]"""));
        var sut = new BulletRewriteService(chatClient.Object, pipeline, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(PostingTarget, [Ranked(id, "original")], bench: [], guidance: null, deepReview: true, CancellationToken.None);

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
        var chatClient = ChatClientMocks.ReturningText(Envelope($$"""[{"bulletId":"{{id}}","rewritten":"Draft text."}]"""));
        var sut = new BulletRewriteService(chatClient.Object, pipeline, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(PostingTarget, [Ranked(id, "original")], bench: [], guidance: null, deepReview: true, CancellationToken.None);

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
        var chatClient = ChatClientMocks.ReturningText(Envelope($$"""[{"bulletId":"{{id}}","rewritten":"Draft text."}]"""));
        var sut = new BulletRewriteService(chatClient.Object, pipeline, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(PostingTarget, [Ranked(id, "original")], bench: [], guidance: null, deepReview: true, CancellationToken.None);

        result.Refinement.Should().BeNull("one surviving version is not a choice");
        result.Recommended[0].Text.Should().Be("Draft text.");
    }

    [Fact]
    public async Task RewriteAsync_WithDeepReview_SkipsTheRefinementPassWhenTheRewriteFellBack()
    {
        var pipeline = new FakeRefinementPipeline();
        var chatClient = ChatClientMocks.ReturningText("not json at all");
        var sut = new BulletRewriteService(chatClient.Object, pipeline, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(PostingTarget, [Ranked(Guid.NewGuid(), "keep me")], bench: [], guidance: null, deepReview: true, CancellationToken.None);

        pipeline.Requests.Should().BeEmpty();
        result.Recommended.Should().ContainSingle().Which.Text.Should().Be("keep me");
    }

    [Fact]
    public async Task RewriteAsync_WithDeepReview_HonoursTheOrderAVersionComesBackIn()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var reordered = $$"""
            [{"bulletId":"{{second}}","rewritten":"Now leading."},{"bulletId":"{{first}}","rewritten":"Now second."}]
            """;
        var draft = $$"""
            [{"bulletId":"{{first}}","rewritten":"A."},{"bulletId":"{{second}}","rewritten":"B."}]
            """;
        var pipeline = new FakeRefinementPipeline(Versions(draft, reordered));
        var chatClient = ChatClientMocks.ReturningText(Envelope(draft));
        var sut = new BulletRewriteService(chatClient.Object, pipeline, NullLogger<BulletRewriteService>.Instance);

        var selected = new[] { Ranked(first, "one"), Ranked(second, "two") };
        var result = await sut.RewriteAsync(PostingTarget, selected, bench: [], guidance: null, deepReview: true, CancellationToken.None);

        result.Recommended.Select(x => x.Bullet.Id).Should().Equal(
            new[] { second, first }, "a resume is read top-down, so sequencing is the refinement's call");
        result.Recommended[0].Text.Should().Be("Now leading.");
        result.VersionBullets[DraftVersionLabels.InitialDraft].Select(x => x.Bullet.Id).Should().Equal(
            new[] { first, second }, "the initial draft keeps the ranker's order");
    }

    [Fact]
    public async Task RewriteAsync_WithDeepReview_CanSwapInABenchBullet()
    {
        var selectedId = Guid.NewGuid();
        var benchId = Guid.NewGuid();
        var draft = $$"""[{"bulletId":"{{selectedId}}","rewritten":"The weak one."}]""";
        var swapped = $$"""[{"bulletId":"{{benchId}}","rewritten":"The stronger one."}]""";
        var pipeline = new FakeRefinementPipeline(Versions(draft, swapped));
        var chatClient = ChatClientMocks.ReturningText(Envelope(draft));
        var sut = new BulletRewriteService(chatClient.Object, pipeline, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(
            PostingTarget,
            [Ranked(selectedId, "weak")],
            [Ranked(benchId, "strong")],
            guidance: null,
            deepReview: true,
            CancellationToken.None);

        result.Recommended.Should().ContainSingle().Which.Bullet.Id.Should().Be(benchId);
        result.Recommended[0].Text.Should().Be("The stronger one.");
        pipeline.Requests.Should().ContainSingle().Which.SourceMaterial.Should().Contain(
            "strong", "the bench has to be in the prompt for the agents to swap from it");
    }

    [Fact]
    public async Task RewriteAsync_WithDeepReview_IgnoresBulletsOutsideTheCandidatePool()
    {
        var id = Guid.NewGuid();
        var draft = $$"""[{"bulletId":"{{id}}","rewritten":"Real bullet."}]""";
        var hallucinated = $$"""
            [{"bulletId":"{{Guid.NewGuid()}}","rewritten":"Invented achievement."},{"bulletId":"{{id}}","rewritten":"Real bullet, polished."}]
            """;
        var pipeline = new FakeRefinementPipeline(Versions(draft, hallucinated));
        var chatClient = ChatClientMocks.ReturningText(Envelope(draft));
        var sut = new BulletRewriteService(chatClient.Object, pipeline, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(PostingTarget, [Ranked(id, "real")], bench: [], guidance: null, deepReview: true, CancellationToken.None);

        result.Recommended.Should().ContainSingle().Which.Text.Should().Be(
            "Real bullet, polished.",
            "an id the candidate does not own is a hallucination, not a bullet");
    }

    [Fact]
    public async Task RewriteAsync_WithDeepReview_CapsAVersionAtTheResumesBulletCount()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var draft = $$"""[{"bulletId":"{{first}}","rewritten":"A."}]""";
        var padded = $$"""
            [{"bulletId":"{{first}}","rewritten":"A."},{"bulletId":"{{second}}","rewritten":"B."},{"bulletId":"{{first}}","rewritten":"A again."}]
            """;
        var pipeline = new FakeRefinementPipeline(Versions(draft, padded));
        var chatClient = ChatClientMocks.ReturningText(Envelope(draft));
        var sut = new BulletRewriteService(chatClient.Object, pipeline, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(
            PostingTarget, [Ranked(first, "one")], [Ranked(second, "two")], guidance: null, deepReview: true, CancellationToken.None);

        result.Recommended.Should().ContainSingle("the resume holds one bullet, so a padded version is trimmed to one");
    }

    [Fact]
    public async Task RewriteAsync_PassesGuidanceToTheDraftAndTheRefinement()
    {
        var id = Guid.NewGuid();
        var draft = $$"""[{"bulletId":"{{id}}","rewritten":"Draft."}]""";
        var pipeline = new FakeRefinementPipeline(Versions(draft, $$"""[{"bulletId":"{{id}}","rewritten":"Merged."}]"""));
        var chatClient = ChatClientMocks.ReturningText(Envelope(draft));
        var sut = new BulletRewriteService(chatClient.Object, pipeline, NullLogger<BulletRewriteService>.Instance);

        await sut.RewriteAsync(
            PostingTarget, [Ranked(id, "one")], bench: [], guidance: "ACME was a 4-person startup", deepReview: true, CancellationToken.None);

        pipeline.Requests.Should().ContainSingle().Which.UserGuidance.Should().Be("ACME was a 4-person startup");
        chatClient.Verify(
            x => x.GetResponseAsync(
                Moq.It.Is<IEnumerable<Microsoft.Extensions.AI.ChatMessage>>(
                    messages => messages.Any(m => m.Text.Contains("ACME was a 4-person startup"))),
                Moq.It.IsAny<Microsoft.Extensions.AI.ChatOptions?>(),
                Moq.It.IsAny<CancellationToken>()),
            Moq.Times.Once,
            "guidance shapes the first draft too, so it is useful without deep review");
    }

    private static RefinementDto Versions(string draftJson, string synthesisJson) => new(
        DraftVersionLabels.Synthesis,
        "Vague.",
        [
            new DraftVersionDto(DraftVersionLabels.InitialDraft, "Initial draft", "", draftJson),
            new DraftVersionDto(DraftVersionLabels.Synthesis, "Synthesis", "", synthesisJson)
        ]);

    [Fact]
    public async Task RewriteAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var chatClient = ChatClientMocks.Throwing(new OperationCanceledException(cts.Token));
        var sut = new BulletRewriteService(chatClient.Object, new FakeRefinementPipeline(), NullLogger<BulletRewriteService>.Instance);

        var act = () => sut.RewriteAsync(PostingTarget, [Ranked(Guid.NewGuid(), "text")], bench: [], guidance: null, deepReview: false, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
