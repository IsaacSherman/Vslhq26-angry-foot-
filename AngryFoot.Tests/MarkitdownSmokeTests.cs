using System.Net;
using System.Net.Http.Json;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fixtures;
using Microsoft.Extensions.Logging;

namespace AngryFoot.Tests;

/// <summary>
/// The only test that proves the container contract - the tool name, the <c>data:</c> URI's media
/// type, the shape of the answer - so a green default run says nothing about whether uploads work.
/// Opt in with <c>RUN_MARKITDOWN_INTEGRATION=1</c> and Docker running.
/// <para>
/// It is also how <c>ResumeN.md</c> is validated: those fixtures are the checked-in output of this
/// conversion, and the corpus asserts they parse to what their <c>ResumeN.txt</c> twin parses to.
/// Regenerate one by running this and saving the markdown the endpoint was handed.
/// </para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
public class MarkitdownSmokeTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    [Theory]
    [InlineData("AnonymizedResumeStandard2.docx", "Resume7")]
    [InlineData("AnonymizedResumeFromPdf3.docx", "Resume8")]
    public async Task UploadingADocument_WhenEnabled_YieldsWhatPastingItsTextYields(string fileName, string corpusCase)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("RUN_MARKITDOWN_INTEGRATION"), "1", StringComparison.Ordinal))
        {
            return;
        }

        var cancellationToken = TestContext.Current.CancellationToken;

        // Qdrant stays off - this is about the converter, and duplicate detection is compared
        // lexically either way with an empty library.
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AngryFoot_AppHost>(
            ["--Qdrant:Enabled=false"], cancellationToken);
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

        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(
            await File.ReadAllBytesAsync(Path.Combine(ResumeCorpus.Directory, fileName), cancellationToken));
        content.Add(fileContent, "file", fileName);

        var response = await apiClient.PostAsync("/api/bullets/import/resume/preview/file", content, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var preview = await response.Content.ReadFromJsonAsync<ResumeImportPreviewResponse>(cancellationToken);
        var expected = ResumeCorpus.Load(corpusCase);

        Assert.Equal(expected.ExpectedBullets, preview!.Candidates.Select(x => x.BulletText).ToArray());
    }
}
