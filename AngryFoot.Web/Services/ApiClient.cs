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

    public async Task<BulletDto> CreateBulletAsync(string bulletText, string? sourceEmployer = null, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/bullets", new CreateBulletRequest(bulletText, sourceEmployer), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BulletDto>(cancellationToken))!;
    }

    public async Task<BulletDto?> UpdateBulletAsync(Guid id, string bulletText, string? sourceEmployer = null, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/api/bullets/{id}", new UpdateBulletRequest(bulletText, sourceEmployer), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BulletDto>(cancellationToken);
    }

    public async Task<RewriteBulletResponse> RewriteBulletAsync(string bulletText, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/bullets/rewrite", new RewriteBulletRequest(bulletText), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RewriteBulletResponse>(cancellationToken))!;
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

    public async Task<JobFitAnalysisDto> AnalyzeJobAsync(string jobDescription, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/generations/analyze", new { jobDescription }, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JobFitAnalysisDto>(cancellationToken))!;
    }

    public async Task<GenerationResultDto> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/generations", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GenerationResultDto>(cancellationToken))!;
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

public sealed record AiStatusResponse(
    bool IsHealthy,
    string Status,
    string? Message = null,
    bool RetrievalEnabled = false,
    string? RetrievalMessage = null);

