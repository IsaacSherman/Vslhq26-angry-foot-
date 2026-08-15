using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence.Diagnostics;

/// <summary>
/// Bullets that say the same thing twice, which spend a reader's attention without adding evidence.
/// <para>
/// Delegates to the detector the import flow already uses, so "near-duplicate" means the same thing
/// whether the pair is caught on the way in or found later in the library.
/// </para>
/// </summary>
internal sealed class DuplicateBulletAnalyzer(IBulletDuplicateDetector duplicateDetector) : IEvidenceDiagnosticAnalyzer
{
    public string Code => CoverageDiagnosticCodes.DuplicateBullet;

    public async Task<IReadOnlyList<CoverageDiagnosticDto>> AnalyzeAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken)
    {
        var bullets = context.Scope.Bullets;
        if (bullets.Count < 2)
        {
            return [];
        }

        var subjects = bullets
            .Select((bullet, index) => new DuplicateSubject(index, bullet.Id, bullet.BulletText))
            .ToArray();

        var scan = await duplicateDetector.DetectAsync(subjects, cancellationToken);

        var diagnostics = CollectPairs(scan, bullets)
            .OrderByDescending(pair => pair.Similarity)
            .Select(pair => ToDiagnostic(pair, bullets))
            .ToArray();

        var capped = DiagnosticBudget.Cap(
            diagnostics,
            remaining => $"{remaining} more pairs of bullets overlap. The closest are listed above.");

        return scan.Mode == DuplicateDetectionModeDto.Lexical && !string.IsNullOrWhiteSpace(scan.Message)
            ? [.. capped, DescribeLimitation(scan.Message)]
            : capped;
    }

    /// <summary>
    /// The detector reports a library pair twice - once as each bullet's match against the other -
    /// so pairs are canonicalised the same way the ignored-pairs table canonicalises them.
    /// </summary>
    private static IEnumerable<DuplicatePair> CollectPairs(DuplicateScanResult scan, IReadOnlyList<Bullet> bullets)
    {
        var best = new Dictionary<(Guid, Guid), double>();

        foreach (var (index, warnings) in scan.WarningsByIndex)
        {
            foreach (var warning in warnings)
            {
                var otherId = warning.ExistingBulletId
                    ?? (warning.CandidateIndex is { } candidateIndex ? bullets[candidateIndex].Id : null);

                if (otherId is not { } other)
                {
                    continue;
                }

                var key = BulletDuplicatePair.Canonical(bullets[index].Id, other);
                if (!best.TryGetValue(key, out var current) || warning.Similarity > current)
                {
                    best[key] = warning.Similarity;
                }
            }
        }

        return best.Select(entry => new DuplicatePair(entry.Key.Item1, entry.Key.Item2, entry.Value));
    }

    private CoverageDiagnosticDto ToDiagnostic(DuplicatePair pair, IReadOnlyList<Bullet> bullets)
    {
        var matched = bullets.Where(bullet => bullet.Id == pair.Left || bullet.Id == pair.Right).ToArray();

        return new CoverageDiagnosticDto(
            DiagnosticSeverityDto.Suggestion,
            Code,
            $"Two bullets describe closely overlapping work ({pair.Similarity:P0} similar). Keeping both spends space without adding evidence.",
            EvidenceMappings.AboutBullets(
                matched,
                "Near-duplicate bullets compete with each other: a reader who has read one learns nothing from the second. "
                + "Merge them into the stronger one, or narrow each to the part the other does not cover.",
                ["One combined bullet carrying the strongest detail from both, or a clear difference in scope between them."]),
            matched.Select(bullet => bullet.Id).ToArray());
    }

    private CoverageDiagnosticDto DescribeLimitation(string message)
    {
        return new CoverageDiagnosticDto(
            DiagnosticSeverityDto.Info,
            Code,
            message,
            new EvidenceRationaleDto(
                Requirement: null,
                SupportingEvidence: [],
                MissingEvidence: [],
                Reasoning: "Saying which comparison ran matters more than the result looking clean: text comparison misses "
                    + "reworded duplicates, so an empty list here is weaker evidence of no duplicates than it would otherwise be."),
            BulletIds: []);
    }

    private readonly record struct DuplicatePair(Guid Left, Guid Right, double Similarity);
}
