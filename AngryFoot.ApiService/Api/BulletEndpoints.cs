using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Application.Conversion;
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

        bullets.MapPost("/{id:guid}/enrich/preview", async (Guid id, IBulletService bulletService, CancellationToken cancellationToken) =>
        {
            var result = await bulletService.ProposeEnrichmentAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        bullets.MapPut("/{id:guid}/enrichment", async (
            Guid id,
            SetBulletEnrichmentRequest request,
            IBulletService bulletService,
            CancellationToken cancellationToken) =>
        {
            var result = await bulletService.SetEnrichmentAsync(id, request, cancellationToken);
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

        // A sibling of the JSON preview rather than a replacement: conversion is the only extra
        // step, and everything downstream - parsing, duplicate detection, the confirm call - is the
        // path a pasted resume already takes.
        bullets.MapPost("/import/resume/preview/file", async (
            IFormFile file,
            IResumeDocumentConverter converter,
            IResumeBulletImportService importService,
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

            var result = await importService.PreviewAsync(markdown, cancellationToken);
            return result.Candidates.Count == 0
                ? Results.BadRequest($"We couldn't find any bullets in {file.FileName}. A resume saved as scanned images has no text to extract.")
                : Results.Ok(result);
        }).DisableAntiforgery();

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

        // Scores wording that has not been saved. Returns the tagging it used so the caller can
        // hand it back on the save that follows rather than paying for enrichment twice.
        bullets.MapPost("/assess", async (
            AssessBulletRequest request,
            IBulletService bulletService,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.BulletText))
            {
                return Results.BadRequest("Bullet text is required.");
            }

            return Results.Ok(await bulletService.AssessAsync(request, cancellationToken));
        });

        bullets.MapPut("/{id:guid}/quality-acknowledgements", async (
            Guid id,
            SetBulletQualityAcknowledgementsRequest request,
            IBulletService bulletService,
            CancellationToken cancellationToken) =>
        {
            var result = await bulletService.SetQualityAcknowledgementsAsync(id, request.Signals, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        // Revisions are variants of a bullet, not edits to it: creating one never touches the
        // bullet's own text, and promoting one is a separate, explicit call.
        bullets.MapGet("/{id:guid}/revisions", async (
            Guid id,
            IBulletRevisionService revisionService,
            CancellationToken cancellationToken) =>
        {
            var result = await revisionService.GetAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        bullets.MapPost("/{id:guid}/revisions", async (
            Guid id,
            CreateBulletRevisionRequest request,
            IBulletRevisionService revisionService,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.IsDefined(request.Mode))
            {
                return Results.BadRequest("Unknown revision mode.");
            }

            var result = await revisionService.CreateAsync(id, request, cancellationToken);
            return result is null
                ? Results.NotFound()
                : Results.Created($"/api/bullets/{id}/revisions/{result.Id}", result);
        });

        bullets.MapDelete("/{id:guid}/revisions/{revisionId:guid}", async (
            Guid id,
            Guid revisionId,
            IBulletRevisionService revisionService,
            CancellationToken cancellationToken) =>
        {
            var deleted = await revisionService.DeleteAsync(id, revisionId, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        bullets.MapPost("/{id:guid}/revisions/{revisionId:guid}/promote", async (
            Guid id,
            Guid revisionId,
            IBulletRevisionService revisionService,
            CancellationToken cancellationToken) =>
        {
            var result = await revisionService.PromoteAsync(id, revisionId, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        bullets.MapPost("/index-missing", async (IBulletService bulletService, CancellationToken cancellationToken) =>
        {
            var indexedCount = await bulletService.IndexAllMissingAsync(cancellationToken);
            return Results.Ok(new IndexMissingBulletsResponse(indexedCount));
        });

        return apiGroup;
    }
}
