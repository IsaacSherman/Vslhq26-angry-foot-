namespace AngryFoot.ApiService.Domain;

/// <summary>
/// A pair of bullets the user has declared distinct despite scoring above the near-duplicate
/// threshold, so the warning stops reappearing. Ids are stored in canonical order
/// (<see cref="BulletIdA"/> &lt;= <see cref="BulletIdB"/>) — the unique index over the two columns
/// only dedupes if callers normalize first.
/// </summary>
public sealed class IgnoredBulletDuplicatePair
{
    public Guid Id { get; set; }
    public Guid BulletIdA { get; set; }
    public Guid BulletIdB { get; set; }
    public double Similarity { get; set; }
    public DateTime CreatedDate { get; set; }
    public string? Note { get; set; }
}
