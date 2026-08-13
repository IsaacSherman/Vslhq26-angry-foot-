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
}
