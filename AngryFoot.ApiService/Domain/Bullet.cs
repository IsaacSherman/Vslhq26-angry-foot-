namespace AngryFoot.ApiService.Domain;

public sealed class Bullet
{
    public Guid Id { get; set; }
    public string BulletText { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public List<string> Skills { get; set; } = [];
    public List<string> Technologies { get; set; } = [];
    public List<string> JobCategories { get; set; } = [];
    public List<string> Impact { get; set; } = [];
    public EnrichmentState EnrichmentState { get; set; } = EnrichmentState.Pending;
    public string? SourceEmployer { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }

    /// <summary>
    /// Alternative wordings of this accomplishment, one per revision mode and version. They never
    /// overwrite <see cref="BulletText"/>; see <see cref="BulletRevision"/>.
    /// </summary>
    public List<BulletRevision> Revisions { get; set; } = [];

    /// <summary>
    /// Quality signals the author has settled for this bullet. Held because some of what quality
    /// scoring asks about is not in the text: a resume elides its subject, so no wording settles
    /// whether work was shared, and the author's answer is the only evidence there is.
    /// </summary>
    public List<string> AcknowledgedQualitySignals { get; set; } = [];
}
