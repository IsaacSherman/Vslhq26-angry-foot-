namespace AngryFoot.ApiService.Domain;

/// <summary>
/// One variant of a bullet, written in one revision mode.
/// <para>
/// A bullet owns many revisions and they sit alongside it rather than replacing it: the bullet's own
/// <see cref="Bullet.BulletText"/> stays the single source of truth for what the candidate did, and
/// promoting a revision into it is an explicit act. A rewrite is a suggestion until the person who
/// did the work says otherwise.
/// </para>
/// </summary>
public sealed class BulletRevision
{
    public Guid Id { get; set; }
    public Guid BulletId { get; set; }
    public Bullet? Bullet { get; set; }
    public BulletRevisionMode Mode { get; set; }
    public string RevisedText { get; set; } = string.Empty;

    /// <summary>
    /// The bullet text this was written from. Snapshotted because the canonical bullet can be
    /// edited afterwards, and a revision of wording that no longer exists has to be able to say so
    /// rather than quietly pass itself off as current.
    /// </summary>
    public string SourceText { get; set; } = string.Empty;

    /// <summary>Monotonic within a (bullet, mode) pair, so "the ATS version" has a history.</summary>
    public int Version { get; set; }

    /// <summary>Why this revision differs from its source, when the writer offered a reason.</summary>
    public string? Rationale { get; set; }

    /// <summary>False when the revision came from the deterministic fallback rather than the AI.</summary>
    public bool IsAiGenerated { get; set; }

    public DateTime CreatedDate { get; set; }
}
