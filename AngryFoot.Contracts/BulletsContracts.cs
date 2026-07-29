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

public sealed record CreateBulletRequest(string BulletText);

public sealed record UpdateBulletRequest(string BulletText);
