namespace AngryFoot.Contracts;

/// <summary>Which part of the posting a requirement came from, and therefore what it weighs.</summary>
public enum RequirementKindDto
{
    Required,
    Preferred,
    Technology
}

/// <summary>
/// How well the bullet library evidences one requirement. Ordered weakest first, so sorting by it
/// puts the requirements needing work at the top.
/// </summary>
public enum EvidenceStrengthDto
{
    Missing,
    Weak,
    Strong
}

/// <summary>
/// Ordered least to most urgent, so <c>OrderByDescending(x =&gt; x.Severity)</c> puts warnings on top.
/// </summary>
public enum DiagnosticSeverityDto
{
    Info,
    Suggestion,
    Warning
}

/// <summary>Whether the report is the deterministic engine's alone, or was reviewed by AI.</summary>
public enum CoverageSourceDto
{
    Deterministic,
    AiReviewed
}

/// <summary>One bullet offered as evidence for one requirement.</summary>
/// <param name="MatchedTerm">The requirement term this bullet was matched on, verbatim.</param>
/// <param name="IsExactTermMatch">
/// True when <paramref name="MatchedTerm"/> literally appears in the bullet's text, skills, or
/// technologies. False marks a citation the AI added by meaning rather than by words - shown to the
/// user as such, and never enough on its own to raise a requirement to
/// <see cref="EvidenceStrengthDto.Strong"/>.
/// </param>
/// <param name="Because">Why this bullet counts, in one clause.</param>
public sealed record EvidenceCitationDto(
    Guid BulletId,
    string BulletText,
    string MatchedTerm,
    bool IsExactTermMatch,
    string Because);

/// <summary>
/// The reasoning behind any single recommendation: the job requirement at stake, the bullets that
/// support it, what evidence is absent, and the reasoning connecting them.
/// <para>
/// One type, carried by every requirement row and every diagnostic, so no feature grows a parallel
/// explanation of its own.
/// </para>
/// </summary>
/// <param name="Requirement">The requirement this is about, or null when it concerns the resume as a whole.</param>
/// <param name="MissingEvidence">What the library would need to say for this to be fully evidenced.</param>
public sealed record EvidenceRationaleDto(
    string? Requirement,
    IReadOnlyList<EvidenceCitationDto> SupportingEvidence,
    IReadOnlyList<string> MissingEvidence,
    string Reasoning);

/// <summary>
/// One extracted requirement and the bullets linked to it as evidence. The evidence lives in
/// <paramref name="Why"/> rather than beside it, so a supporting bullet is listed in exactly one
/// place.
/// </summary>
public sealed record RequirementCoverageDto(
    string Requirement,
    RequirementKindDto Kind,
    int Weight,
    EvidenceStrengthDto Strength,
    EvidenceRationaleDto Why);

/// <param name="Code">A well-known value from <see cref="CoverageDiagnosticCodes"/>.</param>
/// <param name="BulletIds">The bullets this is about; empty when it concerns the library as a whole.</param>
public sealed record CoverageDiagnosticDto(
    DiagnosticSeverityDto Severity,
    string Code,
    string Message,
    EvidenceRationaleDto Why,
    IReadOnlyList<Guid> BulletIds);

/// <summary>
/// How much of one posting's stated requirements the written bullets actually evidence.
/// <para>
/// <paramref name="CoverageScore"/> is derived entirely from <paramref name="Requirements"/> - it is
/// <c>round(100 * EarnedWeight / TotalWeight)</c> - so every point is traceable to a row the user
/// can read. It is never an AI's opinion of a number; the AI may only adjust the per-requirement
/// strengths that feed it.
/// </para>
/// </summary>
/// <param name="EarnedWeight">
/// Points earned: each requirement is worth <c>Weight x 2</c> and earns <c>Weight x</c> 2 for
/// Strong evidence, 1 for Weak, 0 for Missing. Doubling keeps half credit for weak evidence
/// without fractions, so the score above is exactly reproducible from these two integers rather
/// than approximately.
/// </param>
/// <param name="TotalWeight">Points available: the sum of <c>Weight x 2</c> over every requirement.</param>
/// <param name="Disclaimer">
/// <see cref="EvidenceCoverageCopy.Disclaimer"/>, carried on the payload so every consumer of the
/// score - including ones written later - has the framing in hand rather than having to know it.
/// </param>
public sealed record EvidenceCoverageReportDto(
    int CoverageScore,
    int EarnedWeight,
    int TotalWeight,
    string Summary,
    int StrongCount,
    int WeakCount,
    int MissingCount,
    IReadOnlyList<RequirementCoverageDto> Requirements,
    IReadOnlyList<CoverageDiagnosticDto> Diagnostics,
    CoverageSourceDto Source,
    string Disclaimer);

/// <summary>Well-known <see cref="CoverageDiagnosticDto.Code"/> values.</summary>
public static class CoverageDiagnosticCodes
{
    public const string MissingSkill = "missing-skill";
    public const string WeakEvidence = "weak-evidence";
    public const string DuplicateBullet = "duplicate-bullet";
    public const string BulletOrdering = "bullet-ordering";
    public const string OverusedWording = "overused-wording";
    public const string NoMeasurableImpact = "no-measurable-impact";
    public const string UnsupportedClaim = "unsupported-claim";
    public const string AnalysisLimitation = "analysis-limitation";
}

/// <summary>
/// The framing that must accompany every rendering of the coverage number, held in one place so the
/// API, the web UI, and any consumer added later say the same thing (issue #18).
/// </summary>
public static class EvidenceCoverageCopy
{
    public const string Disclaimer =
        "Evidence coverage measures how much of this posting's stated requirements your written "
        + "bullets actually evidence. It is a measure of what your resume says, not of your ability, "
        + "your experience, or your worth as a candidate.";
}
