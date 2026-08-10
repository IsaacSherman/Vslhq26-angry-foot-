using AngryFoot.ApiService.Application.Profile;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Api;

public static class ProfileEndpoints
{
    private const long MaxImportFileSizeBytes = 25 * 1024 * 1024;

    public static RouteGroupBuilder MapProfileEndpoints(this RouteGroupBuilder apiGroup)
    {
        var profile = apiGroup.MapGroup("/profile");

        profile.MapGet("/", async (IProfileService profileService, CancellationToken cancellationToken) =>
        {
            var result = await profileService.GetAsync(cancellationToken);
            return Results.Ok(result);
        });

        profile.MapPut("/", async (ProfileDto request, IProfileService profileService, CancellationToken cancellationToken) =>
        {
            var result = await profileService.UpsertAsync(request, cancellationToken);
            return Results.Ok(result);
        });

        profile.MapPost("/import/linkedin", async (IFormFile file, ILinkedInProfileImportService importService, CancellationToken cancellationToken) =>
        {
            if (file.Length == 0)
            {
                return Results.BadRequest("No file was uploaded.");
            }

            if (file.Length > MaxImportFileSizeBytes)
            {
                return Results.BadRequest("The uploaded file exceeds the 25 MB size limit.");
            }

            if (!string.Equals(Path.GetExtension(file.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("Expected a .zip file exported from LinkedIn.");
            }

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await importService.ImportAsync(stream, cancellationToken);
                return Results.Ok(result);
            }
            catch (InvalidLinkedInExportException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        }).DisableAntiforgery();

        return apiGroup;
    }
}
