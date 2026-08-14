using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Api;

public static class BulletEndpoints
{
    public static RouteGroupBuilder MapBulletEndpoints(this RouteGroupBuilder apiGroup)
    {
        var bullets = apiGroup.MapGroup("/bullets");

        bullets.MapGet("/", async (
            IBulletService bulletService,
            string? search,
            string? tag,
            string? skill,
            string? technology,
            string? category,
            CancellationToken cancellationToken) =>
        {
            var result = await bulletService.SearchAsync(search, tag, skill, technology, category, cancellationToken);
            return Results.Ok(result);
        });

        bullets.MapGet("/{id:guid}", async (Guid id, IBulletService bulletService, CancellationToken cancellationToken) =>
        {
            var result = await bulletService.GetByIdAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        bullets.MapPost("/", async (CreateBulletRequest request, IBulletService bulletService, CancellationToken cancellationToken) =>
        {
            var result = await bulletService.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/bullets/{result.Id}", result);
        });

        bullets.MapPost("/rewrite", async (RewriteBulletRequest request, IBulletRewriteAssistant rewriteAssistant, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.BulletText))
            {
                return Results.BadRequest("Bullet text is required.");
            }

            var result = await rewriteAssistant.RewriteAsync(request.BulletText, request.DeepReview, cancellationToken);
            return Results.Ok(result);
        });

        // Deep review, split in two so the user can resolve an ambiguity the reviewer tripped
        // over before the revision and synthesis stages commit to a reading of it.
        bullets.MapPost("/rewrite/critique", async (RewriteBulletRequest request, IBulletRewriteAssistant rewriteAssistant, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.BulletText))
            {
                return Results.BadRequest("Bullet text is required.");
            }

            var result = await rewriteAssistant.CritiqueAsync(request.BulletText, cancellationToken);
            return result is null
                // No AI draft or no usable critique; the caller should use the one-shot endpoint.
                ? Results.NoContent()
                : Results.Ok(result);
        });

        bullets.MapPost("/rewrite/complete", async (CompleteBulletRewriteRequest request, IBulletRewriteAssistant rewriteAssistant, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Draft) || string.IsNullOrWhiteSpace(request.Critique))
            {
                return Results.BadRequest("The draft and critique from the critique step are required.");
            }

            var result = await rewriteAssistant.CompleteAsync(request, cancellationToken);
            return Results.Ok(result);
        });

        bullets.MapPut("/{id:guid}", async (Guid id, UpdateBulletRequest request, IBulletService bulletService, CancellationToken cancellationToken) =>
        {
            var result = await bulletService.UpdateAsync(id, request, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        bullets.MapDelete("/{id:guid}", async (Guid id, IBulletService bulletService, CancellationToken cancellationToken) =>
        {
            var deleted = await bulletService.DeleteAsync(id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        bullets.MapPost("/{id:guid}/enrich", async (Guid id, IBulletService bulletService, CancellationToken cancellationToken) =>
        {
            var result = await bulletService.EnrichAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        bullets.MapPost("/{id:guid}/index", async (Guid id, IBulletService bulletService, CancellationToken cancellationToken) =>
        {
            var result = await bulletService.IndexAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        bullets.MapPost("/import/resume/preview", async (
            ResumeImportPreviewRequest request,
            IResumeBulletImportService importService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.ResumeText))
            {
                return Results.BadRequest("Paste your resume text to import bullets from it.");
            }

            var result = await importService.PreviewAsync(request.ResumeText, cancellationToken);
            return result.Candidates.Count == 0
                ? Results.BadRequest("We couldn't find any bullets in that text. Check that the achievement lines were included.")
                : Results.Ok(result);
        });

        bullets.MapPost("/import/resume", async (
            ConfirmResumeImportRequest request,
            IResumeBulletImportService importService,
            CancellationToken cancellationToken) =>
        {
            // Blank texts are dropped during import, so counting the raw request would report
            // success for a request that creates nothing.
            if (!request.Bullets.Any(x => !string.IsNullOrWhiteSpace(x.BulletText)))
            {
                return Results.BadRequest("Select at least one bullet with text to import.");
            }

            var result = await importService.ConfirmAsync(request, cancellationToken);
            return Results.Ok(result);
        });

        bullets.MapPost("/index-missing", async (IBulletService bulletService, CancellationToken cancellationToken) =>
        {
            var indexedCount = await bulletService.IndexAllMissingAsync(cancellationToken);
            return Results.Ok(new IndexMissingBulletsResponse(indexedCount));
        });

        return apiGroup;
    }
}
