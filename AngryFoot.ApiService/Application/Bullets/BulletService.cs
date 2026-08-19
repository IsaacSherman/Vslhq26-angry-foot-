using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Data;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Application.Bullets;

public interface IBulletService
{
    Task<IReadOnlyList<BulletDto>> SearchAsync(string? search, string? tag, string? skill, string? technology, string? category, CancellationToken cancellationToken);
    Task<BulletDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<BulletDto> CreateAsync(CreateBulletRequest request, CancellationToken cancellationToken);
    Task<BulletDto?> UpdateAsync(Guid id, UpdateBulletRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<BulletDto?> EnrichAsync(Guid id, CancellationToken cancellationToken);
    Task<BulletDto?> IndexAsync(Guid id, CancellationToken cancellationToken);
    Task<int> IndexAllMissingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enriches and scores wording that has not been saved, persisting nothing. The returned tagging
    /// can be handed back on the create or update that follows so the AI call is paid for once.
    /// </summary>
    Task<BulletAssessmentDto> AssessAsync(AssessBulletRequest request, CancellationToken cancellationToken);

    /// <summary>Records which quality signals the author has disputed for a bullet.</summary>
    Task<BulletDto?> SetQualityAcknowledgementsAsync(Guid id, IReadOnlyList<string> signals, CancellationToken cancellationToken);

    /// <summary>
    /// What re-running the tagger would change about a bullet's enrichment, without changing it.
    /// Costs an AI call and saves nothing, so the author can see the proposal before deciding.
    /// </summary>
    Task<BulletEnrichmentProposalDto?> ProposeEnrichmentAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Sets a bullet's enrichment to exactly what the author asked for, recording which values they
    /// added and which suggestions they dropped so a later re-enrichment honours both.
    /// </summary>
    Task<BulletDto?> SetEnrichmentAsync(Guid id, SetBulletEnrichmentRequest request, CancellationToken cancellationToken);
}

public sealed class BulletService(
    AngryFootDbContext dbContext,
    IBulletTagger bulletTagger,
    IBulletVectorStore vectorStore,
    ILogger<BulletService> logger) : IBulletService
{
    // Outer safety net; must exceed the tagger's own AI timeout so the tagger can
    // time out gracefully and still return its heuristic fallback.
    private static readonly TimeSpan TaggingTimeout = TimeSpan.FromSeconds(30);

    public async Task<IReadOnlyList<BulletDto>> SearchAsync(string? search, string? tag, string? skill, string? technology, string? category, CancellationToken cancellationToken)
    {
        var bullets = await dbContext.Bullets
            .AsNoTracking()
            .OrderByDescending(x => x.ModifiedDate)
            .ToListAsync(cancellationToken);

        IEnumerable<Bullet> query = bullets;

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x => x.BulletText.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(x => ContainsIgnoreCase(x.Tags, tag));
        }

        if (!string.IsNullOrWhiteSpace(skill))
        {
            query = query.Where(x => ContainsIgnoreCase(x.Skills, skill));
        }

        if (!string.IsNullOrWhiteSpace(technology))
        {
            query = query.Where(x => ContainsIgnoreCase(x.Technologies, technology));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(x => ContainsIgnoreCase(x.JobCategories, category));
        }

        var matched = query.ToArray();
        var indexedIds = await vectorStore.GetIndexedIdsAsync(matched.Select(x => x.Id).ToArray(), cancellationToken);

        return matched.Select(x => x.ToDto(indexedIds.Contains(x.Id))).ToArray();
    }

    public async Task<BulletDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var bullet = await dbContext.Bullets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (bullet is null)
        {
            return null;
        }

        var indexedIds = await vectorStore.GetIndexedIdsAsync([bullet.Id], cancellationToken);
        return bullet.ToDto(indexedIds.Contains(bullet.Id));
    }

    public async Task<BulletDto> CreateAsync(CreateBulletRequest request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var bullet = new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = request.BulletText.Trim(),
            SourceEmployer = NormalizeEmployer(request.SourceEmployer),
            CreatedDate = now,
            ModifiedDate = now,
            EnrichmentState = EnrichmentState.Pending
        };

        await ApplyOrReuseTaggingAsync(bullet, request.Tagging, cancellationToken);
        dbContext.Bullets.Add(bullet);
        await dbContext.SaveChangesAsync(cancellationToken);
        var isIndexed = await vectorStore.UpsertAsync(bullet, cancellationToken);

        return bullet.ToDto(isIndexed);
    }

    public async Task<BulletDto?> UpdateAsync(Guid id, UpdateBulletRequest request, CancellationToken cancellationToken)
    {
        var bullet = await dbContext.Bullets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (bullet is null)
        {
            return null;
        }

        var updatedText = request.BulletText.Trim();
        var textChanged = !string.Equals(bullet.BulletText, updatedText, StringComparison.Ordinal);

        bullet.BulletText = updatedText;
        bullet.SourceEmployer = NormalizeEmployer(request.SourceEmployer);
        bullet.ModifiedDate = DateTime.UtcNow;

        if (textChanged)
        {
            bullet.EnrichmentState = EnrichmentState.Pending;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (textChanged)
        {
            // The judgement that these two bullets were distinct was about the old wording.
            await ForgetIgnoredDuplicatesAsync(id, cancellationToken);

            // Enrichment describes the wording, so only changed wording is worth an AI call - and
            // re-tagging an unchanged bullet would spend one only to churn what is already there.
            await ApplyOrReuseTaggingAsync(bullet, request.Tagging, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        var isIndexed = await vectorStore.UpsertAsync(bullet, cancellationToken);

        return bullet.ToDto(isIndexed);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await dbContext.Bullets.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            await ForgetIgnoredDuplicatesAsync(id, cancellationToken);
            await vectorStore.DeleteAsync(id, cancellationToken);
        }

        return deleted > 0;
    }

    private Task ForgetIgnoredDuplicatesAsync(Guid bulletId, CancellationToken cancellationToken)
    {
        return dbContext.IgnoredBulletDuplicatePairs
            .Where(x => x.BulletIdA == bulletId || x.BulletIdB == bulletId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<BulletDto?> EnrichAsync(Guid id, CancellationToken cancellationToken)
    {
        var bullet = await dbContext.Bullets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (bullet is null)
        {
            return null;
        }

        bullet.ModifiedDate = DateTime.UtcNow;
        bullet.EnrichmentState = EnrichmentState.Pending;

        await dbContext.SaveChangesAsync(cancellationToken);

        await TryApplyTaggingAsync(bullet, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        var isIndexed = await vectorStore.UpsertAsync(bullet, cancellationToken);

        return bullet.ToDto(isIndexed);
    }

    public async Task<BulletEnrichmentProposalDto?> ProposeEnrichmentAsync(Guid id, CancellationToken cancellationToken)
    {
        var bullet = await dbContext.Bullets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (bullet is null)
        {
            return null;
        }

        var suggested = await bulletTagger.TagAsync(bullet.BulletText, cancellationToken);

        return new BulletEnrichmentProposalDto(
            bullet.BulletText,
            Enum.GetValues<EnrichmentFacet>()
                .Select(facet => Compare(bullet, facet, Normalize(SuggestedFor(suggested, facet))))
                .ToArray());
    }

    /// <summary>
    /// What the proposal would do to one facet. A value the author wrote is never reported as
    /// removed: the tagger not thinking of it is not a reason to take it away, and offering to is
    /// how the author loses work by clicking "accept all".
    /// </summary>
    private static EnrichmentFacetProposalDto Compare(Bullet bullet, EnrichmentFacet facet, IReadOnlyList<string> suggested)
    {
        var current = CurrentValues(bullet, facet);

        return new EnrichmentFacetProposalDto(
            facet.ToDto(),
            [.. suggested.Where(value => !ContainsIgnoreCase(current, value))],
            [.. current.Where(value =>
                !ContainsIgnoreCase(suggested, value) && !bullet.UserAuthored.Contains(facet, value))],
            [.. current.Where(value => ContainsIgnoreCase(suggested, value))]);
    }

    public async Task<BulletDto?> SetEnrichmentAsync(Guid id, SetBulletEnrichmentRequest request, CancellationToken cancellationToken)
    {
        var bullet = await dbContext.Bullets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (bullet is null)
        {
            return null;
        }

        foreach (var facet in Enum.GetValues<EnrichmentFacet>())
        {
            var chosen = Normalize(RequestedFor(request, facet));
            var previous = CurrentValues(bullet, facet);

            // Only values the author actually introduced become theirs. Marking everything they
            // submitted as authored would be simpler and wrong: keeping a suggestion is not writing
            // it, and treating it as such would freeze the bullet so re-enrichment could never
            // refresh a tag again.
            var authored = bullet.UserAuthored.For(facet);
            var stillAuthored = chosen.Where(value =>
                bullet.UserAuthored.Contains(facet, value) || !ContainsIgnoreCase(previous, value)).ToList();
            authored.Clear();
            authored.AddRange(stillAuthored);

            var suppressed = bullet.Suppressed.For(facet);
            suppressed.AddRange(previous.Where(value => !ContainsIgnoreCase(chosen, value)));
            var kept = Normalize([.. suppressed.Where(value => !ContainsIgnoreCase(chosen, value))]);
            suppressed.Clear();
            suppressed.AddRange(kept);

            SetValues(bullet, facet, chosen);
        }

        bullet.ModifiedDate = DateTime.UtcNow;
        bullet.EnrichmentState = HasMetadata(bullet) ? EnrichmentState.Enriched : EnrichmentState.Failed;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Skills, technologies and categories are part of a bullet's embedding text, so editing them
        // changes what it retrieves for.
        var isIndexed = await vectorStore.UpsertAsync(bullet, cancellationToken);

        return bullet.ToDto(isIndexed);
    }

    private static IReadOnlyList<string> SuggestedFor(BulletTagging tagging, EnrichmentFacet facet) => facet switch
    {
        EnrichmentFacet.Tags => tagging.Tags,
        EnrichmentFacet.Skills => tagging.Skills,
        EnrichmentFacet.Technologies => tagging.Technologies,
        _ => tagging.JobCategories
    };

    private static IReadOnlyList<string> RequestedFor(SetBulletEnrichmentRequest request, EnrichmentFacet facet) => facet switch
    {
        EnrichmentFacet.Tags => request.Tags,
        EnrichmentFacet.Skills => request.Skills,
        EnrichmentFacet.Technologies => request.Technologies,
        _ => request.JobCategories
    };

    private static List<string> CurrentValues(Bullet bullet, EnrichmentFacet facet) => facet switch
    {
        EnrichmentFacet.Tags => bullet.Tags,
        EnrichmentFacet.Skills => bullet.Skills,
        EnrichmentFacet.Technologies => bullet.Technologies,
        _ => bullet.JobCategories
    };

    private static void SetValues(Bullet bullet, EnrichmentFacet facet, List<string> values)
    {
        switch (facet)
        {
            case EnrichmentFacet.Tags: bullet.Tags = values; break;
            case EnrichmentFacet.Skills: bullet.Skills = values; break;
            case EnrichmentFacet.Technologies: bullet.Technologies = values; break;
            default: bullet.JobCategories = values; break;
        }
    }

    private static bool HasMetadata(Bullet bullet)
        => bullet.Tags.Count > 0
        || bullet.Skills.Count > 0
        || bullet.Technologies.Count > 0
        || bullet.JobCategories.Count > 0
        || bullet.Impact.Count > 0;

    /// <summary>
    /// Embeds and upserts an existing bullet into the vector store as-is, without touching its
    /// enrichment metadata or re-running AI tagging. Used to backfill bullets that predate
    /// semantic retrieval being configured.
    /// </summary>
    public async Task<BulletDto?> IndexAsync(Guid id, CancellationToken cancellationToken)
    {
        var bullet = await dbContext.Bullets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (bullet is null)
        {
            return null;
        }

        var isIndexed = await vectorStore.UpsertAsync(bullet, cancellationToken);

        return bullet.ToDto(isIndexed);
    }

    /// <summary>
    /// Indexes every bullet that doesn't yet have a point in the vector store, without touching
    /// enrichment metadata. Returns the number successfully upserted rather than the number
    /// attempted, so a single bad bullet does not make the UI overstate backfill progress.
    /// </summary>
    public async Task<int> IndexAllMissingAsync(CancellationToken cancellationToken)
    {
        var bullets = await dbContext.Bullets.AsNoTracking().ToListAsync(cancellationToken);
        var allIds = bullets.Select(x => x.Id).ToArray();
        var alreadyIndexed = await vectorStore.GetIndexedIdsAsync(allIds, cancellationToken);
        var missing = bullets.Where(x => !alreadyIndexed.Contains(x.Id)).ToArray();

        if (missing.Length == 0)
        {
            return 0;
        }

        var indexedCount = 0;
        foreach (var bullet in missing)
        {
            if (await vectorStore.UpsertAsync(bullet, cancellationToken))
            {
                indexedCount++;
            }
        }

        return indexedCount;
    }

    public async Task<BulletAssessmentDto> AssessAsync(AssessBulletRequest request, CancellationToken cancellationToken)
    {
        // A detached bullet: enrichment and scoring both need one, and nothing here is persisted.
        var draft = new Bullet
        {
            Id = Guid.Empty,
            BulletText = request.BulletText.Trim(),
            AcknowledgedQualitySignals = (request.AcknowledgedSignals ?? []).ToList()
        };

        await TryApplyTaggingAsync(draft, cancellationToken);

        return new BulletAssessmentDto(
            BulletQualityScorer.Score(draft),
            new BulletTaggingDto(
                draft.BulletText,
                draft.Tags,
                draft.Skills,
                draft.Technologies,
                draft.JobCategories,
                draft.Impact));
    }

    public async Task<BulletDto?> SetQualityAcknowledgementsAsync(
        Guid id,
        IReadOnlyList<string> signals,
        CancellationToken cancellationToken)
    {
        var bullet = await dbContext.Bullets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (bullet is null)
        {
            return null;
        }

        bullet.AcknowledgedQualitySignals = Normalize(signals);
        await dbContext.SaveChangesAsync(cancellationToken);

        var indexedIds = await vectorStore.GetIndexedIdsAsync([bullet.Id], cancellationToken);
        return bullet.ToDto(indexedIds.Contains(bullet.Id));
    }

    /// <summary>
    /// Uses tagging the caller already paid for, when it describes the text being saved. The text
    /// has to match because tagging that describes different wording is worse than none: it would
    /// file the bullet under skills it never mentions.
    /// </summary>
    private async Task ApplyOrReuseTaggingAsync(
        Bullet bullet,
        BulletTaggingDto? tagging,
        CancellationToken cancellationToken)
    {
        if (tagging is null || !string.Equals(tagging.ForText.Trim(), bullet.BulletText, StringComparison.Ordinal))
        {
            await TryApplyTaggingAsync(bullet, cancellationToken);
            return;
        }

        ApplyEnrichment(bullet, tagging.ToTagging());
    }

    private async Task TryApplyTaggingAsync(Bullet bullet, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TaggingTimeout);

        try
        {
            await ApplyTaggingAsync(bullet, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            bullet.EnrichmentState = EnrichmentState.Failed;
        }
        catch (Exception ex)
        {
            if(logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Failed to enrich bullet with ID {BulletId}", bullet.Id);
            }
            bullet.EnrichmentState = EnrichmentState.Failed;
            
        }
        finally
        {
            if (bullet.EnrichmentState == EnrichmentState.Pending)
            {
                if(logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed without exception- bullet with ID {BulletId} is still pending after enrichment attempt", bullet.Id);
                }
                bullet.EnrichmentState = EnrichmentState.Failed;
            }
        }
    }

    private async Task ApplyTaggingAsync(Bullet bullet, CancellationToken cancellationToken)
    {
        ApplyEnrichment(bullet, await bulletTagger.TagAsync(bullet.BulletText, cancellationToken));
    }

    /// <summary>
    /// Writes what the tagger found onto the bullet, keeping what the author wrote.
    /// <para>
    /// The rule is <c>(tagger + author) - removed</c>, and it is applied before
    /// <see cref="Normalize"/> rather than after: normalization is case-insensitive, so a later merge
    /// would silently collapse the author's "Kubernetes" into the tagger's "kubernetes" and lose
    /// which of them the value came from.
    /// </para>
    /// </summary>
    private static void ApplyEnrichment(Bullet bullet, BulletTagging tagging)
    {
        bullet.Tags = Merge(tagging.Tags, EnrichmentFacet.Tags, bullet);
        bullet.Skills = Merge(tagging.Skills, EnrichmentFacet.Skills, bullet);
        bullet.Technologies = Merge(tagging.Technologies, EnrichmentFacet.Technologies, bullet);
        bullet.JobCategories = Merge(tagging.JobCategories, EnrichmentFacet.JobCategories, bullet);

        // Impact is extracted figures rather than classification, so there is nothing for an author
        // to curate and nothing to preserve across a re-run.
        bullet.Impact = Normalize(tagging.Impact);

        bullet.EnrichmentState = HasMetadata(bullet) ? EnrichmentState.Enriched : EnrichmentState.Failed;
    }

    private static List<string> Merge(IReadOnlyList<string> suggested, EnrichmentFacet facet, Bullet bullet)
    {
        return Normalize([
            .. suggested.Where(value => !bullet.Suppressed.Contains(facet, value)),
            .. bullet.UserAuthored.For(facet)
        ]);
    }

    private static string? NormalizeEmployer(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ContainsIgnoreCase(IEnumerable<string> values, string value)
    {
        return values.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> Normalize(IReadOnlyList<string> values)
    {
        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
