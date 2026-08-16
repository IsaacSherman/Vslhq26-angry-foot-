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
    /// it prints them. Deterministic: a generation already chains several AI calls, and this is
    /// reporting on a decision that has already been made rather than making one.
    /// </summary>
    Task<EvidenceCoverageReportDto> DescribeResumeAsync(
        JobAnalysisDto analysis,
        IReadOnlyList<Bullet> orderedBullets,
        CancellationToken cancellationToken);
}

internal sealed class EvidenceCoverageService(
    AngryFootDbContext dbContext,
    IEvidenceReviewer reviewer,
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
        var baseline = EvidenceCoverageEngine.Evaluate(requirements, bullets);

        var review = bullets.Count > 0 && requirements.Count > 0
            ? await reviewer.ReviewAsync(
                jobDescription,
                analysis,
                baseline,
                bullets,
                await LoadProfessionalSummaryAsync(cancellationToken),
                cancellationToken)
            : null;

        var extraDiagnostics = review?.Diagnostics ?? [];
        if (review is null && bullets.Count > 0)
        {
            extraDiagnostics = [.. extraDiagnostics, DescribeMissingReview()];
        }

        return await BuildAsync(
            analysis,
            review?.Evidence ?? baseline,
            DiagnosticScope.Library(bullets),
            review is null ? CoverageSourceDto.Deterministic : CoverageSourceDto.AiReviewed,
            review?.Summary,
            extraDiagnostics,
            cancellationToken);
    }

    public Task<EvidenceCoverageReportDto> DescribeResumeAsync(
        JobAnalysisDto analysis,
        IReadOnlyList<Bullet> orderedBullets,
        CancellationToken cancellationToken)
    {
        var requirements = RequirementSet.From(analysis);

        return BuildAsync(
            analysis,
            EvidenceCoverageEngine.Evaluate(requirements, orderedBullets),
            DiagnosticScope.Resume(orderedBullets),
            CoverageSourceDto.Deterministic,
            aiSummary: null,
            extraDiagnostics: [],
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

    private static CoverageDiagnosticDto DescribeMissingReview()
    {
        return new CoverageDiagnosticDto(
            DiagnosticSeverityDto.Info,
            CoverageDiagnosticCodes.AnalysisLimitation,
            "This report was produced by word matching alone, without an AI review.",
            new EvidenceRationaleDto(
                Requirement: null,
                SupportingEvidence: [],
                MissingEvidence: [],
                Reasoning: "Word matching cannot tell that a bullet about \"container orchestration\" evidences a requirement "
                    + "for Kubernetes, so a requirement marked missing here may be evidenced in words the matcher did not "
                    + "recognise. Read the missing list as \"not stated in these words\" rather than as \"not done\"."),
            BulletIds: []);
    }
}
