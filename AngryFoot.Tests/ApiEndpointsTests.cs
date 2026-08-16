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
            new ConfirmResumeImportRequest([
                new ImportBulletItem(chosen.Index, chosen.BulletText, chosen.SuggestedEmployer, chosen.BulletText, [])
            ]),
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

        // Blank texts are dropped during import, so a request made only of them creates nothing and
        // must be rejected rather than reported as a successful import of zero bullets.
        var blankResponse = await apiClient.PostAsJsonAsync(
            "/api/bullets/import/resume",
            new ConfirmResumeImportRequest([new ImportBulletItem(0, "   ", null, "   ", [])]),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, blankResponse.StatusCode);

        var bulletsAfterBlank = await apiClient.GetFromJsonAsync<List<BulletDto>>("/api/bullets", cancellationToken);
        Assert.NotNull(bulletsAfterBlank);
        Assert.Equal(bulletsAfterImport.Count, bulletsAfterBlank!.Count);
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

        await AssertRevisionsRoundTripAsync(apiClient, created.Id, cancellationToken);

        var deleteResponse = await apiClient.DeleteAsync($"/api/bullets/{created.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // The bullet is gone, so its revisions must be too rather than outliving what they revise.
        var orphanedRevisions = await apiClient.GetAsync($"/api/bullets/{created.Id}/revisions", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, orphanedRevisions.StatusCode);

        var getDeletedResponse = await apiClient.GetAsync($"/api/bullets/{created.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, getDeletedResponse.StatusCode);
    }

    /// <summary>
    /// A revision is a variant kept beside the bullet, never an edit to it. This walks the whole
    /// contract: write one, confirm the bullet is untouched, promote it, confirm it took.
    /// </summary>
    private static async Task AssertRevisionsRoundTripAsync(HttpClient apiClient, Guid bulletId, CancellationToken cancellationToken)
    {
        var before = await apiClient.GetFromJsonAsync<BulletDto>($"/api/bullets/{bulletId}", cancellationToken);
        Assert.NotNull(before);

        var emptyResponse = await apiClient.GetAsync($"/api/bullets/{bulletId}/revisions", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        Assert.Empty((await emptyResponse.Content.ReadFromJsonAsync<List<BulletRevisionDto>>(cancellationToken))!);

        var createResponse = await apiClient.PostAsJsonAsync(
            $"/api/bullets/{bulletId}/revisions",
            new CreateBulletRevisionRequest(BulletRevisionModeDto.Ats),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var revision = await createResponse.Content.ReadFromJsonAsync<BulletRevisionDto>(cancellationToken);
        Assert.NotNull(revision);
        Assert.Equal(BulletRevisionModeDto.Ats, revision!.Mode);
        Assert.Equal(1, revision.Version);
        Assert.Equal(before!.BulletText, revision.SourceText);
        Assert.False(revision.IsStale);
        Assert.False(string.IsNullOrWhiteSpace(revision.RevisedText));

        // Quality travels with a revision, and its score is the sum of the signals shown beside it.
        Assert.NotNull(revision.Quality);
        Assert.Equal(
            revision.Quality!.Signals.Where(x => x.Earned).Sum(x => x.Weight),
            revision.Quality.Score);

        var unchanged = await apiClient.GetFromJsonAsync<BulletDto>($"/api/bullets/{bulletId}", cancellationToken);
        Assert.Equal(before.BulletText, unchanged!.BulletText);

        var promoteResponse = await apiClient.PostAsync(
            $"/api/bullets/{bulletId}/revisions/{revision.Id}/promote", content: null, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, promoteResponse.StatusCode);

        var promoted = await promoteResponse.Content.ReadFromJsonAsync<PromoteBulletRevisionResponse>(cancellationToken);
        Assert.NotNull(promoted);
        Assert.Equal(revision.RevisedText, promoted!.Bullet.BulletText);
        Assert.False(promoted.Revisions.Single().IsStale, "the promoted version is the current wording");

        // An out-of-range mode binds fine and has to be caught by the handler; a misspelled one
        // never gets that far, because JSON binding rejects it first.
        var rejected = await apiClient.PostAsJsonAsync(
            $"/api/bullets/{bulletId}/revisions",
            new { mode = 99 },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var deleteResponse = await apiClient.DeleteAsync(
            $"/api/bullets/{bulletId}/revisions/{revision.Id}", cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
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

        var selectMissing = await apiClient.PutAsJsonAsync(
            $"/api/artifacts/{missingId}/selection",
            new SelectArtifactVersionsRequest("synthesis", null),
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, selectMissing.StatusCode);
    }

    [Fact]
    public async Task ArtifactSelectionRejectsAVersionTheGenerationDoesNotHave()
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

        var generateResponse = await apiClient.PostAsJsonAsync(
            "/api/generations",
            new GenerationRequest("Backend engineer role using C# and Azure.", "Engineer", "Contoso", 3),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);
        var result = await generateResponse.Content.ReadFromJsonAsync<GenerationResultDto>(cancellationToken);
        Assert.NotNull(result);

        // Integration tests run without AI configured, so the generation has no deep-review
        // versions at all and every label is unknown to it.
        var selectResponse = await apiClient.PutAsJsonAsync(
            $"/api/artifacts/{result!.ArtifactId}/selection",
            new SelectArtifactVersionsRequest("synthesis", null),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, selectResponse.StatusCode);

        // A request that selects nothing is a no-op rather than an error.
        var noOpResponse = await apiClient.PutAsJsonAsync(
            $"/api/artifacts/{result.ArtifactId}/selection",
            new SelectArtifactVersionsRequest(null, null),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, noOpResponse.StatusCode);
        var artifact = await noOpResponse.Content.ReadFromJsonAsync<GenerationArtifactDto>(cancellationToken);
        Assert.NotNull(artifact);
        Assert.Equal(result.ResumeMarkdown, artifact!.ResumeMarkdown);
    }

    [Fact]
    public async Task GenerationEndpointsProduceAndPersistArtifacts()
    {
        // var cts = new CancellationTokenSource();
        var cancellationToken = TestContext.Current.CancellationToken;
        // var cancellationToken = cts.Token;
        // cts.CancelAfter(10000);

        // Deliberately unconfigured AI: everything asserted below is then the deterministic
        // engine's own output, which is what makes this the end-to-end proof that evidence
        // coverage is a complete feature without an AI deployment.
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AngryFoot_AppHost>(TestAiConfiguration.AppHostArgs, cancellationToken);
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

        // A spread the report has something to say about: one quantified, one that names the
        // posting's technologies without a result, and one opening on an assignment.
        foreach (var bulletText in new[]
        {
            "Implemented automated validation workflows that reduced manual review effort by 75%.",
            "Maintained C# services and ASP.NET Core endpoints across the platform.",
            "Responsible for the deployment pipeline."
        })
        {
            await apiClient.PostAsJsonAsync("/api/bullets", new CreateBulletRequest(bulletText), cancellationToken);
        }

        var analyzeResponse = await apiClient.PostAsJsonAsync("/api/generations/analyze", new
        {
            JobDescription = "Senior .NET Engineer role requiring C#, ASP.NET Core, and Azure.",
            JobTitle = "Senior Software Engineer"
        }, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, analyzeResponse.StatusCode);
        var analysis = await analyzeResponse.Content.ReadFromJsonAsync<JobEvidenceAnalysisDto>(cancellationToken);
        Assert.NotNull(analysis);
        Assert.NotNull(analysis!.Job);

        var coverage = analysis.Coverage;
        Assert.NotNull(coverage);
        Assert.Equal(CoverageSourceDto.Deterministic, coverage.Source);
        Assert.False(string.IsNullOrWhiteSpace(coverage.Summary));
        Assert.False(string.IsNullOrWhiteSpace(coverage.Disclaimer));

        AssertScoreIsDerivedFromRequirements(coverage);

        Assert.NotEmpty(coverage.Requirements);
        Assert.All(coverage.Requirements, requirement =>
        {
            Assert.False(string.IsNullOrWhiteSpace(requirement.Why.Reasoning));
            Assert.Equal(requirement.Requirement, requirement.Why.Requirement);

            // Anything credited must name the bullet it was credited for.
            if (requirement.Strength != EvidenceStrengthDto.Missing)
            {
                Assert.NotEmpty(requirement.Why.SupportingEvidence);
                Assert.All(requirement.Why.SupportingEvidence,
                    citation => Assert.False(string.IsNullOrWhiteSpace(citation.BulletText)));
            }
        });

        // This library cannot evidence C#, ASP.NET Core, and Azure at once.
        Assert.Contains(coverage.Diagnostics, x => x.Code == CoverageDiagnosticCodes.MissingSkill);
        // The bullet opening "Responsible for" is caught without any AI involved.
        Assert.Contains(coverage.Diagnostics, x => x.Code == CoverageDiagnosticCodes.OverusedWording);
        // Word matching alone cannot recognise a paraphrase, and the report has to admit that.
        Assert.Contains(coverage.Diagnostics, x => x.Code == CoverageDiagnosticCodes.AnalysisLimitation);

        Assert.Contains(coverage.Diagnostics, x => x.Severity == DiagnosticSeverityDto.Warning);
        Assert.Contains(coverage.Diagnostics, x => x.Severity == DiagnosticSeverityDto.Suggestion);
        Assert.Contains(coverage.Diagnostics, x => x.Severity == DiagnosticSeverityDto.Info);
        Assert.All(coverage.Diagnostics, diagnostic =>
        {
            Assert.NotNull(diagnostic.Why);
            Assert.False(string.IsNullOrWhiteSpace(diagnostic.Why.Reasoning));
            Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
        });

        // The occupational benchmark ships as bundled data, so it is available with no configuration.
        Assert.NotNull(analysis.Benchmark);
        Assert.Equal("15-1252.00", analysis.Benchmark!.SocCode);
        Assert.Equal("Exact", analysis.Benchmark.MatchConfidence);
        Assert.InRange(analysis.Benchmark.CoverageScore, 0, 100);
        Assert.NotEmpty(analysis.Benchmark.Missing);
        Assert.Contains("O*NET", analysis.Benchmark.SourceAttribution);

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

        Assert.NotNull(result.Coverage);
        AssertScoreIsDerivedFromRequirements(result.Coverage!);

        // Every candidate is accounted for, and the ones on the resume are the ones it names.
        Assert.NotNull(result.Explanation);
        Assert.NotEmpty(result.Explanation!.Decisions);
        Assert.All(result.Explanation.Decisions, decision =>
        {
            Assert.False(string.IsNullOrWhiteSpace(decision.Why.Reasoning));
            Assert.Contains(decision.Why.SupportingEvidence, x => x.BulletId == decision.BulletId);
            Assert.Equal(decision.Kind == BulletDecisionKindDto.Omitted, decision.ResumePosition is null);
            Assert.Equal(decision.Kind == BulletDecisionKindDto.Omitted, decision.FinalText is null);
        });

        var onResume = result.Explanation.Decisions
            .Where(x => x.Kind != BulletDecisionKindDto.Omitted)
            .Select(x => x.BulletId)
            .ToHashSet();
        Assert.Equal(result.SelectedBulletIds.ToHashSet(), onResume);

        var artifactsResponse = await apiClient.GetAsync("/api/artifacts", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, artifactsResponse.StatusCode);
        var artifacts = await artifactsResponse.Content.ReadFromJsonAsync<List<ArtifactSummaryDto>>(cancellationToken);
        Assert.NotNull(artifacts);
        Assert.Contains(artifacts!, x => x.Id == result.ArtifactId);

        // The report has to survive the JSON column round trip, or History shows a resume with no
        // record of why it holds the bullets it holds.
        var artifactResponse = await apiClient.GetAsync($"/api/artifacts/{result.ArtifactId}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, artifactResponse.StatusCode);
        var artifact = await artifactResponse.Content.ReadFromJsonAsync<GenerationArtifactDto>(cancellationToken);
        Assert.NotNull(artifact);
        Assert.NotNull(artifact!.Coverage);
        Assert.Equal(result.Coverage!.CoverageScore, artifact.Coverage!.CoverageScore);
        Assert.Equal(result.Coverage.Requirements.Count, artifact.Coverage.Requirements.Count);
        Assert.Equal(result.Coverage.Diagnostics.Count, artifact.Coverage.Diagnostics.Count);

        Assert.NotNull(artifact.Explanation);
        Assert.Equal(result.Explanation!.Summary, artifact.Explanation!.Summary);
        Assert.Equal(
            result.Explanation.Decisions.Select(x => x.BulletId),
            artifact.Explanation.Decisions.Select(x => x.BulletId));

        // The same library, with no posting at all: the generic route takes no job description and
        // still produces a resume, an explanation, and a browsable artifact.
        var genericResponse = await apiClient.PostAsJsonAsync(
            "/api/generations/generic",
            new GenericGenerationRequest(ResumeAudienceDto.TechnicalLeader, "Staff Engineer", MaxBullets: 3),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, genericResponse.StatusCode);
        var generic = await genericResponse.Content.ReadFromJsonAsync<GenerationResultDto>(cancellationToken);
        Assert.NotNull(generic);
        Assert.False(string.IsNullOrWhiteSpace(generic!.ResumeMarkdown));
        Assert.Equal(string.Empty, generic.CoverLetterMarkdown);
        Assert.Null(generic.Coverage);
        Assert.NotNull(generic.Explanation);
        Assert.NotEmpty(generic.Explanation!.Decisions);

        var genericArtifactResponse = await apiClient.GetAsync($"/api/artifacts/{generic.ArtifactId}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, genericArtifactResponse.StatusCode);
        var genericArtifact = await genericArtifactResponse.Content.ReadFromJsonAsync<GenerationArtifactDto>(cancellationToken);
        Assert.NotNull(genericArtifact);
        Assert.True(genericArtifact!.IsGeneric);
        Assert.Equal(ResumeAudienceDto.TechnicalLeader, genericArtifact.Audience);
        Assert.Equal("Staff Engineer", genericArtifact.JobTitle);
        Assert.Equal(string.Empty, genericArtifact.JobDescription);
        Assert.Null(genericArtifact.Coverage);

        // The flag has to survive the summary projection too, or History cannot tell the two kinds
        // of generation apart in its list.
        var mixedArtifacts = await apiClient.GetFromJsonAsync<List<ArtifactSummaryDto>>("/api/artifacts", cancellationToken);
        Assert.NotNull(mixedArtifacts);
        Assert.True(mixedArtifacts!.Single(x => x.Id == generic.ArtifactId).IsGeneric);
        Assert.False(mixedArtifacts.Single(x => x.Id == result.ArtifactId).IsGeneric);

        var badAudience = await apiClient.PostAsJsonAsync(
            "/api/generations/generic",
            new { audience = 42, maxBullets = 3 },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, badAudience.StatusCode);
    }

    /// <summary>
    /// The promise the whole feature rests on: the number is reproducible from the rows the user can
    /// see, rather than being an opinion printed next to them.
    /// </summary>
    private static void AssertScoreIsDerivedFromRequirements(EvidenceCoverageReportDto coverage)
    {
        var expectedTotal = coverage.Requirements.Sum(x => x.Weight * 2);
        var expectedEarned = coverage.Requirements.Sum(x => x.Weight * x.Strength switch
        {
            EvidenceStrengthDto.Strong => 2,
            EvidenceStrengthDto.Weak => 1,
            _ => 0
        });

        Assert.Equal(expectedTotal, coverage.TotalWeight);
        Assert.Equal(expectedEarned, coverage.EarnedWeight);

        var expectedScore = expectedTotal == 0
            ? 0
            : (int)Math.Round(100.0 * expectedEarned / expectedTotal, MidpointRounding.AwayFromZero);

        Assert.Equal(expectedScore, coverage.CoverageScore);
        Assert.InRange(coverage.CoverageScore, 0, 100);
    }
}
