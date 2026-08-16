using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence;

internal readonly record struct CoverageTotals(int Score, int EarnedWeight, int TotalWeight);

/// <summary>
/// Derives the coverage number from the per-requirement evidence, and from nothing else.
/// <para>
/// Every requirement is worth <c>Weight x <see cref="StrongPoints"/></c> points and earns points
/// according to how well it is evidenced, so the score is reproducible by hand from the table the
/// user is looking at. Points rather than fractions of a requirement because a score the payload
/// only approximately explains is the problem this feature exists to solve.
/// </para>
/// </summary>
internal static class CoverageScore
{
    private const int StrongPoints = 2;
    private const int WeakPoints = 1;
    private const int MissingPoints = 0;

    public static CoverageTotals From(IReadOnlyList<RequirementEvidence> evidence)
    {
        var totalWeight = evidence.Sum(x => x.Requirement.Weight * StrongPoints);
        var earnedWeight = evidence.Sum(x => x.Requirement.Weight * PointsFor(x.Strength));

        // A posting nothing could be extracted from is unscorable, not a zero: the summary says so
        // rather than the number implying the library failed a test that was never set.
        var score = totalWeight == 0
            ? 0
            : (int)Math.Round(100.0 * earnedWeight / totalWeight, MidpointRounding.AwayFromZero);

        return new CoverageTotals(score, earnedWeight, totalWeight);
    }

    /// <summary>What one requirement earns at this strength, before its weight is applied.</summary>
    public static int PointsFor(EvidenceStrengthDto strength) => strength switch
    {
        EvidenceStrengthDto.Strong => StrongPoints,
        EvidenceStrengthDto.Weak => WeakPoints,
        _ => MissingPoints
    };
}
