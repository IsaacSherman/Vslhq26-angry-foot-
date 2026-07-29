using AngryFoot.ApiService.Data;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Application.Generation;

internal sealed class GenerationOrchestrator(
    AngryFootDbContext dbContext,
    IJobAnalyzer jobAnalyzer,
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
        var bullets = await dbContext.Bullets.AsNoTracking().ToListAsync(cancellationToken);

        var maxBullets = request.MaxBullets.GetValueOrDefault(10);
        var ranked = rankingService.Rank(bullets, analysis, Math.Clamp(maxBullets, 1, 20));
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
}
