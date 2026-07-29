using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Application.Generation;

internal sealed class BulletRewriteService(IChatClient chatClient, ILogger<BulletRewriteService> logger)
{
    private sealed record RewriteItem(Guid BulletId, string Rewritten);

    public async Task<IReadOnlyList<RewrittenBullet>> RewriteAsync(
        JobAnalysisDto analysis,
        IReadOnlyList<RankedBullet> selected,
        CancellationToken cancellationToken)
    {
        var fallback = selected.Select(x => new RewrittenBullet(x.Bullet, x.Bullet.BulletText)).ToArray();
        if (selected.Count == 0)
        {
            return fallback;
        }

        var systemPrompt = "You rewrite resume bullets. Preserve factual accuracy. Do not invent technologies, metrics, employers, or responsibilities. Return strict JSON array of { bulletId, rewritten }.";
        var userPrompt = $"Job analysis: {AiJsonUtilities.ToJson(analysis)}\nBullets: {AiJsonUtilities.ToJson(selected.Select(x => new { bulletId = x.Bullet.Id, bulletText = x.Bullet.BulletText }))}";

        try
        {
            var text = await chatClient.GetTextResponseAsync(systemPrompt, userPrompt, cancellationToken);
            if (!AiJsonUtilities.TryDeserialize<List<RewriteItem>>(text, out var rewrites) || rewrites is null)
            {
                logger.LogWarning("Bullet rewrite AI response could not be parsed as JSON. Using original bullet text.");
                return fallback;
            }

            var rewrittenById = rewrites
                .Where(x => x.BulletId != Guid.Empty && !string.IsNullOrWhiteSpace(x.Rewritten))
                .ToDictionary(x => x.BulletId, x => x.Rewritten.Trim());

            return selected
                .Select(x => rewrittenById.TryGetValue(x.Bullet.Id, out var rewritten)
                    ? new RewrittenBullet(x.Bullet, rewritten)
                    : new RewrittenBullet(x.Bullet, x.Bullet.BulletText))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bullet rewrite AI call failed. Using original bullet text.");
            return fallback;
        }
    }
}
