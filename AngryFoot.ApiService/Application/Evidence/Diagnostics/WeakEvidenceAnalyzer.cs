using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence.Diagnostics;

/// <summary>
/// Requirements the library mentions but never demonstrates. These are the cheapest points on the
/// board - the bullet already exists and only needs an outcome - which is why they are called out
/// separately from the ones with no bullet at all.
/// </summary>
internal sealed class WeakEvidenceAnalyzer : IEvidenceDiagnosticAnalyzer
{
    public string Code => CoverageDiagnosticCodes.WeakEvidence;

    public Task<IReadOnlyList<CoverageDiagnosticDto>> AnalyzeAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken)
    {
        var weak = context.Evidence
            .Where(evidence => evidence.Strength == EvidenceStrengthDto.Weak)
            .OrderByDescending(evidence => evidence.Requirement.Weight)
            .ThenBy(evidence => evidence.Requirement.Term, StringComparer.OrdinalIgnoreCase)
            .Select(ToDiagnostic)
            .ToArray();

        return Task.FromResult(DiagnosticBudget.Cap(
            weak,
            remaining => $"{remaining} more requirements are mentioned without a result. The highest-weighted are listed above."));
    }

    private CoverageDiagnosticDto ToDiagnostic(RequirementEvidence evidence)
    {
        return new CoverageDiagnosticDto(
            DiagnosticSeverityDto.Suggestion,
            Code,
            $"\"{evidence.Requirement.Term}\" appears in your bullets but is never shown to have produced anything. It earns half credit.",
            evidence.ToDto().Why,
            evidence.Citations.Select(citation => citation.Bullet.Id).ToArray());
    }
}
