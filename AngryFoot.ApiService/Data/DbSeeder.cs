using AngryFoot.ApiService.Domain;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AngryFootDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var hasProfile = await dbContext.Profiles.AnyAsync(cancellationToken);
        if (!hasProfile)
        {
            dbContext.Profiles.Add(Profile.CreateEmpty());
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
