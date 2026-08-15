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
}
