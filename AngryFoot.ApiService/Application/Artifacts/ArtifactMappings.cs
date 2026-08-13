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
            artifact.CreatedDate);
    }

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
            ArtifactRefinements.FromJson(artifact.ResumeRefinementJson),
            ArtifactRefinements.FromJson(artifact.CoverLetterRefinementJson));
    }
}
