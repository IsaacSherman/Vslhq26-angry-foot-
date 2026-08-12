namespace AngryFoot.Contracts;

/// <summary>
/// One requirement that an occupational dataset associates with an occupation, plus the
/// weight it carries in that occupation's benchmark.
/// </summary>
public sealed record BenchmarkItemDto(
    string Name,
    string Kind,
    int Importance);

/// <summary>
/// How the user's bullet library compares against aggregate labor-market data for the
/// occupation their target job title maps to.
/// <para>
/// This is an occupation-level comparison drawn from a published government dataset. It is
/// never a comparison against a specific person, a specific employer, or that employer's
/// actual employees, and no individual-level data of any kind is involved.
/// </para>
/// </summary>
/// <param name="MatchConfidence">"Exact", "Fuzzy", or "None" when the title mapped to no occupation.</param>
/// <param name="MatchedOn">The occupational title the user's job title was matched against, for transparency.</param>
/// <param name="CoveredCount">Total requirements evidenced, which may exceed the number listed in <paramref name="Covered"/>.</param>
/// <param name="RequirementCount">Total requirements in the occupation's profile.</param>
public sealed record OccupationBenchmarkDto(
    string? SocCode,
    string? OccupationTitle,
    string MatchConfidence,
    string? MatchedOn,
    int CoverageScore,
    string Summary,
    int CoveredCount,
    int RequirementCount,
    IReadOnlyList<BenchmarkItemDto> Covered,
    IReadOnlyList<BenchmarkItemDto> Missing,
    string SourceAttribution);
