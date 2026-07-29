using AngryFoot.ApiService.Application.Artifacts;

namespace AngryFoot.ApiService.Api;

public static class ArtifactEndpoints
{
    public static RouteGroupBuilder MapArtifactEndpoints(this RouteGroupBuilder apiGroup)
    {
        var artifacts = apiGroup.MapGroup("/artifacts");

        artifacts.MapGet("/", async (IArtifactService artifactService, CancellationToken cancellationToken) =>
        {
            var result = await artifactService.GetSummariesAsync(cancellationToken);
            return Results.Ok(result);
        });

        artifacts.MapGet("/{id:guid}", async (Guid id, IArtifactService artifactService, CancellationToken cancellationToken) =>
        {
            var result = await artifactService.GetByIdAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        artifacts.MapDelete("/{id:guid}", async (Guid id, IArtifactService artifactService, CancellationToken cancellationToken) =>
        {
            var deleted = await artifactService.DeleteAsync(id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        return apiGroup;
    }
}
