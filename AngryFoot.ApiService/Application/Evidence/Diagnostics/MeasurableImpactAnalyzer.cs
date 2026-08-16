using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence.Diagnostics;

/// <summary>
/// Bullets that describe activity without saying what came of it. This is the same test the
/// evidence strength rule applies, surfaced per bullet: a bullet that gains a number can lift
/// every requirement it touches from weak to strong.
/// </summary>
internal sealed class MeasurableImpactAnalyzer : IEvidenceDiagnosticAnalyzer
{
    public string Code => CoverageDiagnosticCodes.NoMeasurableImpact;

    public Task<IReadOnlyList<CoverageDiagnosticDto>> AnalyzeAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken)
    {
        var citedBulletIds = context.Evidence
            .SelectMany(evidence => evidence.Citations)
            .Select(citation => citation.Bullet.Id)
            .ToHashSet();

        var unquantified = context.Scope.Bullets
            .Where(bullet => !BulletQualityHeuristics.HasMeasurableImpact(bullet.BulletText))
            // A bullet already cited as evidence is where a number pays off twice, so those first.
            .OrderByDescending(bullet => citedBulletIds.Contains(bullet.Id))
            .Select(bullet => ToDiagnostic(bullet, citedBulletIds.Contains(bullet.Id)))
            .ToArray();

        return Task.FromResult(DiagnosticBudget.Cap(
            unquantified,
            remaining => $"{remaining} more bullets describe work without a measurable result."));
    }

    private CoverageDiagnosticDto ToDiagnostic(Bullet bullet, bool isCited)
    {
        var reasoning = isCited
            ? "This bullet is already carrying evidence for one of the posting's requirements, but only at half credit. "
                + "A number is what moves it to full."
            : "A reader cannot tell the difference between work that mattered and work that happened. "
                + "A figure - percentage, time, cost, volume - is what makes the difference visible.";

        return new CoverageDiagnosticDto(
            DiagnosticSeverityDto.Suggestion,
            Code,
            $"\"{DiagnosticBudget.Excerpt(bullet)}\" says what you did but not what changed because of it.",
            EvidenceMappings.AboutBullets(
                [bullet],
                reasoning,
                ["A figure showing the outcome: how much faster, how much cheaper, how many users, how much less downtime."]),
            [bullet.Id]);
    }
}
