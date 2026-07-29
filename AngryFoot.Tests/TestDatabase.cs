using Aspire.Hosting;

namespace AngryFoot.Tests;

internal static class TestDatabase
{
    /// <summary>
    /// Points the apiservice at a fresh temp SQLite file so integration tests never
    /// read or write the developer's real database.
    /// </summary>
    public static void UseIsolatedDatabase(IDistributedApplicationTestingBuilder appHost)
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"angryfoot-test-{Guid.NewGuid():N}.db");
        var apiService = appHost.Resources.OfType<ProjectResource>()
            .Single(r => string.Equals(r.Name, "apiservice", StringComparison.OrdinalIgnoreCase));

        appHost.CreateResourceBuilder(apiService)
            .WithEnvironment("ConnectionStrings__angryfoot", $"Data Source={databasePath}");
    }
}
