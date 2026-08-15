namespace AngryFoot.Contracts;

public enum EnrichmentStateDto
{
    Pending,
    Enriched,
    Failed
}

public sealed record BulletDto(
    Guid Id,
    string BulletText,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Technologies,
    IReadOnlyList<string> JobCategories,
    IReadOnlyList<string> Impact,
    string? SourceEmployer,
    EnrichmentStateDto EnrichmentState,
    DateTime CreatedDate,
    DateTime ModifiedDate,
    bool IsIndexed,
    BulletQualityDto? Quality = null);

public sealed record CreateBulletRequest(string BulletText, string? SourceEmployer = null);

public sealed record UpdateBulletRequest(string BulletText, string? SourceEmployer = null);

/// <param name="DeepReview">
/// Opt in to the critique-and-revise pass: three extra AI calls that produce alternative versions
/// of the rewrite for the user to choose between. Ignored when the rewrite falls back to heuristics.
/// </param>
public sealed record RewriteBulletRequest(string BulletText, bool DeepReview = false);

/// <param name="Refinement">
/// The deep-review versions, or null when deep review was not requested or not applicable.
/// <see cref="RewriteBulletResponse.RewrittenText"/> always carries the recommended version.
/// </param>
/// <param name="Rationale">
/// One sentence on what the writer changed and why. Null when the rewrite came from the heuristic
/// fallback, which has nothing to explain beyond its suggestions.
/// </param>
public sealed record RewriteBulletResponse(
    string RewrittenText,
    IReadOnlyList<string> Suggestions,
    RefinementDto? Refinement = null,
    string? Rationale = null);

public sealed record IndexMissingBulletsResponse(int IndexedCount);

/// <summary>
/// Phase one of a guided deep review: the draft and the reviewer's verdict on it, handed back so
/// the user can resolve any ambiguity the reviewer stumbled over before the later stages run.
/// Every field round-trips into <see cref="CompleteBulletRewriteRequest"/>, which keeps the pause
/// entirely client-side - the server holds no half-finished rewrite.
/// </summary>
public sealed record BulletRewriteCritiqueResponse(
    string OriginalText,
    string Draft,
    string Critique,
    string? Alternative,
    IReadOnlyList<string> Suggestions);

/// <param name="Guidance">
/// What the user wants the remaining agents to know. Treated as fact, and as outranking the
/// critique. Null or blank simply runs the rest of the pass unguided.
/// </param>
public sealed record CompleteBulletRewriteRequest(
    string OriginalText,
    string Draft,
    string Critique,
    string? Alternative,
    IReadOnlyList<string> Suggestions,
    string? Guidance);

/// <summary>What a revision was written for. Mirrors <c>BulletRevisionMode</c>.</summary>
public enum BulletRevisionModeDto
{
    Grammar,
    StrongerWording,
    Star,
    Executive,
    Technical,
    Ats
}

/// <summary>
/// One stored variant of a bullet. The bullet's own text is never replaced by one of these; see
/// <c>BulletRevision</c>.
/// </summary>
/// <param name="SourceText">The bullet wording this was written from.</param>
/// <param name="IsStale">
/// True when the bullet has been edited since, so <paramref name="RevisedText"/> is a rewrite of
/// wording that no longer exists.
/// </param>
public sealed record BulletRevisionDto(
    Guid Id,
    Guid BulletId,
    BulletRevisionModeDto Mode,
    string RevisedText,
    string SourceText,
    int Version,
    string? Rationale,
    bool IsAiGenerated,
    bool IsStale,
    DateTime CreatedDate,
    BulletQualityDto? Quality = null);

/// <summary>
/// What can be said about how a bullet is written, without reference to any particular posting.
/// </summary>
/// <param name="Score">
/// 0-100, derived from the signals below so it is explainable from them rather than asserted
/// alongside them.
/// </param>
/// <param name="Diagnostics">
/// Reuses the evidence report's diagnostic type, so a bullet's problems are described in the same
/// words and carry the same "Why" panel wherever they appear.
/// </param>
/// <param name="Name">A well-known value from <see cref="BulletQualitySignals"/>.</param>
/// <param name="Label">How to name this signal to the user.</param>
/// <param name="Weight">What it contributes to the score when earned.</param>
public sealed record BulletQualitySignalDto(string Name, string Label, bool Earned, int Weight);

/// <summary>
/// What can be said about how a bullet is written, without reference to any particular posting.
/// </summary>
/// <param name="Score">
/// The sum of the weights of the earned <paramref name="Signals"/>, and nothing else - so it can
/// be taken apart rather than trusted, the same way the evidence coverage score can.
/// </param>
/// <param name="Diagnostics">
/// Reuses the evidence report's diagnostic type, so a bullet's problems are described in the same
/// words and carry the same "Why" panel wherever they appear.
/// </param>
public sealed record BulletQualityDto(
    int Score,
    IReadOnlyList<BulletQualitySignalDto> Signals,
    int WordCount,
    IReadOnlyList<CoverageDiagnosticDto> Diagnostics);

/// <summary>Well-known <see cref="BulletQualitySignalDto.Name"/> values.</summary>
public static class BulletQualitySignals
{
    public const string OpensWithAction = "opens-with-action";
    public const string MeasurableImpact = "measurable-impact";
    public const string Ownership = "ownership";
    public const string Specificity = "specificity";
    public const string Technology = "technology";

    /// <summary>
    /// Whether enrichment could place this accomplishment in any job family at all. Deliberately
    /// not a judgement about a particular posting - that is evidence coverage's question, and
    /// answering it twice in two ways would give the user two numbers to reconcile.
    /// </summary>
    public const string RoleRelevance = "role-relevance";
}

/// <param name="Guidance">
/// The candidate's own clarification of anything ambiguous in the bullet, treated as fact by the
/// writer.
/// </param>
public sealed record CreateBulletRevisionRequest(
    BulletRevisionModeDto Mode,
    bool DeepReview = false,
    string? Guidance = null);

/// <summary>
/// Replaces the bullet's canonical text with a revision's, recording the text being replaced as a
/// new revision so nothing is lost.
/// </summary>
public sealed record PromoteBulletRevisionResponse(
    BulletDto Bullet,
    IReadOnlyList<BulletRevisionDto> Revisions);
