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
    DateTime ModifiedDate);

public sealed record CreateBulletRequest(string BulletText, string? SourceEmployer = null);

public sealed record UpdateBulletRequest(string BulletText, string? SourceEmployer = null);

public sealed record RewriteBulletRequest(string BulletText);

public sealed record RewriteBulletResponse(
    string RewrittenText,
    IReadOnlyList<string> Suggestions);
