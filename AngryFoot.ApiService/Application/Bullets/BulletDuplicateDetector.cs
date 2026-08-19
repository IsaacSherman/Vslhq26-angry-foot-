using System.Text.RegularExpressions;
using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Data;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Application.Bullets;

/// <summary>
/// One text being scanned for near-duplicates. <paramref name="BulletId"/> is null for an imported
/// candidate that has not been created yet; ignored-pair suppression only applies once both sides
/// of a pair have real ids.
/// </summary>
public sealed record DuplicateSubject(int Index, Guid? BulletId, string Text);

public sealed record DuplicateScanResult(
    IReadOnlyDictionary<int, IReadOnlyList<DuplicateWarningDto>> WarningsByIndex,
    DuplicateDetectionModeDto Mode,
    string? Message);

public interface IBulletDuplicateDetector
{
    Task<DuplicateScanResult> DetectAsync(IReadOnlyList<DuplicateSubject> subjects, CancellationToken cancellationToken);
}

public sealed class BulletDuplicateDetector(
    AngryFootDbContext dbContext,
    IBulletVectorStore vectorStore,
    ITextEmbedder embedder) : IBulletDuplicateDetector
{
    // Feature #12 proposed 0.90, but measurement against the real embedding deployment showed
    // near-duplicates land well below it: two wordings of one achievement scored 0.896, and a
    // resume line paraphrasing an already-indexed bullet only 0.812 — partly because indexed
    // vectors also carry the bullet's enriched tags. Unrelated bullets sat at 0.22-0.45, leaving
    // 0.80 in a wide empty gap. Token-overlap lands in the same place: two bullets differing by a
    // single plural scored 0.846, while distinct achievements share far less.
    private const double DuplicateThreshold = 0.80;
    private const int NearestNeighbourCount = 5;

    private static readonly Regex NonAlphanumeric = new(@"[^a-z0-9\s]", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    public async Task<DuplicateScanResult> DetectAsync(IReadOnlyList<DuplicateSubject> subjects, CancellationToken cancellationToken)
    {
        if (subjects.Count == 0)
        {
            return new DuplicateScanResult(new Dictionary<int, IReadOnlyList<DuplicateWarningDto>>(), DuplicateDetectionModeDto.Semantic, null);
        }

        var existing = await dbContext.Bullets
            .AsNoTracking()
            .Select(x => new ExistingBullet(x.Id, x.BulletText))
            .ToListAsync(cancellationToken);

        var ignoredPairs = await LoadIgnoredPairsAsync(cancellationToken);
        var collector = new WarningCollector(subjects, ignoredPairs);

        // Normalized exact matches are unambiguous duplicates, and catching them here also covers
        // the asymmetry of the semantic path: indexed vectors include enriched tags that the raw
        // candidate text does not, which nudges even identical bullets below a perfect score.
        AddExactMatches(collector, subjects, existing);

        var (mode, message) = await AddSimilarityMatchesAsync(collector, subjects, existing, cancellationToken);

        return new DuplicateScanResult(collector.Build(), mode, message);
    }

    private async Task<(DuplicateDetectionModeDto Mode, string? Message)> AddSimilarityMatchesAsync(
        WarningCollector collector,
        IReadOnlyList<DuplicateSubject> subjects,
        IReadOnlyList<ExistingBullet> existing,
        CancellationToken cancellationToken)
    {
        // The existing-bullet pass searches the vector index, so this path needs Qdrant and not
        // merely an embedding deployment.
        if (vectorStore.IsAvailable)
        {
            var vectors = await embedder.EmbedAsync(subjects.Select(x => x.Text).ToArray(), cancellationToken);

            if (vectors is not null)
            {
                await AddSemanticExistingMatchesAsync(collector, subjects, existing, cancellationToken);
                AddSemanticBatchMatches(collector, subjects, vectors);

                // A bullet can sit in SQLite without a point in Qdrant — indexing fails silently and
                // the Bullets page offers a backfill for exactly that. Those are invisible to the
                // vector search, so compare them by text rather than let them go unchecked.
                var unindexed = await FindUnindexedAsync(existing, cancellationToken);
                AddLexicalExistingMatches(collector, subjects, unindexed);

                return (DuplicateDetectionModeDto.Semantic, DescribeIndexGap(unindexed.Count));
            }

            AddLexicalMatches(collector, subjects, existing);
            return (DuplicateDetectionModeDto.Lexical, "Embedding the candidates failed, so duplicates were compared by text only. Near-duplicates worded differently may not be flagged.");
        }

        AddLexicalMatches(collector, subjects, existing);
        return (DuplicateDetectionModeDto.Lexical, "Semantic duplicate detection is unavailable, so duplicates were compared by text only. Near-duplicates worded differently may not be flagged.");
    }

    private async Task AddSemanticExistingMatchesAsync(
        WarningCollector collector,
        IReadOnlyList<DuplicateSubject> subjects,
        IReadOnlyList<ExistingBullet> existing,
        CancellationToken cancellationToken)
    {
        var textsById = existing.ToDictionary(x => x.Id, x => x.Text);

        foreach (var subject in subjects)
        {
            var matches = await vectorStore.SearchAsync(subject.Text, NearestNeighbourCount, cancellationToken);
            foreach (var match in matches)
            {
                if (match.Score <= DuplicateThreshold || !textsById.TryGetValue(match.BulletId, out var text))
                {
                    continue;
                }

                collector.AddExisting(subject, match.BulletId, text, match.Score);
            }
        }
    }

    private static void AddSemanticBatchMatches(
        WarningCollector collector,
        IReadOnlyList<DuplicateSubject> subjects,
        IReadOnlyList<float[]> vectors)
    {
        for (var i = 0; i < subjects.Count; i++)
        {
            for (var j = i + 1; j < subjects.Count; j++)
            {
                var similarity = VectorMath.CosineSimilarity(vectors[i], vectors[j]);
                if (similarity > DuplicateThreshold)
                {
                    collector.AddPair(subjects[i], subjects[j], similarity);
                }
            }
        }
    }

    private static void AddExactMatches(
        WarningCollector collector,
        IReadOnlyList<DuplicateSubject> subjects,
        IReadOnlyList<ExistingBullet> existing)
    {
        var normalizedSubjects = subjects.Select(x => Normalize(x.Text)).ToArray();

        for (var i = 0; i < subjects.Count; i++)
        {
            foreach (var bullet in existing)
            {
                if (normalizedSubjects[i] == Normalize(bullet.Text))
                {
                    collector.AddExisting(subjects[i], bullet.Id, bullet.Text, 1.0);
                }
            }

            for (var j = i + 1; j < subjects.Count; j++)
            {
                if (normalizedSubjects[i] == normalizedSubjects[j])
                {
                    collector.AddPair(subjects[i], subjects[j], 1.0);
                }
            }
        }
    }

    private static void AddLexicalMatches(
        WarningCollector collector,
        IReadOnlyList<DuplicateSubject> subjects,
        IReadOnlyList<ExistingBullet> existing)
    {
        AddLexicalExistingMatches(collector, subjects, existing);

        var subjectTokens = subjects.Select(x => Tokenize(x.Text)).ToArray();
        for (var i = 0; i < subjects.Count; i++)
        {
            for (var j = i + 1; j < subjects.Count; j++)
            {
                var similarity = JaccardSimilarity(subjectTokens[i], subjectTokens[j]);
                if (similarity > DuplicateThreshold)
                {
                    collector.AddPair(subjects[i], subjects[j], similarity);
                }
            }
        }
    }

    private static void AddLexicalExistingMatches(
        WarningCollector collector,
        IReadOnlyList<DuplicateSubject> subjects,
        IReadOnlyList<ExistingBullet> existing)
    {
        if (existing.Count == 0)
        {
            return;
        }

        var subjectTokens = subjects.Select(x => Tokenize(x.Text)).ToArray();
        var existingTokens = existing.Select(x => Tokenize(x.Text)).ToArray();

        for (var i = 0; i < subjects.Count; i++)
        {
            for (var e = 0; e < existing.Count; e++)
            {
                var similarity = JaccardSimilarity(subjectTokens[i], existingTokens[e]);
                if (similarity > DuplicateThreshold)
                {
                    collector.AddExisting(subjects[i], existing[e].Id, existing[e].Text, similarity);
                }
            }
        }
    }

    /// <summary>
    /// The existing bullets with no point in the vector index. A failed lookup reports every bullet
    /// as missing, which costs an extra text pass but never hides a duplicate.
    /// </summary>
    private async Task<IReadOnlyList<ExistingBullet>> FindUnindexedAsync(
        IReadOnlyList<ExistingBullet> existing,
        CancellationToken cancellationToken)
    {
        if (existing.Count == 0)
        {
            return [];
        }

        var indexed = await vectorStore.GetIndexedIdsAsync(existing.Select(x => x.Id).ToArray(), cancellationToken);
        return existing.Where(x => !indexed.Contains(x.Id)).ToArray();
    }

    private static string? DescribeIndexGap(int unindexedCount)
    {
        if (unindexedCount == 0)
        {
            return null;
        }

        return $"{unindexedCount} existing bullet{(unindexedCount == 1 ? " is" : "s are")} missing from the semantic "
            + "index and could only be compared by text. Use \"Index All Missing\" on the Bullets page for full "
            + "duplicate detection.";
    }

    private async Task<HashSet<(Guid, Guid)>> LoadIgnoredPairsAsync(CancellationToken cancellationToken)
    {
        var pairs = await dbContext.IgnoredBulletDuplicatePairs
            .AsNoTracking()
            .Select(x => new { x.BulletIdA, x.BulletIdB })
            .ToListAsync(cancellationToken);

        return pairs.Select(x => BulletDuplicatePair.Canonical(x.BulletIdA, x.BulletIdB)).ToHashSet();
    }


    private static double JaccardSimilarity(HashSet<string> left, HashSet<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var intersection = left.Count(right.Contains);
        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static HashSet<string> Tokenize(string text)
    {
        return Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    }

    private static string Normalize(string text)
    {
        return WhitespaceRun.Replace(NonAlphanumeric.Replace(text.ToLowerInvariant(), " "), " ").Trim();
    }

    private sealed record ExistingBullet(Guid Id, string Text);

    /// <summary>
    /// Accumulates warnings per subject, keeping the highest similarity when several passes flag
    /// the same match, and dropping pairs the user has already chosen to ignore.
    /// </summary>
    private sealed class WarningCollector(IReadOnlyList<DuplicateSubject> subjects, HashSet<(Guid, Guid)> ignoredPairs)
    {
        private readonly Dictionary<int, Dictionary<(DuplicateWarningKindDto, Guid?, int?), DuplicateWarningDto>> _warnings = [];

        public void AddExisting(DuplicateSubject subject, Guid bulletId, string matchedText, double similarity)
        {
            if (subject.BulletId is { } subjectId)
            {
                if (subjectId == bulletId || ignoredPairs.Contains(BulletDuplicatePair.Canonical(subjectId, bulletId)))
                {
                    return;
                }
            }

            Add(subject.Index, new DuplicateWarningDto(DuplicateWarningKindDto.ExistingBullet, bulletId, null, matchedText, similarity));
        }

        public void AddPair(DuplicateSubject left, DuplicateSubject right, double similarity)
        {
            if (left.BulletId is { } leftId && right.BulletId is { } rightId
                && ignoredPairs.Contains(BulletDuplicatePair.Canonical(leftId, rightId)))
            {
                return;
            }

            Add(left.Index, new DuplicateWarningDto(DuplicateWarningKindDto.BatchCandidate, null, right.Index, right.Text, similarity));
            Add(right.Index, new DuplicateWarningDto(DuplicateWarningKindDto.BatchCandidate, null, left.Index, left.Text, similarity));
        }

        public IReadOnlyDictionary<int, IReadOnlyList<DuplicateWarningDto>> Build()
        {
            return subjects.ToDictionary(
                subject => subject.Index,
                subject => _warnings.TryGetValue(subject.Index, out var found)
                    ? (IReadOnlyList<DuplicateWarningDto>)found.Values.OrderByDescending(x => x.Similarity).ToArray()
                    : []);
        }

        private void Add(int index, DuplicateWarningDto warning)
        {
            if (!_warnings.TryGetValue(index, out var forSubject))
            {
                forSubject = [];
                _warnings[index] = forSubject;
            }

            var key = (warning.Kind, warning.ExistingBulletId, warning.CandidateIndex);
            if (!forSubject.TryGetValue(key, out var current) || warning.Similarity > current.Similarity)
            {
                forSubject[key] = warning;
            }
        }
    }
}

public static class BulletDuplicatePair
{
    /// <summary>
    /// Orders a pair so the same two bullets always produce the same row, which is what makes the
    /// unique index on (BulletIdA, BulletIdB) actually prevent duplicates.
    /// </summary>
    public static (Guid A, Guid B) Canonical(Guid first, Guid second)
        => first.CompareTo(second) <= 0 ? (first, second) : (second, first);
}
