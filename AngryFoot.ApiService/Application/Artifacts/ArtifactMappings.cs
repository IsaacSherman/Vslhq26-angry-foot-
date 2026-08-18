using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Artifacts;

public static class ArtifactMappings
{
    public static ArtifactSummaryDto ToSummaryDto(this GenerationArtifact artifact)
    {
        return new ArtifactSummaryDto(
            artifact.Id,
            artifact.JobTitle,
            artifact.Company,
            artifact.CreatedDate,
            artifact.IsGeneric,
            ReadAudience(artifact.Audience));
    }

    /// <summary>
    /// Null rather than a throw for a name no longer in the enum: a stored artifact is a record of
    /// what happened, and losing the whole row because its audience was renamed would be a poor
    /// trade for a label.
    /// </summary>
    private static ResumeAudienceDto? ReadAudience(string? stored)
        => Enum.TryParse<ResumeAudienceDto>(stored, out var audience) ? audience : null;

    public static GenerationArtifactDto ToDto(this GenerationArtifact artifact)
    {
        return new GenerationArtifactDto(
            artifact.Id,
            artifact.JobTitle,
            artifact.Company,
            artifact.JobDescription,
            artifact.ResumeMarkdown,
            artifact.CoverLetterMarkdown,
            artifact.SelectedBulletIds,
            artifact.JobAnalysisJson,
            artifact.CreatedDate,
            ArtifactJsonColumns.FromJson<RefinementDto>(artifact.ResumeRefinementJson),
            ArtifactJsonColumns.FromJson<RefinementDto>(artifact.CoverLetterRefinementJson),
            ArtifactJsonColumns.FromJson<EvidenceCoverageReportDto>(artifact.EvidenceCoverageJson),
            ArtifactJsonColumns.FromJson<GenerationExplanationDto>(artifact.GenerationExplanationJson),
            artifact.IsGeneric,
            ReadAudience(artifact.Audience));
    }
}
