using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Application.Evidence.Diagnostics;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.Tests.Fakes;

/// <summary>
/// Stands in for the AI evidence review. Defaults to declining, which is what a service test that
/// is not specifically about the review should see - and what the product does with no AI
/// configured.
/// </summary>
/// <remarks>
/// Hand-written rather than mocked because <see cref="IEvidenceReviewer"/> is internal, and Moq's
/// proxy generator cannot reach it without opening the assembly's internals to the dynamic proxy
/// assembly. Same reason as <see cref="FakeRefinementPipeline"/>.
/// </remarks>
internal sealed class FakeEvidenceReviewer(Func<IReadOnlyList<RequirementEvidence>, EvidenceReview?> factory)
    : IEvidenceReviewer
{
    public FakeEvidenceReviewer(EvidenceReview? result = null)
        : this(_ => result)
    {
    }

    /// <summary>Thrown instead of answering, for the failure and cancellation paths.</summary>
    public Exception? Throws { get; init; }

    public int CallCount { get; private set; }

    public IReadOnlyList<Bullet> LastBulletsSent { get; private set; } = [];

    public string? LastProfessionalSummary { get; private set; }

    public SemanticEvidenceIndex? LastSemanticIndex { get; private set; }

    public Task<EvidenceReview?> ReviewAsync(
        string jobDescription,
        JobAnalysisDto analysis,
        IReadOnlyList<RequirementEvidence> baseline,
        IReadOnlyList<Bullet> bullets,
        string? professionalSummary,
        SemanticEvidenceIndex? semantic,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastBulletsSent = bullets;
        LastProfessionalSummary = professionalSummary;
        LastSemanticIndex = semantic;

        return Throws is null
            ? Task.FromResult(factory(baseline))
            : Task.FromException<EvidenceReview?>(Throws);
    }
}

/// <summary>
/// A diagnostic analyzer that only ever throws, for proving one broken analyzer cannot take the
/// whole report down with it. Hand-written for the same reason as <see cref="FakeEvidenceReviewer"/>.
/// </summary>
internal sealed class ThrowingDiagnosticAnalyzer(Exception? exception = null) : IEvidenceDiagnosticAnalyzer
{
    private readonly Exception _exception = exception ?? new InvalidOperationException("boom");

    public string Code => "throwing";

    public Task<IReadOnlyList<CoverageDiagnosticDto>> AnalyzeAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken)
        => Task.FromException<IReadOnlyList<CoverageDiagnosticDto>>(_exception);
}
