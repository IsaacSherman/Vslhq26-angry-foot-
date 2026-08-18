using AngryFoot.ApiService.Application.Artifacts;
using AngryFoot.ApiService.Application.Evidence;
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
    GenericBulletRankingService genericRankingService,
    TargetTitleRelevanceService titleRelevanceService,
    BulletRewriteService rewriteService,
    ResumeMarkdownService resumeService,
    CoverLetterService coverLetterService,
    IEvidenceCoverageAnalyzer coverageAnalyzer) : IGenerationOrchestrator
{
    private const float MinimumSemanticSimilarity = 0.35f;

    /// <summary>
    /// How many bullets past the cut to retrieve purely so the explanation can account for them.
    /// Bounded rather than proportional: the near-misses are what a reader learns from, and a list
    /// of every bullet that lost is a list nobody reads.
    /// </summary>
    private const int MaxRunnersUpExplained = 10;

    public async Task<GenerationResultDto> GenerateAsync(GenerationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.JobDescription))
        {
            throw new ArgumentException("Job description is required.", nameof(request));
        }

        var profile = await LoadOrCreateProfileAsync(cancellationToken);

        var analysis = await jobAnalyzer.AnalyzeAsync(request.JobDescription, cancellationToken);

        var maxBullets = ClampMaxBullets(request.MaxBullets);

        // Deep review is allowed to swap a weak bullet out for a better one, so it needs
        // candidates the ranker left on the bench.
        var benchSize = request.DeepReview ? maxBullets : 0;

        // And the explanation needs runners-up whatever the mode: asking retrieval for exactly the
        // number of bullets the resume will hold means nothing is ever left off, so "why isn't this
        // bullet here" has no answer - including the answer worth having, that a bullet just below
        // the cut was the only one evidencing something the resume now misses.
        var ranked = await RetrieveRankedBulletsAsync(
            request.JobDescription, analysis, maxBullets + benchSize + MaxRunnersUpExplained, cancellationToken);
        var selected = ranked.Take(maxBullets).ToArray();
        var bench = ranked.Skip(maxBullets).Take(benchSize).ToArray();

        var guidance = TrimToNull(request.Guidance);
        var rewriteOutcome = await rewriteService.RewriteAsync(
            RewriteTarget.ForPosting(analysis), selected, bench, guidance, request.DeepReview, cancellationToken);
        var rewritten = rewriteOutcome.Recommended;

        var resumeMarkdown = resumeService.BuildResume(profile, analysis, rewritten);
        var resumeRefinement = BuildResumeRefinement(profile, analysis, rewriteOutcome);

        var coverLetter = await coverLetterService.BuildCoverLetterAsync(
            profile,
            new CoverLetterContext(request.JobTitle, request.Company, analysis, rewritten),
            guidance,
            request.DeepReview,
            cancellationToken);

        // Reported over the bullets as the resume orders them, not as the ranker scored them, so
        // an ordering note is about the document the user is holding.
        var coverage = await coverageAnalyzer.DescribeResumeAsync(
            analysis,
            rewritten.Select(x => x.Bullet).ToArray(),
            cancellationToken);

        // Every candidate the ranker produced, not only the ones that made it: an account that
        // covered the selected bullets alone would be the flattering half of the story.
        var explanation = GenerationExplanationService.Explain(analysis, ranked, rewritten);

        var artifact = new GenerationArtifact
        {
            Id = Guid.NewGuid(),
            JobTitle = request.JobTitle?.Trim(),
            Company = request.Company?.Trim(),
            JobDescription = request.JobDescription.Trim(),
            ResumeMarkdown = resumeMarkdown,
            CoverLetterMarkdown = coverLetter.Markdown,
            SelectedBulletIds = rewritten.Select(x => x.Bullet.Id).ToList(),
            JobAnalysisJson = Ai.AiJsonUtilities.ToJson(analysis),
            CreatedDate = DateTime.UtcNow,
            ResumeRefinementJson = ArtifactJsonColumns.ToJson(resumeRefinement),
            CoverLetterRefinementJson = ArtifactJsonColumns.ToJson(coverLetter.Refinement),
            EvidenceCoverageJson = ArtifactJsonColumns.ToJson(coverage),
            GenerationExplanationJson = ArtifactJsonColumns.ToJson(explanation)
        };

        dbContext.GenerationArtifacts.Add(artifact);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new GenerationResultDto(
            artifact.Id,
            artifact.ResumeMarkdown,
            artifact.CoverLetterMarkdown,
            analysis,
            artifact.SelectedBulletIds,
            resumeRefinement,
            coverLetter.Refinement,
            coverage,
            explanation);
    }

    public async Task<GenerationResultDto> GenerateGenericAsync(
        GenericGenerationRequest request, CancellationToken cancellationToken)
    {
        var profile = await LoadOrCreateProfileAsync(cancellationToken);

        // No posting means no analysis to extract, so the ranker reads the whole library rather
        // than retrieving against a description that does not exist.
        var analysis = JobAnalysis.Neutral;
        var maxBullets = ClampMaxBullets(request.MaxBullets);

        // Verbatim never rewrites, so deep review has nothing to critique and the bench it would
        // have swapped from is dead weight.
        var isVerbatim = request.Audience == ResumeAudienceDto.Verbatim;
        var deepReview = request.DeepReview && !isVerbatim;
        var benchSize = deepReview ? maxBullets : 0;

        var targetTitle = TrimToNull(request.TargetTitle);
        var selection = await SelectGenericBulletsAsync(
            profile, targetTitle, maxBullets + benchSize + MaxRunnersUpExplained, cancellationToken);

        var ranked = selection.Ranked;
        var selected = ranked.Take(maxBullets).ToArray();
        var bench = ranked.Skip(maxBullets).Take(benchSize).ToArray();

        var guidance = TrimToNull(request.Guidance);
        var rewriteOutcome = isVerbatim
            ? BulletRewriteOutcome.WithoutRefinement(
                selected.Select(x => new RewrittenBullet(x.Bullet, x.Bullet.BulletText)).ToArray())
            : await rewriteService.RewriteAsync(
                RewriteTarget.ForAudience(targetTitle, request.Audience),
                selected,
                bench,
                guidance,
                deepReview,
                cancellationToken);
        var rewritten = rewriteOutcome.Recommended;

        var resumeMarkdown = resumeService.BuildResume(profile, analysis, rewritten);
        var resumeRefinement = BuildResumeRefinement(profile, analysis, rewriteOutcome);
        var explanation = GenerationExplanationService.ExplainGeneric(
            ranked, rewritten, request.Audience, selection.TitleSummary);

        var artifact = new GenerationArtifact
        {
            Id = Guid.NewGuid(),
            JobTitle = targetTitle,
            Company = null,
            JobDescription = string.Empty,
            ResumeMarkdown = resumeMarkdown,
            // No posting, no role, no company: a letter built from those would be three blanks and
            // a paragraph the user has to delete.
            CoverLetterMarkdown = string.Empty,
            SelectedBulletIds = rewritten.Select(x => x.Bullet.Id).ToList(),
            JobAnalysisJson = Ai.AiJsonUtilities.ToJson(analysis),
            CreatedDate = DateTime.UtcNow,
            ResumeRefinementJson = ArtifactJsonColumns.ToJson(resumeRefinement),
            // Coverage measures a library against a posting's requirements. With no requirements
            // extracted, a report would score 0% and read as a failure rather than as a question
            // that was never asked.
            EvidenceCoverageJson = null,
            GenerationExplanationJson = ArtifactJsonColumns.ToJson(explanation),
            IsGeneric = true,
            Audience = request.Audience.ToString()
        };

        dbContext.GenerationArtifacts.Add(artifact);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new GenerationResultDto(
            artifact.Id,
            artifact.ResumeMarkdown,
            artifact.CoverLetterMarkdown,
            analysis,
            artifact.SelectedBulletIds,
            resumeRefinement,
            CoverLetterRefinement: null,
            Coverage: null,
            explanation);
    }

    public async Task<GenericPreviewDto> PreviewGenericAsync(
        GenericGenerationRequest request, CancellationToken cancellationToken)
    {
        var profile = await LoadOrCreateProfileAsync(cancellationToken);
        var maxBullets = ClampMaxBullets(request.MaxBullets);

        var selection = await SelectGenericBulletsAsync(
            profile, TrimToNull(request.TargetTitle), maxBullets + MaxRunnersUpExplained, cancellationToken);

        // A preview shows the candidate's own wording, because that is what selection actually
        // produced. Reporting it as the resume's final text would be a guess at what an AI has not
        // been asked to write yet.
        var selected = selection.Ranked
            .Take(maxBullets)
            .Select(x => new RewrittenBullet(x.Bullet, x.Bullet.BulletText))
            .ToArray();

        return new GenericPreviewDto(
            selected.Select(x => x.Bullet.Id).ToArray(),
            GenerationExplanationService.ExplainGeneric(
                selection.Ranked, selected, request.Audience, selection.TitleSummary));
    }

    private sealed record GenericSelection(IReadOnlyList<RankedBullet> Ranked, string TitleSummary);

    /// <summary>
    /// The whole deterministic half of a generic generation: score the library against the target
    /// title, weigh it for strength, breadth, and recency, and order it. Shared by the preview and
    /// the generation so the preview cannot drift from what a generation would actually pick.
    /// </summary>
    private async Task<GenericSelection> SelectGenericBulletsAsync(
        Domain.Profile profile, string? targetTitle, int take, CancellationToken cancellationToken)
    {
        var bullets = await dbContext.Bullets.AsNoTracking().ToListAsync(cancellationToken);
        var titleRelevance = await titleRelevanceService.BuildAsync(targetTitle, bullets, cancellationToken);
        var context = new GenericRankingContext(titleRelevance, EmployerRecency.From(profile.WorkHistory));

        return new GenericSelection(
            genericRankingService.Rank(bullets, take, context),
            titleRelevance.Summary);
    }

    private async Task<Domain.Profile> LoadOrCreateProfileAsync(CancellationToken cancellationToken)
    {
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

        return profile;
    }

    private static int ClampMaxBullets(int? requested) => Math.Clamp(requested.GetValueOrDefault(10), 1, 20);

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Turns the deep-review versions of the bullet rewrites into versions of the whole resume.
    /// Rendering markdown is deterministic and free, so the user compares finished documents
    /// instead of the JSON the agents actually exchanged.
    /// </summary>
    private RefinementDto? BuildResumeRefinement(
        Domain.Profile profile, JobAnalysisDto analysis, BulletRewriteOutcome outcome)
    {
        if (outcome.Refinement is null)
        {
            return null;
        }

        var versions = outcome.Refinement.Versions
            .Where(version => outcome.VersionBullets.ContainsKey(version.Label))
            .Select(version => version with
            {
                Text = resumeService.BuildResume(profile, analysis, outcome.VersionBullets[version.Label])
            })
            .ToArray();

        return outcome.Refinement with { Versions = versions };
    }

    /// <summary>
    /// Prefers strong semantic matches, then fills any remaining slots with the deterministic
    /// keyword-overlap ranker so retrieval cannot hide otherwise useful bullets.
    /// </summary>
    private async Task<IReadOnlyList<RankedBullet>> RetrieveRankedBulletsAsync(
        string jobDescription, JobAnalysisDto analysis, int maxBullets, CancellationToken cancellationToken)
    {
        var retrieved = Array.Empty<RankedBullet>();

        if (retrievalService.IsAvailable)
        {
            var matches = await retrievalService.SearchAsync(jobDescription, analysis, maxBullets, cancellationToken);
            if (matches.Count > 0)
            {
                var strongMatches = matches
                    .Where(x => x.Score >= MinimumSemanticSimilarity)
                    .ToArray();
                var matchedIds = strongMatches.Select(x => x.BulletId).ToArray();
                var bulletsById = await dbContext.Bullets
                    .AsNoTracking()
                    .Where(x => matchedIds.Contains(x.Id))
                    .ToDictionaryAsync(x => x.Id, cancellationToken);

                retrieved = strongMatches
                    .Where(x => bulletsById.ContainsKey(x.BulletId))
                    .Select(x => new RankedBullet(bulletsById[x.BulletId], ScoreToRankingPoints(x.Score)))
                    .ToArray();

                if (retrieved.Length >= maxBullets)
                {
                    return retrieved;
                }
            }
        }

        var selectedIds = retrieved.Select(x => x.Bullet.Id).ToHashSet();
        var remainingSlots = maxBullets - retrieved.Length;
        var bullets = await dbContext.Bullets
            .AsNoTracking()
            .Where(x => !selectedIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var keywordRanked = rankingService.Rank(bullets, analysis, remainingSlots);

        return retrieved.Concat(keywordRanked).Take(maxBullets).ToArray();
    }

    // Qdrant cosine similarity is in [-1, 1]; scale onto roughly the same order of magnitude as
    // BulletRankingService's integer scores so both paths behave the same downstream.
    private static int ScoreToRankingPoints(float similarity) => (int)Math.Round(similarity * 100, MidpointRounding.AwayFromZero);
}
