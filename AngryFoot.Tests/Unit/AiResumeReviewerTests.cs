using AngryFoot.ApiService.Application.Review;
using AngryFoot.ApiService.Domain;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// Mostly about what the reviewer is not allowed to do: keep a note that points at a bullet it was
/// never shown, raise an observation about the resume that cites nothing in it, or take the
/// deterministic report down with it when the call goes wrong.
/// </summary>
public class AiResumeReviewerTests
{
    private static readonly Bullet[] Pool =
    [
        new() { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), BulletText = "Led the billing rewrite." },
        new() { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), BulletText = "Mentored four engineers." }
    ];

    private static AiResumeReviewer CreateSut(string response)
        => new(ChatClientMocks.ReturningText(response).Object, NullLogger<AiResumeReviewer>.Instance);

    private static Task<ResumeReview?> ReviewAsync(AiResumeReviewer sut)
        => sut.ReviewAsync(Pool, [], TestContext.Current.CancellationToken);

    [Fact]
    public async Task ReviewAsync_KeepsASummaryAndTheNotesThatPointAtRealBullets()
    {
        var sut = CreateSut("""
            {
              "summary": "The document is specific about scope and vague about outcome.",
              "spotChecks": [{ "message": "Two bullets describe the same project.", "reasoning": "Both name billing.", "bullets": [0, 1] }],
              "bulletNotes": [{ "bullet": 0, "suggestions": ["Name what the rewrite changed."] }]
            }
            """);

        var review = await ReviewAsync(sut);

        review!.Summary.Should().Be("The document is specific about scope and vague about outcome.");
        review.SpotChecks.Should().ContainSingle()
            .Which.BulletIds.Should().Equal(Pool[0].Id, Pool[1].Id);
        review.BulletSuggestions[0].Should().Equal("Name what the rewrite changed.");
    }

    [Fact]
    public async Task ReviewAsync_NoteForABulletItWasNotShownIsDropped()
    {
        var sut = CreateSut("""
            {
              "summary": "Fine.",
              "bulletNotes": [
                { "bullet": 7, "suggestions": ["Add a metric."] },
                { "bullet": 1, "suggestions": ["Say how many stayed."] }
              ]
            }
            """);

        var review = await ReviewAsync(sut);

        review!.BulletSuggestions.Should().ContainKey(1).And.NotContainKey(7,
            "advice about a bullet that does not exist cannot be acted on and cannot be recognised as wrong");
    }

    [Fact]
    public async Task ReviewAsync_SpotCheckCitingNoBulletIsDropped()
    {
        var sut = CreateSut("""
            {
              "summary": "Fine.",
              "spotChecks": [
                { "message": "The tone is inconsistent.", "reasoning": "It just is.", "bullets": [] },
                { "message": "These two overlap.", "reasoning": "Same project.", "bullets": [0, 1] }
              ]
            }
            """);

        var review = await ReviewAsync(sut);

        review!.SpotChecks.Should().ContainSingle("an observation about the resume that points at nothing in it cannot be checked")
            .Which.Message.Should().Be("These two overlap.");
    }

    [Fact]
    public async Task ReviewAsync_SpotCheckCitingOnlyUnknownBulletsIsDropped()
    {
        var sut = CreateSut("""
            { "spotChecks": [{ "message": "Bullet nine repeats bullet eight.", "reasoning": "...", "bullets": [8, 9] }] }
            """);

        (await ReviewAsync(sut)).Should().BeNull("nothing survived, so there is no review to merge");
    }

    [Fact]
    public async Task ReviewAsync_EmptySuggestionListsDoNotCreateAnEntry()
    {
        var sut = CreateSut("""
            { "summary": "Fine.", "bulletNotes": [{ "bullet": 0, "suggestions": ["", "   "] }] }
            """);

        var review = await ReviewAsync(sut);

        review!.BulletSuggestions.Should().NotContainKey(0, "a bullet with nothing to say about it should render nothing");
    }

    [Fact]
    public async Task ReviewAsync_WhenTheAnswerDoesNotParse_ReturnsNothingRatherThanGuessing()
    {
        var sut = CreateSut("I'd be happy to help you review this resume!");

        (await ReviewAsync(sut)).Should().BeNull();
    }

    [Fact]
    public async Task ReviewAsync_WhenTheCallThrows_ReturnsNothingSoTheDeterministicReportStands()
    {
        var sut = new AiResumeReviewer(
            ChatClientMocks.Throwing(new HttpRequestException("No connection could be made")).Object,
            NullLogger<AiResumeReviewer>.Instance);

        (await ReviewAsync(sut)).Should().BeNull();
    }

    [Fact]
    public async Task ReviewAsync_PropagatesCancellation()
    {
        var sut = new AiResumeReviewer(
            ChatClientMocks.Throwing(new OperationCanceledException()).Object,
            NullLogger<AiResumeReviewer>.Instance);

        var review = async () => await ReviewAsync(sut);

        await review.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReviewAsync_WithNoBullets_DoesNotCallTheModel()
    {
        var chatClient = ChatClientMocks.ReturningText("{}");
        var sut = new AiResumeReviewer(chatClient.Object, NullLogger<AiResumeReviewer>.Instance);

        var review = await sut.ReviewAsync([], [], TestContext.Current.CancellationToken);

        review.Should().BeNull();
        chatClient.VerifyNoOtherCalls();
    }
}
