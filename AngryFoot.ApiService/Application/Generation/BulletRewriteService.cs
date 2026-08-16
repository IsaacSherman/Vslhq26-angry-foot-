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

    /// <summary>
    /// Envelope for the first draft only. A strict JSON schema has to be rooted in an object, so
    /// the array the agents exchange between themselves gets wrapped for the one call we can
    /// constrain. The refinement stages still pass bare arrays, because their bullet set travels
    /// as a string inside another payload where no schema can reach it.
    /// </summary>
    private sealed record RewriteSet(IReadOnlyList<RewriteItem> Bullets);

    /// <param name="bench">
    /// Runner-up bullets the ranker did not select. Deep review may swap these in; the initial
    /// draft never sees them.
    /// </param>
    /// <param name="target">The posting, or the audience when there is no posting.</param>
    /// <param name="guidance">The candidate's clarification of their own material, if any.</param>
    public async Task<BulletRewriteOutcome> RewriteAsync(
        RewriteTarget target,
        IReadOnlyList<RankedBullet> selected,
        IReadOnlyList<RankedBullet> bench,
        string? guidance,
        bool deepReview,
        CancellationToken cancellationToken)
    {
        var fallback = BulletRewriteOutcome.WithoutRefinement(
            selected.Select(x => new RewrittenBullet(x.Bullet, x.Bullet.BulletText)).ToArray());

        if (selected.Count == 0)
        {
            return fallback;
        }

        var systemPrompt = "You rewrite resume bullets. Preserve factual accuracy. Do not invent technologies, metrics, employers, or responsibilities. Return strict JSON: { \"bullets\": [ { \"bulletId\", \"rewritten\" } ] } with one entry per bullet given.";
        var userPrompt = $"{target.Brief}\nBullets: {AiJsonUtilities.ToJson(ToPayload(selected))}{FormatGuidance(guidance)}";

        try
        {
            var response = await chatClient.GetJsonResponseAsync<RewriteSet>(systemPrompt, userPrompt, cancellationToken, logger);
            if (response.Value?.Bullets is not { } rewrites)
            {
                logger.LogWarning(
                    "Bullet rewrite AI response could not be parsed as JSON. Using original bullet text. Raw response: {RawResponse}",
                    AiJsonUtilities.ForLog(response.RawText));
                return fallback;
            }

            // The first draft only rewords, in the ranker's order - the behaviour that predates
            // deep review, and the one the no-AI fallback path has to stay compatible with.
            var applied = selected
                .Select(x => new RewrittenBullet(x.Bullet, RewrittenTextFor(rewrites, x.Bullet)))
                .ToArray();

            return deepReview
                ? await RefineAsync(target, selected, bench, applied, guidance, cancellationToken)
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
    /// costs three extra calls per generation instead of three per bullet. Unlike the first draft,
    /// these stages may reorder the set and swap bench bullets in for weak ones - a resume is read
    /// top-down, so sequencing is as much of an editorial choice as wording.
    /// </summary>
    private async Task<BulletRewriteOutcome> RefineAsync(
        RewriteTarget target,
        IReadOnlyList<RankedBullet> selected,
        IReadOnlyList<RankedBullet> bench,
        IReadOnlyList<RewrittenBullet> applied,
        string? guidance,
        CancellationToken cancellationToken)
    {
        var pool = selected.Concat(bench).ToDictionary(x => x.Bullet.Id, x => x.Bullet);

        var sourceMaterial = $"""
            {target.Brief}
            The bullets currently on the resume, in order, as the candidate actually wrote them: {AiJsonUtilities.ToJson(ToPayload(selected))}
            Other bullets in the candidate's library that were not selected, available to swap in: {AiJsonUtilities.ToJson(ToPayload(bench))}
            The resume holds exactly {selected.Count} bullet(s). Every bulletId you return must come from one of the two lists above.
            """;

        var refinement = await refinementPipeline.RefineAsync(
            new RefinementRequest(
                ArtifactKind: "ordered set of resume bullets",
                OutputContract:
                    $"a JSON array of at most {selected.Count} objects with exactly the fields bulletId and rewritten, each rewritten value a single plain-text resume bullet. " +
                    $"Array order is the order the bullets appear on the resume, {target.OrderingRule}. " +
                    "You may reorder them, and you may drop a weak bullet and swap in a stronger one from the unselected list, but only bulletIds from the lists provided",
                SourceMaterial: sourceMaterial,
                Draft: ToDraftJson(applied),
                GroundingQuery: string.Join(" ", selected.Select(x => x.Bullet.BulletText)),
                UserGuidance: guidance),
            cancellationToken);

        if (refinement is null)
        {
            return BulletRewriteOutcome.WithoutRefinement(applied);
        }

        // A version is only offerable if its JSON parses back into a usable bullet set; the rest
        // are dropped rather than shown as an empty choice.
        var versionBullets = new Dictionary<string, IReadOnlyList<RewrittenBullet>>();
        var usableVersions = new List<DraftVersionDto>();

        foreach (var version in refinement.Versions)
        {
            var bullets = TryReadBulletSet(version, pool, selected.Count);
            if (bullets is null)
            {
                continue;
            }

            versionBullets[version.Label] = bullets;
            usableVersions.Add(version);
        }

        if (usableVersions.Count < 2)
        {
            logger.LogWarning("Deep review produced no alternative bullet set to choose from. Using the initial draft.");
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

    /// <summary>
    /// Reads one refined version back into bullets, honouring the order it came in. Ids outside
    /// the pool are hallucinations and are dropped; repeats keep their first appearance; the set
    /// is capped at the resume's bullet count so a runaway version cannot pad the document.
    /// </summary>
    private IReadOnlyList<RewrittenBullet>? TryReadBulletSet(
        DraftVersionDto version, IReadOnlyDictionary<Guid, Bullet> pool, int maxBullets)
    {
        if (!AiJsonUtilities.TryDeserialize<List<RewriteItem>>(version.Text, out var items) || items is null)
        {
            logger.LogWarning(
                "Deep review version '{Label}' was not a parseable bullet set. Dropping it. Raw version text: {RawResponse}",
                version.Label,
                AiJsonUtilities.ForLog(version.Text));
            return null;
        }

        var seen = new HashSet<Guid>();
        var bullets = new List<RewrittenBullet>();

        foreach (var item in items)
        {
            if (!pool.TryGetValue(item.BulletId, out var bullet))
            {
                logger.LogWarning(
                    "Deep review version '{Label}' referenced bullet {BulletId}, which is not in the candidate pool. Skipping it.",
                    version.Label,
                    item.BulletId);
                continue;
            }

            if (!seen.Add(item.BulletId))
            {
                continue;
            }

            bullets.Add(new RewrittenBullet(
                bullet,
                string.IsNullOrWhiteSpace(item.Rewritten) ? bullet.BulletText : item.Rewritten.Trim()));

            if (bullets.Count == maxBullets)
            {
                break;
            }
        }

        if (bullets.Count == 0)
        {
            logger.LogWarning("Deep review version '{Label}' selected no usable bullets. Dropping it.", version.Label);
            return null;
        }

        return bullets;
    }

    private static string RewrittenTextFor(IReadOnlyList<RewriteItem> rewrites, Bullet bullet)
    {
        // First match wins rather than keying the whole list: a repeated bulletId should not throw
        // away an otherwise good rewrite set.
        var match = rewrites.FirstOrDefault(x => x.BulletId == bullet.Id && !string.IsNullOrWhiteSpace(x.Rewritten));
        return match is null ? bullet.BulletText : match.Rewritten.Trim();
    }

    private static object ToPayload(IReadOnlyList<RankedBullet> bullets)
        => bullets.Select(x => new { bulletId = x.Bullet.Id, bulletText = x.Bullet.BulletText });

    private static string FormatGuidance(string? guidance)
        => string.IsNullOrWhiteSpace(guidance)
            ? string.Empty
            : $"\nThe candidate has clarified what their bullets mean. Treat this as fact: {guidance.Trim()}";

    private static string ToDraftJson(IReadOnlyList<RewrittenBullet> bullets)
        => AiJsonUtilities.ToJson(bullets.Select(x => new RewriteItem(x.Bullet.Id, x.Text)));
}
