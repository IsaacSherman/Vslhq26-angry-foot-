namespace AngryFoot.ApiService.Domain;

public sealed class GenerationArtifact
{
    public Guid Id { get; set; }
    public string? JobTitle { get; set; }
    public string? Company { get; set; }
    public string JobDescription { get; set; } = string.Empty;
    public string ResumeMarkdown { get; set; } = string.Empty;
    public string CoverLetterMarkdown { get; set; } = string.Empty;
    public List<Guid> SelectedBulletIds { get; set; } = [];
    public string JobAnalysisJson { get; set; } = "{}";
    public DateTime CreatedDate { get; set; }

    /// <summary>
    /// Serialized <c>RefinementDto</c> of deep-review resume versions, or null when the
    /// generation did not run deep review. <see cref="ResumeMarkdown"/> holds whichever version
    /// is currently selected.
    /// </summary>
    public string? ResumeRefinementJson { get; set; }

    /// <summary>Same, for the cover letter.</summary>
    public string? CoverLetterRefinementJson { get; set; }

    /// <summary>
    /// Serialized <c>EvidenceCoverageReportDto</c> for the bullets this resume used, as of the
    /// moment it was generated. Frozen rather than recomputed on read: the library moves on, and a
    /// report that silently re-scored itself against later bullets would no longer explain the
    /// resume sitting next to it. Null for artifacts generated before the report existed.
    /// </summary>
    public string? EvidenceCoverageJson { get; set; }

    /// <summary>
    /// Serialized <c>GenerationExplanationDto</c>: what became of every bullet the generator
    /// considered. Frozen for the same reason as <see cref="EvidenceCoverageJson"/> - it explains
    /// the decisions taken on the day, and the candidate set has moved on since.
    /// </summary>
    public string? GenerationExplanationJson { get; set; }

    /// <summary>
    /// Generated with no posting to aim at. <see cref="JobDescription"/> and
    /// <see cref="CoverLetterMarkdown"/> are both empty for these, and <see cref="JobTitle"/> is
    /// the title the candidate was aiming at rather than one a posting advertised.
    /// </summary>
    public bool IsGeneric { get; set; }

    /// <summary>
    /// The <c>ResumeAudienceDto</c> the generic resume was written for, by name. Null for tailored
    /// generations. Stored as a string so a reordered enum cannot silently relabel old rows.
    /// </summary>
    public string? Audience { get; set; }
}
