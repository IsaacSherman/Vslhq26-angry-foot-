using AngryFoot.ApiService.Application.Artifacts;
using AngryFoot.Contracts;

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

        artifacts.MapPut("/{id:guid}/selection", async (
            Guid id,
            SelectArtifactVersionsRequest request,
            IArtifactService artifactService,
            CancellationToken cancellationToken) =>
        {
            var result = await artifactService.SelectVersionsAsync(id, request, cancellationToken);
            return result.Status switch
            {
                VersionSelectionStatus.Updated => Results.Ok(result.Artifact),
                VersionSelectionStatus.ArtifactNotFound => Results.NotFound(),
                _ => Results.BadRequest("That version is not stored on this generation.")
            };
        });

        artifacts.MapDelete("/{id:guid}", async (Guid id, IArtifactService artifactService, CancellationToken cancellationToken) =>
        {
            var deleted = await artifactService.DeleteAsync(id, cancellationToken);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        return apiGroup;
    }
}
