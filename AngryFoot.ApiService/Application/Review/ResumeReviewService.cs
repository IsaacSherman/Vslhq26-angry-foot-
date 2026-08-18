using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Application.Evidence.Diagnostics;
using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Review;

internal interface IResumeReviewService
{
    Task<ResumeReviewReportDto> ReviewAsync(
        string resumeText,
        string? jobDescription,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reviews an uploaded resume without storing any part of it.
/// </summary>
/// <remarks>
/// Almost nothing here analyses anything itself. The bullets are read by the same parser the import
/// flow uses, judged by the same diagnostic analyzers the evidence report uses, and described with
/// the same diagnostic type - so a problem is worded identically wherever the user meets it, and a
/// new analyzer registered for one feature arrives in both.
/// </remarks>
internal sealed class ResumeReviewService(
    IEnumerable<IEvidenceDiagnosticAnalyzer> diagnosticAnalyzers,
    IResumeReviewer reviewer,
    IJobAnalyzer jobAnalyzer,
    IEvidenceCoverageAnalyzer coverageAnalyzer,
    ILogger<ResumeReviewService> logger) : IResumeReviewService
{
    public async Task<ResumeReviewReportDto> ReviewAsync(
        string resumeText,
        string? jobDescription,
        CancellationToken cancellationToken)
    {
        var bullets = Detach(ResumeBulletParser.Parse(resumeText));

        var findings = new List<CoverageDiagnosticDto>(ResumeDocumentHeuristics.Check(resumeText, bullets));
        findings.AddRange(await RunAnalyzersAsync(bullets, cancellationToken));

        var review = await reviewer.ReviewAsync(bullets, findings, cancellationToken);
        var coverage = await DescribeAgainstPostingAsync(bullets, jobDescription, cancellationToken);

        // Where a finding is shown follows what it is about, not whether it happens to cite a
        // bullet. The reviewer's spot-checks are observations about the resume and are required to
        // cite the bullets behind them, so filing them by citation would print one note about three
        // bullets three times, once under each.
        var documentFindings = findings.Where(finding => finding.BulletIds.Count == 0);
        if (review is not null)
        {
            documentFindings = documentFindings.Concat(review.SpotChecks);
        }

        return new ResumeReviewReportDto(
            review?.Summary ?? ResumeReviewNarrative.Summary(bullets.Count, findings.Count),
            [.. Ordered(documentFindings)],
            [.. bullets.Select((bullet, index) => new ResumeBulletFeedbackDto(
                index,
                bullet.BulletText,
                bullet.SourceEmployer,
                [.. Ordered(findings.Where(finding => finding.BulletIds.Contains(bullet.Id)))],
                review?.BulletSuggestions.GetValueOrDefault(index) ?? []))],
            review is null ? CoverageSourceDto.Deterministic : CoverageSourceDto.AiReviewed,
            EvidenceCoverageCopy.Disclaimer,
            coverage);
    }

    /// <summary>Warnings first, then a stable order, so a reader meets the same report twice.</summary>
    private static IEnumerable<CoverageDiagnosticDto> Ordered(IEnumerable<CoverageDiagnosticDto> findings)
        => findings
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.Code, StringComparer.Ordinal);

    /// <summary>
    /// Detached bullets: everything downstream needs one, and nothing here is persisted. The ids are
    /// fresh so diagnostics can point at a bullet within this response, and they mean nothing after it.
    /// </summary>
    private static IReadOnlyList<Bullet> Detach(IReadOnlyList<ParsedCandidate> candidates)
        => [.. candidates.Select(candidate => new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = candidate.Text,
            SourceEmployer = candidate.SuggestedEmployer
        })];

    private async Task<IReadOnlyList<CoverageDiagnosticDto>> RunAnalyzersAsync(
        IReadOnlyList<Bullet> bullets,
        CancellationToken cancellationToken)
    {
        if (bullets.Count == 0)
        {
            return [];
        }

        // No posting means no requirements, which is not a degraded mode: the analyzers that judge
        // the writing run on an empty requirement set, and the two that answer "does this match the
        // job" correctly say nothing when there is no job.
        var context = new DiagnosticContext(
            new JobAnalysisDto([], [], [], [], [], null, null),
            [],
            DiagnosticScope.Resume(bullets));

        List<CoverageDiagnosticDto> diagnostics = [];

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

    private async Task<EvidenceCoverageReportDto?> DescribeAgainstPostingAsync(
        IReadOnlyList<Bullet> bullets,
        string? jobDescription,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobDescription) || bullets.Count == 0)
        {
            return null;
        }

        var analysis = await jobAnalyzer.AnalyzeAsync(jobDescription, cancellationToken);
        return await coverageAnalyzer.DescribeResumeAsync(analysis, bullets, cancellationToken);
    }
}
