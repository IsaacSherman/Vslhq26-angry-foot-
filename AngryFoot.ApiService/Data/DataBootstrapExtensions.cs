using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Data;

public static class DataBootstrapExtensions
{
    public static async Task MigrateAndSeedAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AngryFootDbContext>();

        await dbContext.Database.MigrateAsync(cancellationToken);
        await DbSeeder.SeedAsync(dbContext, cancellationToken);
    }
}
