using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Application.Refinement;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Application.Generation;

internal sealed class BulletRewriteService(
    IChatClient chatClient,
    IDraftRefinementPipeline refinementPipeline,
    ILogger<BulletRewriteService> logger)
{
    private sealed record RewriteItem(Guid BulletId, string Rewritten);

    public async Task<BulletRewriteOutcome> RewriteAsync(
        JobAnalysisDto analysis,
        IReadOnlyList<RankedBullet> selected,
        bool deepReview,
        CancellationToken cancellationToken)
    {
        var fallback = BulletRewriteOutcome.WithoutRefinement(
            selected.Select(x => new RewrittenBullet(x.Bullet, x.Bullet.BulletText)).ToArray());

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

            var applied = ApplyRewrites(selected, rewrites);

            return deepReview
                ? await RefineAsync(analysis, selected, applied, cancellationToken)
                : BulletRewriteOutcome.WithoutRefinement(applied);
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

    /// <summary>
    /// Refines the whole rewrite set as one draft rather than bullet by bullet, so deep review
    /// costs three extra calls per generation instead of three per bullet.
    /// </summary>
    private async Task<BulletRewriteOutcome> RefineAsync(
        JobAnalysisDto analysis,
        IReadOnlyList<RankedBullet> selected,
        IReadOnlyList<RewrittenBullet> applied,
        CancellationToken cancellationToken)
    {
        var originals = selected.Select(x => new { bulletId = x.Bullet.Id, bulletText = x.Bullet.BulletText });

        var refinement = await refinementPipeline.RefineAsync(
            new RefinementRequest(
                ArtifactKind: "set of resume bullet rewrites",
                OutputContract: "a JSON array of objects with exactly the fields bulletId and rewritten - one entry for every bullet id given, each rewritten value a single plain-text resume bullet",
                SourceMaterial: $"Job analysis: {AiJsonUtilities.ToJson(analysis)}\nThe bullets the candidate actually wrote: {AiJsonUtilities.ToJson(originals)}",
                Draft: ToDraftJson(applied),
                GroundingQuery: string.Join(" ", selected.Select(x => x.Bullet.BulletText))),
            cancellationToken);

        if (refinement is null)
        {
            return BulletRewriteOutcome.WithoutRefinement(applied);
        }

        // A version is only offerable if its JSON parses back into a rewrite set; the rest are
        // dropped rather than shown as an empty choice.
        var versionBullets = new Dictionary<string, IReadOnlyList<RewrittenBullet>>();
        var usableVersions = new List<DraftVersionDto>();

        foreach (var version in refinement.Versions)
        {
            if (!AiJsonUtilities.TryDeserialize<List<RewriteItem>>(version.Text, out var items) || items is null)
            {
                logger.LogWarning("Deep review version '{Label}' was not a parseable rewrite set. Dropping it.", version.Label);
                continue;
            }

            versionBullets[version.Label] = ApplyRewrites(selected, items);
            usableVersions.Add(version);
        }

        if (usableVersions.Count < 2)
        {
            logger.LogWarning("Deep review produced no alternative rewrite set to choose from. Using the initial draft.");
            return BulletRewriteOutcome.WithoutRefinement(applied);
        }

        // v1's JSON is ours, so it always parses - a safe landing spot if the recommended version
        // was one of the dropped ones.
        var recommendedLabel = versionBullets.ContainsKey(refinement.RecommendedLabel)
            ? refinement.RecommendedLabel
            : DraftVersionLabels.InitialDraft;

        return new BulletRewriteOutcome(
            versionBullets[recommendedLabel],
            refinement with { RecommendedLabel = recommendedLabel, Versions = usableVersions },
            versionBullets);
    }

    private static IReadOnlyList<RewrittenBullet> ApplyRewrites(IReadOnlyList<RankedBullet> selected, List<RewriteItem> rewrites)
    {
        // Grouped rather than keyed directly: deep review parses up to four AI-authored payloads,
        // and one repeated bulletId should cost that version, not the whole generation.
        var rewrittenById = rewrites
            .Where(x => x.BulletId != Guid.Empty && !string.IsNullOrWhiteSpace(x.Rewritten))
            .GroupBy(x => x.BulletId)
            .ToDictionary(x => x.Key, x => x.First().Rewritten.Trim());

        return selected
            .Select(x => rewrittenById.TryGetValue(x.Bullet.Id, out var rewritten)
                ? new RewrittenBullet(x.Bullet, rewritten)
                : new RewrittenBullet(x.Bullet, x.Bullet.BulletText))
            .ToArray();
    }

    private static string ToDraftJson(IReadOnlyList<RewrittenBullet> bullets)
        => AiJsonUtilities.ToJson(bullets.Select(x => new RewriteItem(x.Bullet.Id, x.Text)));
}
