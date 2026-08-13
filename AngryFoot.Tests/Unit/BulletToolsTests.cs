using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Mcp;
using AngryFoot.Contracts;
using AwesomeAssertions;
using ModelContextProtocol;
using Moq;

namespace AngryFoot.Tests.Unit;

public class BulletToolsTests
{
    private static BulletDto Dto(Guid? id = null, string text = "A bullet.")
        => new(id ?? Guid.NewGuid(), text, [], [], [], [], [], null, EnrichmentStateDto.Enriched, DateTime.UtcNow, DateTime.UtcNow, false);

    private readonly Mock<IBulletService> _bulletService = new();
    private readonly Mock<IBulletRewriteAssistant> _rewriteAssistant = new();

    [Fact]
    public async Task AddBulletAsync_PassesTextAndEmployerThrough()
    {
        var expected = Dto();
        _bulletService.Setup(x => x.CreateAsync(
                It.Is<CreateBulletRequest>(r => r.BulletText == "Did a thing." && r.SourceEmployer == "Acme"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await BulletTools.AddBulletAsync(_bulletService.Object, "Did a thing.", "Acme");

        result.Should().BeSameAs(expected);
        _bulletService.VerifyAll();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddBulletAsync_WithBlankText_ThrowsWithoutCallingService(string text)
    {
        var act = () => BulletTools.AddBulletAsync(_bulletService.Object, text);

        await act.Should().ThrowAsync<McpException>().WithMessage("*bulletText is required*");
        _bulletService.Verify(
            x => x.CreateAsync(It.IsAny<CreateBulletRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateBulletAsync_PassesRequestThrough()
    {
        var id = Guid.NewGuid();
        var expected = Dto(id, "New text.");
        _bulletService.Setup(x => x.UpdateAsync(
                id,
                It.Is<UpdateBulletRequest>(r => r.BulletText == "New text." && r.SourceEmployer == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await BulletTools.UpdateBulletAsync(_bulletService.Object, id, "New text.");

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task UpdateBulletAsync_WhenBulletMissing_ThrowsMcpException()
    {
        _bulletService.Setup(x => x.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateBulletRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BulletDto?)null);

        var act = () => BulletTools.UpdateBulletAsync(_bulletService.Object, Guid.NewGuid(), "text");

        await act.Should().ThrowAsync<McpException>().WithMessage("*was not found*");
    }

    [Fact]
    public async Task UpdateBulletAsync_WithBlankText_ThrowsWithoutCallingService()
    {
        var act = () => BulletTools.UpdateBulletAsync(_bulletService.Object, Guid.NewGuid(), "  ");

        await act.Should().ThrowAsync<McpException>();
        _bulletService.Verify(
            x => x.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateBulletRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RewriteBulletAsync_ReturnsAssistantResponse()
    {
        var expected = new RewriteBulletResponse("Better.", ["Add metrics"]);
        _rewriteAssistant.Setup(x => x.RewriteAsync("meh", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await BulletTools.RewriteBulletAsync(_rewriteAssistant.Object, "meh");

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task RewriteBulletAsync_PassesDeepReviewThrough()
    {
        var expected = new RewriteBulletResponse("Better.", []);
        _rewriteAssistant.Setup(x => x.RewriteAsync("meh", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await BulletTools.RewriteBulletAsync(_rewriteAssistant.Object, "meh", deepReview: true);

        result.Should().BeSameAs(expected);
    }

    [Fact]
    public async Task RewriteBulletAsync_WithBlankText_Throws()
    {
        var act = () => BulletTools.RewriteBulletAsync(_rewriteAssistant.Object, " ");

        await act.Should().ThrowAsync<McpException>();
    }

    [Fact]
    public async Task EnrichBulletAsync_WhenBulletMissing_ThrowsMcpException()
    {
        _bulletService.Setup(x => x.EnrichAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BulletDto?)null);

        var act = () => BulletTools.EnrichBulletAsync(_bulletService.Object, Guid.NewGuid());

        await act.Should().ThrowAsync<McpException>().WithMessage("*was not found*");
    }

    [Fact]
    public async Task GetBulletAsync_ReturnsBullet_OrThrowsWhenMissing()
    {
        var id = Guid.NewGuid();
        var expected = Dto(id);
        _bulletService.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        (await BulletTools.GetBulletAsync(_bulletService.Object, id)).Should().BeSameAs(expected);

        _bulletService.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BulletDto?)null);
        var act = () => BulletTools.GetBulletAsync(_bulletService.Object, Guid.NewGuid());
        await act.Should().ThrowAsync<McpException>();
    }

    [Fact]
    public async Task ListBulletsAsync_PassesAllFiltersThrough()
    {
        _bulletService.Setup(x => x.SearchAsync("s", "t", "sk", "tech", "cat", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Dto()]);

        var result = await BulletTools.ListBulletsAsync(_bulletService.Object, "s", "t", "sk", "tech", "cat");

        result.Should().ContainSingle();
        _bulletService.VerifyAll();
    }

    [Fact]
    public async Task DeleteBulletAsync_ReturnsConfirmation_OrThrowsWhenMissing()
    {
        var id = Guid.NewGuid();
        _bulletService.Setup(x => x.DeleteAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await BulletTools.DeleteBulletAsync(_bulletService.Object, id)).Should().Contain(id.ToString());

        _bulletService.Setup(x => x.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var act = () => BulletTools.DeleteBulletAsync(_bulletService.Object, Guid.NewGuid());
        await act.Should().ThrowAsync<McpException>();
    }
}
