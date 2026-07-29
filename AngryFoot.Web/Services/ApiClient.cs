using System.Net.Http.Json;
using AngryFoot.Contracts;

namespace AngryFoot.Web.Services;

public sealed class ApiClient(HttpClient httpClient)
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

    public async Task<BulletDto> CreateBulletAsync(string bulletText, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync("/api/bullets", new CreateBulletRequest(bulletText), cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BulletDto>(cancellationToken))!;
    }

    public async Task<BulletDto?> UpdateBulletAsync(Guid id, string bulletText, CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/api/bullets/{id}", new UpdateBulletRequest(bulletText), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BulletDto>(cancellationToken);
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

    private static void AddQuery(ICollection<string> query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value.Trim())}");
        }
    }
}
