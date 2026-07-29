using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
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
        var sut = new CoverLetterService(chatClient.Object, NullLogger<CoverLetterService>.Instance);

        var result = await sut.BuildCoverLetterAsync(
            CreateProfile(),
            new CoverLetterContext("Engineer", "Contoso", EmptyAnalysis, []),
            CancellationToken.None);

        result.Should().Be("Dear Team, I am thrilled.");
    }

    [Fact]
    public async Task BuildCoverLetterAsync_WithEmptyAiResponse_UsesTemplateFallback()
    {
        var chatClient = ChatClientMocks.ReturningText("   ");
        var sut = new CoverLetterService(chatClient.Object, NullLogger<CoverLetterService>.Instance);

        var result = await sut.BuildCoverLetterAsync(
            CreateProfile(),
            new CoverLetterContext("Staff Engineer", "Contoso", EmptyAnalysis, [Bullet("Did a thing."), Bullet("Did another."), Bullet("Third."), Bullet("Fourth - should be cut.")]),
            CancellationToken.None);

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
        var sut = new CoverLetterService(chatClient.Object, NullLogger<CoverLetterService>.Instance);

        var result = await sut.BuildCoverLetterAsync(
            new Profile { Id = Guid.NewGuid() },
            new CoverLetterContext(null, null, EmptyAnalysis, []),
            CancellationToken.None);

        result.Should().StartWith("Dear Hiring Team,");
        result.Should().Contain("this role");
        result.Should().Contain("- I align execution with business goals", "with no bullets a stock highlight is used");
        result.Should().EndWith("Candidate");
    }

    [Fact]
    public async Task BuildCoverLetterAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var chatClient = ChatClientMocks.Throwing(new OperationCanceledException(cts.Token));
        var sut = new CoverLetterService(chatClient.Object, NullLogger<CoverLetterService>.Instance);

        var act = () => sut.BuildCoverLetterAsync(
            CreateProfile(),
            new CoverLetterContext(null, null, EmptyAnalysis, []),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
