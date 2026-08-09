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

            var result = await rewriteAssistant.RewriteAsync(request.BulletText, cancellationToken);
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

        bullets.MapPost("/index-missing", async (IBulletService bulletService, CancellationToken cancellationToken) =>
        {
            var indexedCount = await bulletService.IndexAllMissingAsync(cancellationToken);
            return Results.Ok(new IndexMissingBulletsResponse(indexedCount));
        });

        return apiGroup;
    }
}
