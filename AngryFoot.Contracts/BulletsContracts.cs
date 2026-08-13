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
    bool IsIndexed);

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
public sealed record RewriteBulletResponse(
    string RewrittenText,
    IReadOnlyList<string> Suggestions,
    RefinementDto? Refinement = null);

public sealed record IndexMissingBulletsResponse(int IndexedCount);
