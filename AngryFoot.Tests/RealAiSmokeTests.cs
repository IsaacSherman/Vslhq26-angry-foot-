using System.Net;
using System.Net.Http.Json;
using AngryFoot.Contracts;
using Microsoft.Extensions.Logging;

namespace AngryFoot.Tests;

[Collection(IntegrationTestCollection.Name)]
public class RealAiSmokeTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task GenerationEndpoint_WithRealAi_WhenEnabled_ReturnsArtifact()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_AI_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var cancellationToken = TestContext.Current.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AngryFoot_AppHost>(cancellationToken);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Aspire.", LogLevel.Warning);
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder => clientBuilder.AddStandardResilienceHandler());

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        var apiClient = app.CreateHttpClient("apiservice");

        await apiClient.PostAsJsonAsync(
            "/api/bullets",
            new CreateBulletRequest("Implemented API integrations that improved workflow throughput by 25%."),
            cancellationToken);

        var response = await apiClient.PostAsJsonAsync(
            "/api/generations",
            new GenerationRequest(
                "Senior backend engineer role requiring C#, .NET, Azure, and API design experience.",
                "Senior Backend Engineer",
                "Contoso",
                8),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<GenerationResultDto>(cancellationToken);
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result!.ArtifactId);
        Assert.False(string.IsNullOrWhiteSpace(result.ResumeMarkdown));
        Assert.False(string.IsNullOrWhiteSpace(result.CoverLetterMarkdown));
    }
}
