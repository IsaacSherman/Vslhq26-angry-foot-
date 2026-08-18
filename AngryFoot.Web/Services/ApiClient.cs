using System.Net.Http.Json;
using AngryFoot.Contracts;

namespace AngryFoot.Web.Services;

public sealed class ApiClient(HttpClient httpClient, ILogger<ApiClient> logger)
{
    public async Task<List<BulletDto>> GetBulletsAsync(string? search, string? tag, string? skill, string? technology, string? category, CancellationToken cancellationToken = default)
    {
        var query = new List<string>();
        AddQuery(query, "search", search);
        AddQuery(query, "tag", tag);
        AddQuery(query, "skill", skill);
        AddQuery(query, "technology", technology);
        AddQuery(query, "category", category);

        var uri = query.Count == 0
            ? "/api/bullets"
            : $"/api/bullets?{string.Join("&", query)}";

        return await httpClient.GetFromJsonAsync<List<BulletDto>>(uri, cancellationToken) ?? [];
    }

    public async Task<BulletDto?> GetBulletAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<BulletDto>($"/api/bullets/{id}", cancellationToken);
    }

    /// <param name="tagging">
    /// Enrichment from a prior assess of the same text, so saving does not pay for it again.
    /// Ignored by the API when it describes different wording.
    /// </param>
    public async Task<BulletDto> CreateBulletAsync(string bulletText, string? sourceEmployer = null, BulletTaggingDto? tagging = null, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/bullets", new CreateBulletRequest(bulletText, sourceEmployer, tagging), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BulletDto>(cancellationToken))!;
    }

    /// <param name="tagging">See <see cref="CreateBulletAsync"/>.</param>
    public async Task<BulletDto?> UpdateBulletAsync(Guid id, string bulletText, string? sourceEmployer = null, BulletTaggingDto? tagging = null, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/api/bullets/{id}", new UpdateBulletRequest(bulletText, sourceEmployer, tagging), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BulletDto>(cancellationToken);
    }

    public async Task<RewriteBulletResponse> RewriteBulletAsync(string bulletText, bool deepReview = false, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/bullets/rewrite", new RewriteBulletRequest(bulletText, deepReview), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RewriteBulletResponse>(cancellationToken))!;
    }

    /// <summary>
    /// Phase one of a guided deep review. Null when there was no AI draft to critique, in which
    /// case the caller should fall back to <see cref="RewriteBulletAsync"/>.
    /// </summary>
    public async Task<BulletRewriteCritiqueResponse?> CritiqueBulletRewriteAsync(string bulletText, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/bullets/rewrite/critique", new RewriteBulletRequest(bulletText, DeepReview: true), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BulletRewriteCritiqueResponse>(cancellationToken);
    }

    public async Task<RewriteBulletResponse> CompleteBulletRewriteAsync(CompleteBulletRewriteRequest request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/bullets/rewrite/complete", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RewriteBulletResponse>(cancellationToken))!;
    }

    /// <summary>Scores unsaved wording. Nothing is persisted.</summary>
    public async Task<BulletAssessmentDto> AssessBulletAsync(
        string bulletText,
        IReadOnlyList<string>? acknowledgedSignals = null,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/bullets/assess",
            new AssessBulletRequest(bulletText, acknowledgedSignals),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BulletAssessmentDto>(cancellationToken))!;
    }

    public async Task<BulletDto?> SetBulletQualityAcknowledgementsAsync(
        Guid id,
        IReadOnlyList<string> signals,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"/api/bullets/{id}/quality-acknowledgements",
            new SetBulletQualityAcknowledgementsRequest(signals),
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BulletDto>(cancellationToken);
    }

    public async Task<List<BulletRevisionDto>> GetBulletRevisionsAsync(Guid bulletId, CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<List<BulletRevisionDto>>($"/api/bullets/{bulletId}/revisions", cancellationToken) ?? [];
    }

    public async Task<BulletRevisionDto?> CreateBulletRevisionAsync(
        Guid bulletId,
        BulletRevisionModeDto mode,
        bool deepReview = false,
        string? guidance = null,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/api/bullets/{bulletId}/revisions",
            new CreateBulletRevisionRequest(mode, deepReview, guidance),
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BulletRevisionDto>(cancellationToken);
    }

    public async Task<bool> DeleteBulletRevisionAsync(Guid bulletId, Guid revisionId, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync($"/api/bullets/{bulletId}/revisions/{revisionId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>Makes a revision the bullet's canonical text; returns the updated bullet and revisions.</summary>
    public async Task<PromoteBulletRevisionResponse?> PromoteBulletRevisionAsync(
        Guid bulletId,
        Guid revisionId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"/api/bullets/{bulletId}/revisions/{revisionId}/promote", null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PromoteBulletRevisionResponse>(cancellationToken);
    }

    public async Task<bool> DeleteBulletAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync($"/api/bullets/{id}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    public async Task<BulletDto?> EnrichBulletAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"/api/bullets/{id}/enrich", null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BulletDto>(cancellationToken);
    }

    public async Task<BulletDto?> IndexBulletAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync($"/api/bullets/{id}/index", null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BulletDto>(cancellationToken);
    }

    public async Task<int> IndexMissingBulletsAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsync("/api/bullets/index-missing", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<IndexMissingBulletsResponse>(cancellationToken);
        return result?.IndexedCount ?? 0;
    }

    public async Task<ResumeImportPreviewResponse> PreviewResumeImportAsync(string resumeText, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/bullets/import/resume/preview", new ResumeImportPreviewRequest(resumeText), cancellationToken);
        await EnsureImportSucceededAsync(response, "Resume import", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ResumeImportPreviewResponse>(cancellationToken))!;
    }

    public async Task<ResumeImportPreviewResponse> PreviewResumeImportFromFileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        content.Add(fileContent, "file", fileName);

        var response = await httpClient.PostAsync("/api/bullets/import/resume/preview/file", content, cancellationToken);
        await EnsureImportSucceededAsync(response, "Resume import", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ResumeImportPreviewResponse>(cancellationToken))!;
    }

    public async Task<ResumeReviewReportDto> ReviewResumeAsync(string resumeText, string? jobDescription, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/resume-review", new ResumeReviewRequest(resumeText, jobDescription), cancellationToken);
        await EnsureImportSucceededAsync(response, "Resume review", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ResumeReviewReportDto>(cancellationToken))!;
    }

    public async Task<ResumeReviewReportDto> ReviewResumeFromFileAsync(Stream fileStream, string fileName, string? jobDescription, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        content.Add(fileContent, "file", fileName);

        if (!string.IsNullOrWhiteSpace(jobDescription))
        {
            content.Add(new StringContent(jobDescription), "jobDescription");
        }

        var response = await httpClient.PostAsync("/api/resume-review/file", content, cancellationToken);
        await EnsureImportSucceededAsync(response, "Resume review", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ResumeReviewReportDto>(cancellationToken))!;
    }

    public async Task<ResumeImportResultDto> ConfirmResumeImportAsync(IReadOnlyList<ImportBulletItem> bullets, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/bullets/import/resume", new ConfirmResumeImportRequest(bullets), cancellationToken);
        await EnsureImportSucceededAsync(response, "Resume import", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<ResumeImportResultDto>(cancellationToken))!;
    }

    public async Task<ProfileDto> GetProfileAsync(CancellationToken cancellationToken = default)
    {
        return (await httpClient.GetFromJsonAsync<ProfileDto>("/api/profile", cancellationToken))!;
    }

    public async Task<ProfileDto> SaveProfileAsync(ProfileDto profile, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync("/api/profile", profile, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProfileDto>(cancellationToken))!;
    }

    public async Task<LinkedInImportResultDto> ImportLinkedInProfileAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        content.Add(fileContent, "file", fileName);

        var response = await httpClient.PostAsync("/api/profile/import/linkedin", content, cancellationToken);
        await EnsureImportSucceededAsync(response, "LinkedIn import", cancellationToken);

        return (await response.Content.ReadFromJsonAsync<LinkedInImportResultDto>(cancellationToken))!;
    }

    /// <summary>
    /// Surfaces the endpoint's own message for a failed import, which the import pages show
    /// verbatim. Results.BadRequest(string) serializes the message as a JSON string literal.
    /// </summary>
    private static async Task EnsureImportSucceededAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? message = null;
        try
        {
            message = await response.Content.ReadFromJsonAsync<string>(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Not a plain-string body (e.g. ProblemDetails from an unhandled failure); fall through.
        }

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
            ? $"{operation} failed with status {(int)response.StatusCode}."
            : message);
    }

    public async Task<JobEvidenceAnalysisDto> AnalyzeJobAsync(string jobDescription, string? jobTitle = null, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/generations/analyze", new { jobDescription, jobTitle }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JobEvidenceAnalysisDto>(cancellationToken))!;
    }

    public async Task<GenerationResultDto> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/generations", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GenerationResultDto>(cancellationToken))!;
    }

    /// <summary>
    /// Generates from the whole bullet library with no posting to aim at. The result's
    /// <see cref="GenerationResultDto.CoverLetterMarkdown"/> is empty and its
    /// <see cref="GenerationResultDto.Coverage"/> null by design; see the API's /generic route.
    /// </summary>
    public async Task<GenerationResultDto> GenerateGenericAsync(GenericGenerationRequest request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/generations/generic", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GenerationResultDto>(cancellationToken))!;
    }

    /// <summary>
    /// What a generic generation would select, without generating anything. Costs no AI call, so
    /// it can be run freely while the user tries different target titles.
    /// </summary>
    public async Task<GenericPreviewDto> PreviewGenericAsync(GenericGenerationRequest request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/generations/generic/preview", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GenericPreviewDto>(cancellationToken))!;
    }

    public async Task<List<ArtifactSummaryDto>> GetArtifactsAsync(CancellationToken cancellationToken = default)
    {
        return await httpClient.GetFromJsonAsync<List<ArtifactSummaryDto>>("/api/artifacts", cancellationToken) ?? [];
    }

    public async Task<GenerationArtifactDto?> GetArtifactAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync($"/api/artifacts/{id}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GenerationArtifactDto>(cancellationToken);
    }

    /// <summary>
    /// Promotes a stored deep-review version to be the artifact's resume and/or cover letter, so
    /// history keeps the version the user actually chose. Pass null for a document to leave it be.
    /// </summary>
    public async Task<GenerationArtifactDto?> SelectArtifactVersionsAsync(
        Guid id,
        string? resumeVersionLabel,
        string? coverLetterVersionLabel,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            $"/api/artifacts/{id}/selection",
            new SelectArtifactVersionsRequest(resumeVersionLabel, coverLetterVersionLabel),
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GenerationArtifactDto>(cancellationToken);
    }

    public async Task<bool> DeleteArtifactAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync($"/api/artifacts/{id}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private static void AddQuery(ICollection<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value.Trim())}");
        }
    }

    public async Task<AiStatusResponse?> GetAiStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await httpClient.GetFromJsonAsync<AiStatusResponse>("/api/ai/status", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch AI status from the API service.");
            return null;
        }
    }
}

