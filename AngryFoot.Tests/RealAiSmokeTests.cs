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

    /// <summary>
    /// The reviewer sees the candidate's other bullets so it can tell an overreach from a fair
    /// claim. It used to treat them as a supply of claims instead, answering a vague bullet about
    /// one job with achievements lifted from another. Prompt-content tests pin the wording; only a
    /// real model can say whether the wording works.
    /// </summary>
    [Fact]
    public async Task DeepReviewCritique_WithRealAi_WhenEnabled_DoesNotImportOtherBulletsAchievements()
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

        // Distinctive, unmistakably unrelated work for the reviewer to be tempted by.
        string[] library =
        [
            "Streamed oscilloscope acquisition data at 25ns granularity to multiple concurrent clients.",
            "Cut release time from 20 minutes to under 2 with a one-click deployment pipeline.",
            "Maintained systems and improved throughput for the ACME account."
        ];

        foreach (var bullet in library)
        {
            await apiClient.PostAsJsonAsync("/api/bullets", new CreateBulletRequest(bullet), cancellationToken);
        }

        var response = await apiClient.PostAsJsonAsync(
            "/api/bullets/rewrite/critique",
            new RewriteBulletRequest("Maintained systems and improved throughput for the ACME account", DeepReview: true),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var critique = await response.Content.ReadFromJsonAsync<BulletRewriteCritiqueResponse>(cancellationToken);
        Assert.NotNull(critique);

        var alternative = critique!.Alternative ?? string.Empty;
        output?.WriteLine($"v1:  {critique.Draft}");
        output?.WriteLine($"v2:  {alternative}");

        // Terms that appear nowhere in the bullet under review, only in the other two.
        string[] borrowed = ["oscilloscope", "25ns", "one-click", "20 minutes", "deployment pipeline"];
        var imported = borrowed
            .Where(term => alternative.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            imported.Length == 0,
            $"The reviewer's alternative imported claims from unrelated bullets: {string.Join(", ", imported)}. Full text: {alternative}");
    }

    /// <summary>
    /// Re-measures the constant behind semantic evidence matching. It is not a pass/fail check on the
    /// model so much as a guard on the ordering the threshold assumes: work the library actually
    /// evidences must outscore a technology it has never touched, or no cut separates them and the
    /// number in <c>SemanticEvidenceMatcher.MinimumConfidence</c> is arbitrary rather than measured.
    /// <para>
    /// It prints every score, because the useful output of this test is the distribution rather than
    /// the assertion.
    /// </para>
    /// </summary>
    [Fact]
    public async Task SemanticEvidenceThreshold_KeepsRelatedWorkAboveUnrelatedTechnologies()
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

        // A library about mentoring and release engineering, naming neither Kubernetes nor Salesforce.
        string[] library =
        [
            "Mentored two interns through weekly 1:1s, pair programming, and code reviews, helping establish engineering best practices.",
            "Automated the release pipeline, cutting deployments from 40 minutes to under 4.",
            "Ran the on-call rotation and wrote the runbooks the team still uses."
        ];

        foreach (var text in library)
        {
            await apiClient.PostAsJsonAsync("/api/bullets", new CreateBulletRequest(text), cancellationToken);
        }

        var response = await apiClient.PostAsJsonAsync(
            "/api/generations/analyze",
            new
            {
                JobDescription = """
                    Requirements:
                    - Technical leadership and mentoring
                    - Release automation
                    - Salesforce administration
                    - Kubernetes cluster operations
                    """
            },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var analysis = await response.Content.ReadFromJsonAsync<JobEvidenceAnalysisDto>(cancellationToken);
        Assert.NotNull(analysis);

        var scores = analysis.Coverage.Requirements.ToDictionary(
            requirement => requirement.Requirement,
            requirement => requirement.Why.SupportingEvidence
                .Where(citation => citation.MatchKind == EvidenceMatchKindDto.Semantic)
                .Select(citation => citation.Confidence ?? 0)
                .DefaultIfEmpty(0)
                .Max(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var scored in scores.OrderByDescending(x => x.Value))
        {
            Console.WriteLine($"{scored.Key,-45} {scored.Value:0.000}");
        }

        var evidenced = Best(scores, "mentor", "leadership", "release", "automation");
        var absent = Best(scores, "salesforce", "kubernetes");

        Assert.True(
            evidenced > absent,
            $"work the library evidences scored {evidenced:0.000}, technology it has never touched scored {absent:0.000}. "
                + "A threshold can only separate them while the first is higher.");
    }

    /// <summary>The best semantic score across every requirement whose wording contains one of the given words.</summary>
    private static double Best(IReadOnlyDictionary<string, double> scores, params string[] words)
        => scores
            .Where(x => words.Any(word => x.Key.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Select(x => x.Value)
            .DefaultIfEmpty(0)
            .Max();

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
