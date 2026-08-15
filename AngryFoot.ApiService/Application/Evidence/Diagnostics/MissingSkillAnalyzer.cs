using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence.Diagnostics;

/// <summary>
/// Requirements the library says nothing about at all. The highest-value diagnostic in the report:
/// these are the points the score did not earn, and each one names a bullet worth writing.
/// </summary>
internal sealed class MissingSkillAnalyzer : IEvidenceDiagnosticAnalyzer
{
    public string Code => CoverageDiagnosticCodes.MissingSkill;

    public Task<IReadOnlyList<CoverageDiagnosticDto>> AnalyzeAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken)
    {
        var missing = context.Evidence
            .Where(evidence => evidence.Strength == EvidenceStrengthDto.Missing)
            .OrderByDescending(evidence => evidence.Requirement.Weight)
            .ThenBy(evidence => evidence.Requirement.Term, StringComparer.OrdinalIgnoreCase)
            .Select(ToDiagnostic)
            .ToArray();

        return Task.FromResult(DiagnosticBudget.Cap(
            missing,
            remaining => $"{remaining} more of this posting's requirements have no supporting bullet. The highest-weighted are listed above."));
    }

    private CoverageDiagnosticDto ToDiagnostic(RequirementEvidence evidence)
    {
        var requirement = evidence.Requirement;

        // A stated requirement missing from the resume is a warning; a preferred one is worth
        // knowing about but is not a hole in the document.
        var severity = requirement.Kind == RequirementKindDto.Preferred
            ? DiagnosticSeverityDto.Suggestion
            : DiagnosticSeverityDto.Warning;

        return new CoverageDiagnosticDto(
            severity,
            Code,
            $"\"{requirement.Term}\" is {Describe(requirement.Kind)} in this posting, but no bullet in your library mentions it.",
            evidence.ToDto().Why,
            BulletIds: []);
    }

    private static string Describe(RequirementKindDto kind) => kind switch
    {
        RequirementKindDto.Required => "listed as a requirement",
        RequirementKindDto.Preferred => "listed as preferred",
        _ => "named among the technologies"
    };
}
