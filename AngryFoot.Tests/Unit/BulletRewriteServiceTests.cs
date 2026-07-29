using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
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
        var sut = new BulletRewriteService(chatClient.Object, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(EmptyAnalysis, [], CancellationToken.None);

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
        var sut = new BulletRewriteService(chatClient.Object, NullLogger<BulletRewriteService>.Instance);

        var selected = new[] { Ranked(rewrittenId, "original one"), Ranked(untouchedId, "original two") };
        var result = await sut.RewriteAsync(EmptyAnalysis, selected, CancellationToken.None);

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
        var sut = new BulletRewriteService(chatClient.Object, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(EmptyAnalysis, [Ranked(id, "original")], CancellationToken.None);

        result.Should().ContainSingle().Which.Text.Should().Be("original");
    }

    [Fact]
    public async Task RewriteAsync_WithUnparseableResponse_ReturnsOriginals()
    {
        var chatClient = ChatClientMocks.ReturningText("not json at all");
        var sut = new BulletRewriteService(chatClient.Object, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(EmptyAnalysis, [Ranked(Guid.NewGuid(), "keep me")], CancellationToken.None);

        result.Should().ContainSingle().Which.Text.Should().Be("keep me");
    }

    [Fact]
    public async Task RewriteAsync_WhenAiCallFails_ReturnsOriginals()
    {
        var chatClient = ChatClientMocks.Throwing(new HttpRequestException("boom"));
        var sut = new BulletRewriteService(chatClient.Object, NullLogger<BulletRewriteService>.Instance);

        var result = await sut.RewriteAsync(EmptyAnalysis, [Ranked(Guid.NewGuid(), "keep me")], CancellationToken.None);

        result.Should().ContainSingle().Which.Text.Should().Be("keep me");
    }

    [Fact]
    public async Task RewriteAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var chatClient = ChatClientMocks.Throwing(new OperationCanceledException(cts.Token));
        var sut = new BulletRewriteService(chatClient.Object, NullLogger<BulletRewriteService>.Instance);

        var act = () => sut.RewriteAsync(EmptyAnalysis, [Ranked(Guid.NewGuid(), "text")], cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
