using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Data;
using AngryFoot.ApiService.Domain;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Application.Refinement;

/// <summary>
/// Grounds the critique agents in the bullet library, preferring semantic retrieval and falling
/// back to term overlap so deep review still has context when no embedding deployment is
/// configured - the same two-path arrangement <see cref="GenerationOrchestrator"/> uses for
/// bullet selection.
/// </summary>
internal sealed class BulletLibraryGrounding(
    AngryFootDbContext dbContext,
    IBulletVectorStore vectorStore,
    ILogger<BulletLibraryGrounding> logger) : IRefinementGrounding
{
    private const int MaxBullets = 5;

    /// <summary>Words too common to say anything about which bullets are relevant.</summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "also", "been", "before", "being", "between", "both", "each", "from",
        "have", "into", "more", "most", "over", "some", "such", "than", "that", "their", "them",
        "then", "there", "these", "they", "this", "through", "using", "were", "what", "when",
        "where", "which", "while", "with", "would", "your"
    };

    public async Task<string> BuildContextAsync(string queryText, CancellationToken cancellationToken)
    {
        try
        {
            var bullets = await FindRelevantBulletsAsync(queryText, cancellationToken);
            if (bullets.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine, bullets.Select(x => $"- {x.BulletText}"));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not build bullet library grounding. Continuing without it.");
            return string.Empty;
        }
    }

    private async Task<IReadOnlyList<Bullet>> FindRelevantBulletsAsync(string queryText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return [];
        }

        if (vectorStore.IsAvailable)
        {
            var matches = await vectorStore.SearchAsync(queryText, MaxBullets, cancellationToken);
            if (matches.Count > 0)
            {
                var matchedIds = matches.Select(x => x.BulletId).ToArray();
                var bulletsById = await dbContext.Bullets
                    .AsNoTracking()
                    .Where(x => matchedIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, cancellationToken);

                // Preserve the store's relevance order rather than the database's.
                var ordered = matchedIds
                    .Where(bulletsById.ContainsKey)
                    .Select(id => bulletsById[id])
                    .ToArray();

                if (ordered.Length > 0)
                {
                    return ordered;
                }
            }
        }

        return await FindByTermOverlapAsync(queryText, cancellationToken);
    }

    private async Task<IReadOnlyList<Bullet>> FindByTermOverlapAsync(string queryText, CancellationToken cancellationToken)
    {
        var terms = ExtractTerms(queryText);
        if (terms.Count == 0)
        {
            return [];
        }

        var bullets = await dbContext.Bullets.AsNoTracking().ToListAsync(cancellationToken);

        return bullets
            // WordStart, like the fit heuristic and the occupation benchmark, so "deploy" in the
            // draft matches a bullet that says "deployed".
            .Select(bullet => (Bullet: bullet, Score: terms.Count(term => BulletEvidence.Supports(bullet, term, EvidenceMatch.WordStart))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Bullet.ModifiedDate)
            .Take(MaxBullets)
            .Select(x => x.Bullet)
            .ToArray();
    }

    private static IReadOnlyList<string> ExtractTerms(string text)
    {
        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.Trim('.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '-', '_'))
            .Where(word => word.Length >= 4 && !StopWords.Contains(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
