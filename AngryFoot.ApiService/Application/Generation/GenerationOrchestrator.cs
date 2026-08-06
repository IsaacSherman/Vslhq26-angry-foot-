using AngryFoot.ApiService.Data;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Application.Generation;

internal sealed class GenerationOrchestrator(
    AngryFootDbContext dbContext,
    IJobAnalyzer jobAnalyzer,
    BulletRetrievalService retrievalService,
    BulletRankingService rankingService,
    BulletRewriteService rewriteService,
    ResumeMarkdownService resumeService,
    CoverLetterService coverLetterService) : IGenerationOrchestrator
{
    public async Task<GenerationResultDto> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.JobDescription))
        {
            throw new ArgumentException("Job description is required.", nameof(request));
        }

        var profile = await dbContext.Profiles
            .Include(x => x.WorkHistory)
            .Include(x => x.Education)
            .Include(x => x.Certifications)
            .FirstOrDefaultAsync(cancellationToken)
            ?? Domain.Profile.CreateEmpty();

        if (profile.Id == Guid.Empty || !await dbContext.Profiles.AnyAsync(x => x.Id == profile.Id, cancellationToken))
        {
            if (profile.Id == Guid.Empty)
            {
                profile.Id = Guid.NewGuid();
            }

            dbContext.Profiles.Add(profile);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var analysis = await jobAnalyzer.AnalyzeAsync(request.JobDescription, cancellationToken);

        var maxBullets = Math.Clamp(request.MaxBullets.GetValueOrDefault(10), 1, 20);
        var ranked = await RetrieveRankedBulletsAsync(request.JobDescription, analysis, maxBullets, cancellationToken);
        var rewritten = await rewriteService.RewriteAsync(analysis, ranked, cancellationToken);

        var resumeMarkdown = resumeService.BuildResume(profile, analysis, rewritten);
        var coverLetterMarkdown = await coverLetterService.BuildCoverLetterAsync(
            profile,
            new CoverLetterContext(request.JobTitle, request.Company, analysis, rewritten),
            cancellationToken);

        var artifact = new GenerationArtifact
        {
            Id = Guid.NewGuid(),
            JobTitle = request.JobTitle?.Trim(),
            Company = request.Company?.Trim(),
            JobDescription = request.JobDescription.Trim(),
            ResumeMarkdown = resumeMarkdown,
            CoverLetterMarkdown = coverLetterMarkdown,
            SelectedBulletIds = rewritten.Select(x => x.Bullet.Id).ToList(),
            JobAnalysisJson = Ai.AiJsonUtilities.ToJson(analysis),
            CreatedDate = DateTime.UtcNow
        };

        dbContext.GenerationArtifacts.Add(artifact);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new GenerationResultDto(
            artifact.Id,
            artifact.ResumeMarkdown,
            artifact.CoverLetterMarkdown,
            analysis,
            artifact.SelectedBulletIds);
    }

    /// <summary>
    /// Prefers semantic retrieval (only the matched bullets are loaded from the database) and
    /// falls back to the deterministic keyword-overlap ranking over the full bullet library when
    /// retrieval is unavailable or configured but returns nothing.
    /// </summary>
    private async Task<IReadOnlyList<RankedBullet>> RetrieveRankedBulletsAsync(
        string jobDescription, JobAnalysisDto analysis, int maxBullets, CancellationToken cancellationToken)
    {
        if (retrievalService.IsAvailable)
        {
            var matches = await retrievalService.SearchAsync(jobDescription, analysis, maxBullets, cancellationToken);
            if (matches.Count > 0)
            {
                var matchedIds = matches.Select(x => x.BulletId).ToArray();
                var bulletsById = await dbContext.Bullets
                    .AsNoTracking()
                    .Where(x => matchedIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, cancellationToken);

                var retrieved = matches
                    .Where(x => bulletsById.ContainsKey(x.BulletId))
                    .Select(x => new RankedBullet(bulletsById[x.BulletId], ScoreToRankingPoints(x.Score)))
                    .ToArray();

                if (retrieved.Length > 0)
                {
                    return retrieved;
                }
            }
        }

        var bullets = await dbContext.Bullets.AsNoTracking().ToListAsync(cancellationToken);
        return rankingService.Rank(bullets, analysis, maxBullets);
    }

    // Qdrant cosine similarity is in [-1, 1]; scale onto roughly the same order of magnitude as
    // BulletRankingService's integer scores so both paths behave the same downstream.
    private static int ScoreToRankingPoints(float similarity) => (int)Math.Round(similarity * 100, MidpointRounding.AwayFromZero);
}
