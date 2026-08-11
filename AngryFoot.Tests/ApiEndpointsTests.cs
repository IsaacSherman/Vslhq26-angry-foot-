using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using AngryFoot.Contracts;
using Microsoft.Extensions.Logging;

namespace AngryFoot.Tests;

[Collection(IntegrationTestCollection.Name)]
public class ApiEndpointsTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ProfileGetAndPutRoundTripWorks()
    {
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

        var getResponse = await apiClient.GetAsync("/api/profile", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var profile = await getResponse.Content.ReadFromJsonAsync<ProfileDto>(cancellationToken);
        Assert.NotNull(profile);

        var updated = profile! with
        {
            Name = "Isaac Sherman",
            Email = "isaac@example.com",
            Phone = "555-555-5555",
            LinkedIn = "https://linkedin.com/in/isaacsherman",
            GitHub = "https://github.com/isaacsherman",
            ProfessionalSummary = "Principal engineer building AI-enhanced productivity tooling.",
            WorkHistory =
            [
                new WorkHistoryDto(Guid.Empty, "Acme Corp", "Principal Engineer", "Remote", "2023", null, 0)
            ],
            Education =
            [
                new EducationDto(Guid.Empty, "State University", "BS", "Computer Science", "2016", 0)
            ],
            Certifications =
            [
                new CertificationDto(Guid.Empty, "Azure Developer Associate", "Microsoft", "2025", 0)
            ]
        };

        var putResponse = await apiClient.PutAsJsonAsync("/api/profile", updated, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        var putProfile = await putResponse.Content.ReadFromJsonAsync<ProfileDto>(cancellationToken);
        Assert.NotNull(putProfile);
        Assert.Equal("Isaac Sherman", putProfile!.Name);
        Assert.Single(putProfile.WorkHistory);
        Assert.Single(putProfile.Education);
        Assert.Single(putProfile.Certifications);
    }

    [Fact]
    public async Task ImportLinkedInProfileEndpointParsesExportWithoutPersisting()
    {
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

        using var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("Positions.csv");
            using var entryStream = entry.Open();
            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
            writer.Write("Company Name,Title,Location,Started On,Finished On\nAcme Corp,Principal Engineer,Remote,2023,\n");
        }
        zipStream.Position = 0;

        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(zipStream);
        content.Add(fileContent, "file", "linkedin-export.zip");

        var importResponse = await apiClient.PostAsync("/api/profile/import/linkedin", content, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, importResponse.StatusCode);

        var imported = await importResponse.Content.ReadFromJsonAsync<LinkedInImportResultDto>(cancellationToken);
        Assert.NotNull(imported);
        Assert.Single(imported!.Profile.WorkHistory);
        Assert.Equal("Acme Corp", imported.Profile.WorkHistory[0].Employer);
        Assert.True(imported.WorkHistoryFound);
        Assert.False(imported.EducationFound, "this fixture only includes Positions.csv, e.g. a Profile-only download");
        Assert.False(imported.CertificationsFound, "this fixture only includes Positions.csv, e.g. a Profile-only download");

        // Importing only returns a prefilled draft for review; it must not persist.
        var getResponse = await apiClient.GetAsync("/api/profile", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var stored = await getResponse.Content.ReadFromJsonAsync<ProfileDto>(cancellationToken);
        Assert.NotNull(stored);
        Assert.Empty(stored!.WorkHistory);
    }

    [Fact]
    public async Task ResumeImportPreviewsCandidatesWithoutPersistingThenImportsSelectedOnes()
    {
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

        const string resumeText = """
            EXPERIENCE

            Vandelay Industries
            • Negotiated a vendor contract renewal that lowered annual licensing costs by 18%.
            • Introduced a triage rotation that halved the median time to first response on incidents.
            """;

        var bulletsBefore = await apiClient.GetFromJsonAsync<List<BulletDto>>("/api/bullets", cancellationToken);
        Assert.NotNull(bulletsBefore);

        var previewResponse = await apiClient.PostAsJsonAsync("/api/bullets/import/resume/preview", new ResumeImportPreviewRequest(resumeText), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);

        var preview = await previewResponse.Content.ReadFromJsonAsync<ResumeImportPreviewResponse>(cancellationToken);
        Assert.NotNull(preview);
        Assert.Equal(2, preview!.Candidates.Count);
        Assert.Equal("Vandelay Industries", preview.Candidates[0].SuggestedEmployer);
        // Qdrant is disabled for integration tests, so preview must still work on the text-only path.
        Assert.Equal(DuplicateDetectionModeDto.Lexical, preview.DetectionMode);

        // Previewing is a dry run; nothing may be written until the user confirms.
        var bulletsAfterPreview = await apiClient.GetFromJsonAsync<List<BulletDto>>("/api/bullets", cancellationToken);
        Assert.NotNull(bulletsAfterPreview);
        Assert.Equal(bulletsBefore!.Count, bulletsAfterPreview!.Count);

        var chosen = preview.Candidates[0];
        var confirmResponse = await apiClient.PostAsJsonAsync(
            "/api/bullets/import/resume",
            new ConfirmResumeImportRequest([new ImportBulletItem(chosen.Index, chosen.BulletText, chosen.SuggestedEmployer, [])]),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        var result = await confirmResponse.Content.ReadFromJsonAsync<ResumeImportResultDto>(cancellationToken);
        Assert.NotNull(result);
        Assert.Single(result!.Created);
        Assert.Equal("Vandelay Industries", result.Created[0].SourceEmployer);

        var bulletsAfterImport = await apiClient.GetFromJsonAsync<List<BulletDto>>("/api/bullets", cancellationToken);
        Assert.NotNull(bulletsAfterImport);
        Assert.Equal(bulletsBefore.Count + 1, bulletsAfterImport!.Count);
        Assert.Contains(bulletsAfterImport, x => x.BulletText == chosen.BulletText);
    }

    [Fact]
    public async Task BulletCrudSearchAndEnrichWorks()
    {
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

        var createResponse = await apiClient.PostAsJsonAsync("/api/bullets", new CreateBulletRequest("Reduced widget waste by 30% by redesigning the assembly process."), cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<BulletDto>(cancellationToken);
        Assert.NotNull(created);
        Assert.Equal(EnrichmentStateDto.Enriched, created!.EnrichmentState);

        var listResponse = await apiClient.GetAsync("/api/bullets?search=widget", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<BulletDto>>(cancellationToken);
        Assert.NotNull(list);
        Assert.Single(list!);

        var updateResponse = await apiClient.PutAsJsonAsync($"/api/bullets/{created.Id}", new UpdateBulletRequest("Reduced widget waste by 35% by redesigning the assembly process."), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<BulletDto>(cancellationToken);
        Assert.NotNull(updated);
        Assert.Contains("35%", updated!.BulletText);

        var enrichResponse = await apiClient.PostAsync($"/api/bullets/{created.Id}/enrich", content: null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, enrichResponse.StatusCode);

        var deleteResponse = await apiClient.DeleteAsync($"/api/bullets/{created.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getDeletedResponse = await apiClient.GetAsync($"/api/bullets/{created.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getDeletedResponse.StatusCode);
    }

    [Fact]
    public async Task ArtifactHistoryEndpointsReturnExpectedDefaults()
    {
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

        var listResponse = await apiClient.GetAsync("/api/artifacts", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<ArtifactSummaryDto>>(cancellationToken);
        Assert.NotNull(list);

        var missingId = Guid.NewGuid();
        var getMissing = await apiClient.GetAsync($"/api/artifacts/{missingId}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getMissing.StatusCode);

        var deleteMissing = await apiClient.DeleteAsync($"/api/artifacts/{missingId}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, deleteMissing.StatusCode);
    }

    [Fact]
    public async Task GenerationEndpointsProduceAndPersistArtifacts()
    {
        // var cts = new CancellationTokenSource();
        var cancellationToken = TestContext.Current.CancellationToken;
        // var cancellationToken = cts.Token;
        // cts.CancelAfter(10000);
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

        await apiClient.PostAsJsonAsync("/api/bullets", new CreateBulletRequest("Implemented automated validation workflows that reduced manual review effort by 75%."), cancellationToken);

        var analyzeResponse = await apiClient.PostAsJsonAsync("/api/generations/analyze", new { JobDescription = "Senior .NET Engineer role requiring C#, ASP.NET Core, and Azure." }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, analyzeResponse.StatusCode);
        var analysis = await analyzeResponse.Content.ReadFromJsonAsync<JobFitAnalysisDto>(cancellationToken);
        Assert.NotNull(analysis);
        Assert.NotNull(analysis!.Job);
        Assert.NotNull(analysis.Fit);
        Assert.InRange(analysis.Fit.FitScore, 0, 100);
        Assert.False(string.IsNullOrWhiteSpace(analysis.Fit.Verdict));

        var generationResponse = await apiClient.PostAsJsonAsync("/api/generations", new GenerationRequest(
            "Senior .NET Engineer role requiring C#, ASP.NET Core, and Azure. Drive architecture and automation.",
            "Senior .NET Engineer",
            "Contoso",
            8), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, generationResponse.StatusCode);
        var result = await generationResponse.Content.ReadFromJsonAsync<GenerationResultDto>(cancellationToken);
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result!.ResumeMarkdown));
        Assert.False(string.IsNullOrWhiteSpace(result.CoverLetterMarkdown));

        var artifactsResponse = await apiClient.GetAsync("/api/artifacts", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, artifactsResponse.StatusCode);
        var artifacts = await artifactsResponse.Content.ReadFromJsonAsync<List<ArtifactSummaryDto>>(cancellationToken);
        Assert.NotNull(artifacts);
        Assert.Contains(artifacts!, x => x.Id == result.ArtifactId);
    }
}
