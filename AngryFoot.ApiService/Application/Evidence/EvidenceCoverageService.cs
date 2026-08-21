using AngryFoot.ApiService.Application.Evidence.Diagnostics;
using AngryFoot.ApiService.Data;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Application.Evidence;

public interface IEvidenceCoverageAnalyzer
{
    /// <summary>
    /// How much of a posting the whole bullet library evidences, reviewed by AI when one is
    /// configured.
    /// </summary>
    Task<EvidenceCoverageReportDto> AnalyzeLibraryAsync(
        string jobDescription,
        JobAnalysisDto analysis,
        CancellationToken cancellationToken);

    /// <summary>
    /// How much of a posting one generated resume evidences, over the bullets it used in the order
    /// it prints them. No AI review: a generation already chains several AI calls, and this is
    /// reporting on a decision that has already been made rather than making one.
    /// </summary>
    /// <param name="semantic">
    /// Embedding matches computed by the caller. The generation path passes the same index it gives
    /// the build-log explanation, so the two can never disagree about which requirements the resume
    /// leaves unevidenced. Null asks this to compute its own.
    /// </param>
    Task<EvidenceCoverageReportDto> DescribeResumeAsync(
        JobAnalysisDto analysis,
        IReadOnlyList<Bullet> orderedBullets,
        SemanticEvidenceIndex? semantic,
        CancellationToken cancellationToken);
}

internal sealed class EvidenceCoverageService(
    AngryFootDbContext dbContext,
    IEvidenceReviewer reviewer,
    ISemanticEvidenceMatcher semanticMatcher,
    IEnumerable<IEvidenceDiagnosticAnalyzer> diagnosticAnalyzers,
    ILogger<EvidenceCoverageService> logger) : IEvidenceCoverageAnalyzer
{
    public async Task<EvidenceCoverageReportDto> AnalyzeLibraryAsync(
        string jobDescription,
        JobAnalysisDto analysis,
        CancellationToken cancellationToken)
    {
        var bullets = await dbContext.Bullets.AsNoTracking().ToListAsync(cancellationToken);
        var requirements = RequirementSet.From(analysis);
        var semantic = await semanticMatcher.MatchAsync(requirements, bullets, cancellationToken);
        var baseline = EvidenceCoverageEngine.Evaluate(requirements, bullets, semantic);

        var review = bullets.Count > 0 && requirements.Count > 0
            ? await reviewer.ReviewAsync(
                jobDescription,
                analysis,
                baseline,
                bullets,
                await LoadProfessionalSummaryAsync(cancellationToken),
                semantic,
                cancellationToken)
            : null;

        var extraDiagnostics = review?.Diagnostics ?? [];
        if (review is null && bullets.Count > 0)
        {
            extraDiagnostics = [.. extraDiagnostics, DescribeMissingReview(semantic)];
        }

        return await BuildAsync(
            analysis,
            review?.Evidence ?? baseline,
            DiagnosticScope.Library(bullets),
            review is null ? CoverageSourceDto.Deterministic : CoverageSourceDto.AiReviewed,
            review?.Summary,
            WithSemanticNote(semantic, extraDiagnostics),
            cancellationToken);
    }

    public async Task<EvidenceCoverageReportDto> DescribeResumeAsync(
        JobAnalysisDto analysis,
        IReadOnlyList<Bullet> orderedBullets,
        SemanticEvidenceIndex? semantic,
        CancellationToken cancellationToken)
    {
        var requirements = RequirementSet.From(analysis);
        semantic ??= await semanticMatcher.MatchAsync(requirements, orderedBullets, cancellationToken);

        return await BuildAsync(
            analysis,
            EvidenceCoverageEngine.Evaluate(requirements, orderedBullets, semantic),
            DiagnosticScope.Resume(orderedBullets),
            CoverageSourceDto.Deterministic,
            aiSummary: null,
            extraDiagnostics: WithSemanticNote(semantic, []),
            cancellationToken);
    }

    private async Task<EvidenceCoverageReportDto> BuildAsync(
        JobAnalysisDto analysis,
        IReadOnlyList<RequirementEvidence> evidence,
        DiagnosticScope scope,
        CoverageSourceDto source,
        string? aiSummary,
        IReadOnlyList<CoverageDiagnosticDto> extraDiagnostics,
        CancellationToken cancellationToken)
    {
        var totals = CoverageScore.From(evidence);
        var strongCount = evidence.Count(x => x.Strength == EvidenceStrengthDto.Strong);
        var weakCount = evidence.Count(x => x.Strength == EvidenceStrengthDto.Weak);
        var missingCount = evidence.Count(x => x.Strength == EvidenceStrengthDto.Missing);

        var diagnostics = await RunAnalyzersAsync(
            new DiagnosticContext(analysis, evidence, scope),
            cancellationToken);

        return new EvidenceCoverageReportDto(
            totals.Score,
            totals.EarnedWeight,
            totals.TotalWeight,
            aiSummary ?? EvidenceNarrative.Summary(totals, strongCount, weakCount, missingCount, scope.Bullets.Count > 0),
            strongCount,
            weakCount,
            missingCount,
            evidence
                .OrderBy(x => x.Strength)
                .ThenByDescending(x => x.Requirement.Weight)
                .ThenBy(x => x.Requirement.Term, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.ToDto())
                .ToArray(),
            diagnostics.Concat(extraDiagnostics)
                .OrderByDescending(x => x.Severity)
                .ThenBy(x => x.Code, StringComparer.Ordinal)
                .ToArray(),
            source,
            EvidenceCoverageCopy.Disclaimer);
    }

    /// <summary>
    /// Runs every analyzer, keeping whatever the others found when one fails. A diagnostic is
    /// advice about a resume; losing the whole report because one kind of advice threw would trade
    /// a small gap for a total outage.
    /// </summary>
    private async Task<IReadOnlyList<CoverageDiagnosticDto>> RunAnalyzersAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<CoverageDiagnosticDto>();

        foreach (var analyzer in diagnosticAnalyzers)
        {
            try
            {
                diagnostics.AddRange(await analyzer.AnalyzeAsync(context, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Diagnostic analyzer {Code} failed and was skipped.", analyzer.Code);
            }
        }

        return diagnostics;
    }

    private async Task<string?> LoadProfessionalSummaryAsync(CancellationToken cancellationToken)
    {
        var profile = await dbContext.Profiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return profile?.ProfessionalSummary;
    }

    /// <summary>
    /// What this report could not do. The wording turns on whether embeddings ran, because "word
    /// matching alone" stops being true once a paraphrase can be counted - and a stated limitation
    /// that overstates itself misleads exactly as much as one that understates itself.
    /// </summary>
    private static CoverageDiagnosticDto DescribeMissingReview(SemanticEvidenceIndex? semantic)
    {
        var matchedByMeaning = semantic is { IsEmpty: false };

        return new CoverageDiagnosticDto(
            DiagnosticSeverityDto.Info,
            CoverageDiagnosticCodes.AnalysisLimitation,
            matchedByMeaning
                ? "This report was produced by word matching and embeddings, without an AI review."
                : "This report was produced by word matching alone, without an AI review.",
            new EvidenceRationaleDto(
                Requirement: null,
                SupportingEvidence: [],
                MissingEvidence: [],
                Reasoning: matchedByMeaning
                    ? "Embeddings catch a bullet about \"container orchestration\" evidencing a requirement for Kubernetes, "
                        + "but only where the two read as close. A requirement marked missing here may still be evidenced in "
                        + "words neither pass recognised, so read the missing list as \"not stated in these words\" rather "
                        + "than as \"not done\"."
                    : "Word matching cannot tell that a bullet about \"container orchestration\" evidences a requirement "
                        + "for Kubernetes, so a requirement marked missing here may be evidenced in words the matcher did not "
                        + "recognise. Read the missing list as \"not stated in these words\" rather than as \"not done\"."),
            BulletIds: []);
    }

    /// <summary>
    /// Says on the report itself that some evidence was found by meaning rather than by wording. The
    /// point of the feature is that a reader can tell the two apart, which needs saying once at the
    /// top as well as per citation.
    /// </summary>
    private static IReadOnlyList<CoverageDiagnosticDto> WithSemanticNote(
        SemanticEvidenceIndex? semantic,
        IReadOnlyList<CoverageDiagnosticDto> existing)
    {
        if (semantic is null or { IsEmpty: true })
        {
            return existing;
        }

        return [.. existing, new CoverageDiagnosticDto(
            DiagnosticSeverityDto.Info,
            CoverageDiagnosticCodes.SemanticMatching,
            "Some requirements were matched to bullets by meaning rather than by wording.",
            new EvidenceRationaleDto(
                Requirement: null,
                SupportingEvidence: [],
                MissingEvidence: [],
                Reasoning: "Where a bullet does not use a requirement's words, the two were compared by embedding and "
                    + $"counted when they scored at least {SemanticEvidenceMatcher.MinimumConfidence:0.00} out of 1. Those "
                    + "citations carry their score and can never count as full evidence, because full evidence means the "
                    + "resume states the requirement."),
            BulletIds: [])];
    }
}
