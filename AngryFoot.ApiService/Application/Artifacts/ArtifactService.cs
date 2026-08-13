using AngryFoot.ApiService.Data;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Application.Artifacts;

public enum VersionSelectionStatus
{
    Updated,
    ArtifactNotFound,
    UnknownVersionLabel
}

public sealed record VersionSelectionResult(VersionSelectionStatus Status, GenerationArtifactDto? Artifact);

public interface IArtifactService
{
    Task<IReadOnlyList<ArtifactSummaryDto>> GetSummariesAsync(CancellationToken cancellationToken);
    Task<GenerationArtifactDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Promotes stored deep-review versions to be the artifact's resume and/or cover letter, so
    /// the copy in history is the one the user actually chose.
    /// </summary>
    Task<VersionSelectionResult> SelectVersionsAsync(Guid id, SelectArtifactVersionsRequest request, CancellationToken cancellationToken);
}

public sealed class ArtifactService(AngryFootDbContext dbContext) : IArtifactService
{
    public async Task<IReadOnlyList<ArtifactSummaryDto>> GetSummariesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.GenerationArtifacts
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedDate)
            .Select(x => x.ToSummaryDto())
            .ToListAsync(cancellationToken);
    }

    public async Task<GenerationArtifactDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var artifact = await dbContext.GenerationArtifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return artifact?.ToDto();
    }

    public async Task<VersionSelectionResult> SelectVersionsAsync(Guid id, SelectArtifactVersionsRequest request, CancellationToken cancellationToken)
    {
        var artifact = await dbContext.GenerationArtifacts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (artifact is null)
        {
            return new VersionSelectionResult(VersionSelectionStatus.ArtifactNotFound, null);
        }

        var resumeRefinement = ArtifactRefinements.FromJson(artifact.ResumeRefinementJson);
        var coverLetterRefinement = ArtifactRefinements.FromJson(artifact.CoverLetterRefinementJson);

        var resume = FindVersion(resumeRefinement, request.ResumeVersionLabel);
        var coverLetter = FindVersion(coverLetterRefinement, request.CoverLetterVersionLabel);

        // All or nothing: a label that matches nothing should not leave the artifact half-updated.
        if ((resume is null && !string.IsNullOrWhiteSpace(request.ResumeVersionLabel))
            || (coverLetter is null && !string.IsNullOrWhiteSpace(request.CoverLetterVersionLabel)))
        {
            return new VersionSelectionResult(VersionSelectionStatus.UnknownVersionLabel, null);
        }

        if (resume is not null)
        {
            artifact.ResumeMarkdown = resume.Text;
            artifact.ResumeRefinementJson = ArtifactRefinements.ToJson(
                resumeRefinement! with { RecommendedLabel = resume.Label });
        }

        if (coverLetter is not null)
        {
            artifact.CoverLetterMarkdown = coverLetter.Text;
            artifact.CoverLetterRefinementJson = ArtifactRefinements.ToJson(
                coverLetterRefinement! with { RecommendedLabel = coverLetter.Label });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new VersionSelectionResult(VersionSelectionStatus.Updated, artifact.ToDto());
    }

    private static DraftVersionDto? FindVersion(RefinementDto? refinement, string? label)
    {
        return string.IsNullOrWhiteSpace(label)
            ? null
            : refinement?.Versions.FirstOrDefault(x => string.Equals(x.Label, label, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await dbContext.GenerationArtifacts
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }
}
