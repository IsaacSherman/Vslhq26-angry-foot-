using AngryFoot.ApiService.Application.Conversion;
using AngryFoot.ApiService.Application.Review;
using AngryFoot.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace AngryFoot.ApiService.Api;

public static class ResumeReviewEndpoints
{
    public static RouteGroupBuilder MapResumeReviewEndpoints(this RouteGroupBuilder apiGroup)
    {
        var review = apiGroup.MapGroup("/resume-review");

        review.MapPost("/", async (
            ResumeReviewRequest request,
            IResumeReviewService reviewService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.ResumeText))
            {
                return Results.BadRequest("Paste your resume text to have it reviewed.");
            }

            return Results.Ok(await reviewService.ReviewAsync(request.ResumeText, request.JobDescription, cancellationToken));
        });

        // The upload sibling. Conversion is the only difference; the review itself cannot tell which
        // door the text came through, and neither reads or writes the database.
        review.MapPost("/file", async (
            IFormFile file,
            [FromForm] string? jobDescription,
            IResumeDocumentConverter converter,
            IResumeReviewService reviewService,
            CancellationToken cancellationToken) =>
        {
            string markdown;
            try
            {
                markdown = await ResumeUploads.ReadMarkdownAsync(file, converter, cancellationToken);
            }
            catch (ResumeConversionException ex)
            {
                return Results.BadRequest(ex.Message);
            }

            return Results.Ok(await reviewService.ReviewAsync(markdown, jobDescription, cancellationToken));
        }).DisableAntiforgery();

        return apiGroup;
    }
}
