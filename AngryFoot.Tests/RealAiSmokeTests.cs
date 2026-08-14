using System.Diagnostics;
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

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AngryFoot_AppHost>(TestDatabase.AppHostArgs, cancellationToken);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Aspire.", LogLevel.Warning);
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder => clientBuilder.AddStandardResilienceHandler(TestResilience.ConfigureStandardHandler));
        TestDatabase.UseIsolatedDatabase(appHost);

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

    /// <summary>
    /// Times a plain generation against a deep-review one so the README's latency figures come
    /// from a measurement rather than a guess, and so a regression that pushes deep review past
    /// its 10 minute budget shows up as a failure rather than a hung page.
    /// </summary>
    [Fact]
    public async Task DeepReviewGeneration_WithRealAi_WhenEnabled_FitsInsideItsTimeout()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_AI_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var output = TestContext.Current.TestOutputHelper;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AngryFoot_AppHost>(TestDatabase.AppHostArgs, cancellationToken);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Aspire.", LogLevel.Warning);
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder => clientBuilder.AddStandardResilienceHandler(TestResilience.ConfigureStandardHandler));
        TestDatabase.UseIsolatedDatabase(appHost);

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        var apiClient = app.CreateHttpClient("apiservice");

        string[] bullets =
        [
            "Implemented API integrations that improved workflow throughput by 25%.",
            "Led the migration of a monolith to containerized services on Azure.",
            "Cut nightly batch runtime from 6 hours to 40 minutes by reworking the query plan.",
            "Mentored four junior engineers through their first production on-call rotations.",
            "Ran the quarterly office relocation logistics.",
            "Built an internal dashboard in Blazor for support triage."
        ];

        foreach (var bullet in bullets)
        {
            await apiClient.PostAsJsonAsync("/api/bullets", new CreateBulletRequest(bullet), cancellationToken);
        }

        var request = new GenerationRequest(
            "Senior backend engineer role requiring C#, .NET, Azure, and API design experience.",
            "Senior Backend Engineer",
            "Contoso",
            5);

        var plain = await TimeGenerationAsync(apiClient, request, cancellationToken);
        var deep = await TimeGenerationAsync(
            apiClient,
            request with { DeepReview = true, Guidance = "\"Workflow throughput\" means support-ticket resolution rate, not CI pipeline speed." },
            cancellationToken);

        output?.WriteLine($"Plain generation:       {plain.Elapsed.TotalSeconds:F1}s");
        output?.WriteLine($"Deep review generation: {deep.Elapsed.TotalSeconds:F1}s ({deep.Elapsed.TotalSeconds / plain.Elapsed.TotalSeconds:F1}x)");
        output?.WriteLine($"Resume versions:        {deep.Result.ResumeRefinement?.Versions.Count ?? 0}");
        output?.WriteLine($"Cover letter versions:  {deep.Result.CoverLetterRefinement?.Versions.Count ?? 0}");

        Assert.NotNull(deep.Result.ResumeRefinement);
        Assert.NotNull(deep.Result.CoverLetterRefinement);
        Assert.True(
            deep.Elapsed < TimeSpan.FromMinutes(10),
            $"Deep review took {deep.Elapsed.TotalSeconds:F0}s, which exceeds the 10 minute client budget.");
    }

    private static async Task<(GenerationResultDto Result, TimeSpan Elapsed)> TimeGenerationAsync(
        HttpClient apiClient, GenerationRequest request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await apiClient.PostAsJsonAsync("/api/generations", request, cancellationToken);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<GenerationResultDto>(cancellationToken);
        Assert.NotNull(result);

        return (result!, stopwatch.Elapsed);
    }
}
