using AngryFoot.ApiService.Application.Profile;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Api;

public static class ProfileEndpoints
{
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

        return apiGroup;
    }
}
