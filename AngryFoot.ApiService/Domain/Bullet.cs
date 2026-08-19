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
    /// Quality signals the author has disputed for this bullet. Held because some of what quality
    /// scoring asks about is not in the text: a resume elides its subject, so no wording establishes
    /// whether work was shared, and the author's answer is the only evidence there is.
    /// </summary>
    public List<string> AcknowledgedQualitySignals { get; set; } = [];

    /// <summary>
    /// Enrichment values the author added by hand. Re-running the tagger merges its answer with
    /// these rather than replacing them: the author knows what the work was, and an AI that phrases
    /// it differently this time is not a reason to lose what they wrote.
    /// </summary>
    public EnrichmentSet UserAuthored { get; set; } = EnrichmentSet.Empty();

    /// <summary>
    /// Enrichment values the author removed. Held rather than merely deleted because the tagger is
    /// deterministic enough to suggest the same wrong tag every time, and a removal that does not
    /// stick is not a removal.
    /// </summary>
    public EnrichmentSet Suppressed { get; set; } = EnrichmentSet.Empty();
}
