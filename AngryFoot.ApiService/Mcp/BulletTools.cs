using System.ComponentModel;
using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.Contracts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AngryFoot.ApiService.Mcp;

/// <summary>
/// MCP tools mirroring the web app's bullet management features. They call the same
/// application services as the REST endpoints, so AI tagging, employer normalization,
/// and heuristic fallbacks behave identically.
/// </summary>
[McpServerToolType]
public static class BulletTools
{
    [McpServerTool(Name = "add_bullet")]
    [Description("Adds a new resume bullet. The bullet is automatically enriched with AI-extracted skills, technologies, tags, job categories, and impact metrics (with a heuristic fallback when AI is unavailable). Optionally associate it with an employer from the profile's work history so generated resumes can group it correctly.")]
    public static async Task<BulletDto> AddBulletAsync(
        IBulletService bulletService,
        [Description("The bullet text, e.g. 'Reduced deployment time by 40% by automating the release pipeline.'")] string bulletText,
        [Description("Optional employer this bullet belongs to; should match an employer name in the profile's work history.")] string? sourceEmployer = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bulletText))
        {
            throw new McpException("bulletText is required.");
        }

        return await bulletService.CreateAsync(new CreateBulletRequest(bulletText, sourceEmployer), cancellationToken);
    }

    [McpServerTool(Name = "update_bullet")]
    [Description("Updates an existing resume bullet's text and/or employer. Changed text is re-enriched with AI metadata; enrichment values the author added by hand are kept, and ones they removed are not reinstated. Changing only the employer leaves enrichment alone.")]
    public static async Task<BulletDto> UpdateBulletAsync(
        IBulletService bulletService,
        [Description("The id of the bullet to update.")] Guid id,
        [Description("The new bullet text.")] string bulletText,
        [Description("Optional employer this bullet belongs to; pass null or omit to clear it.")] string? sourceEmployer = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bulletText))
        {
            throw new McpException("bulletText is required.");
        }

        var updated = await bulletService.UpdateAsync(id, new UpdateBulletRequest(bulletText, sourceEmployer), cancellationToken);
        return updated ?? throw new McpException($"Bullet '{id}' was not found.");
    }

    [McpServerTool(Name = "rewrite_bullet")]
    [Description("Suggests an improved rewrite of bullet text without saving anything, plus suggestions for strengthening it (adding metrics, impact, or technologies). Same feature as the web editor's 'Rewrite Suggestion' button.")]
    public static async Task<RewriteBulletResponse> RewriteBulletAsync(
        IBulletRewriteAssistant rewriteAssistant,
        [Description("The bullet text to improve.")] string bulletText,
        [Description("Run the critique-and-revise pass: three extra AI calls that return labelled alternative versions (v1, v2, v1a, synthesis) in the 'refinement' field, with the best one in 'rewrittenText'. Slower; off by default.")] bool deepReview = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bulletText))
        {
            throw new McpException("bulletText is required.");
        }

        return await rewriteAssistant.RewriteAsync(bulletText, deepReview, cancellationToken);
    }

    [McpServerTool(Name = "enrich_bullet")]
    [Description("Re-runs AI enrichment on an existing bullet to refresh its skills, technologies, tags, job categories, and impact metadata. Merges with what the author wrote rather than replacing it: values they added are kept and values they removed stay removed. Same feature as the web app's 'Retry Enrich' button.")]
    public static async Task<BulletDto> EnrichBulletAsync(
        IBulletService bulletService,
        [Description("The id of the bullet to re-enrich.")] Guid id,
        CancellationToken cancellationToken = default)
    {
        var enriched = await bulletService.EnrichAsync(id, cancellationToken);
        return enriched ?? throw new McpException($"Bullet '{id}' was not found.");
    }

    [McpServerTool(Name = "get_bullet")]
    [Description("Gets a single resume bullet by id, including its enrichment metadata and which of those values the author wrote rather than the tagger.")]
    public static async Task<BulletDto> GetBulletAsync(
        IBulletService bulletService,
        [Description("The id of the bullet.")] Guid id,
        CancellationToken cancellationToken = default)
    {
        var bullet = await bulletService.GetByIdAsync(id, cancellationToken);
        return bullet ?? throw new McpException($"Bullet '{id}' was not found.");
    }

    [McpServerTool(Name = "list_bullets")]
    [Description("Lists resume bullets, optionally filtered by free-text search or by exact (case-insensitive) tag, skill, technology, or job category.")]
    public static async Task<IReadOnlyList<BulletDto>> ListBulletsAsync(
        IBulletService bulletService,
        [Description("Free-text search within the bullet text.")] string? search = null,
        [Description("Filter to bullets carrying this tag.")] string? tag = null,
        [Description("Filter to bullets carrying this skill.")] string? skill = null,
        [Description("Filter to bullets carrying this technology.")] string? technology = null,
        [Description("Filter to bullets carrying this job category.")] string? category = null,
        CancellationToken cancellationToken = default)
    {
        return await bulletService.SearchAsync(search, tag, skill, technology, category, cancellationToken);
    }

    [McpServerTool(Name = "delete_bullet")]
    [Description("Permanently deletes a resume bullet. Same feature as the web app's Delete button.")]
    public static async Task<string> DeleteBulletAsync(
        IBulletService bulletService,
        [Description("The id of the bullet to delete.")] Guid id,
        CancellationToken cancellationToken = default)
    {
        var deleted = await bulletService.DeleteAsync(id, cancellationToken);
        return deleted
            ? $"Bullet '{id}' deleted."
            : throw new McpException($"Bullet '{id}' was not found.");
    }
}
