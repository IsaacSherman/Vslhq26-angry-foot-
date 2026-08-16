using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence.Diagnostics;

/// <summary>
/// Bullets carrying more of the posting's requirements than something printed above them.
/// <para>
/// Silent for the library as a whole: there, order is a modified date rather than a decision, and
/// criticising it would be criticising something the user never chose.
/// </para>
/// </summary>
internal sealed class BulletOrderingAnalyzer : IEvidenceDiagnosticAnalyzer
{
    public string Code => CoverageDiagnosticCodes.BulletOrdering;

    public Task<IReadOnlyList<CoverageDiagnosticDto>> AnalyzeAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Scope.IsOrderedDocument || context.Scope.Bullets.Count < 2)
        {
            return Task.FromResult<IReadOnlyList<CoverageDiagnosticDto>>([]);
        }

        var weights = EvidenceWeightByBullet(context);
        var bullets = context.Scope.Bullets;
        var inversions = new List<Inversion>();

        var weakestAbove = weights.GetValueOrDefault(bullets[0].Id);
        var weakestAboveBullet = bullets[0];

        foreach (var bullet in bullets.Skip(1))
        {
            var weight = weights.GetValueOrDefault(bullet.Id);
            if (weight > weakestAbove)
            {
                inversions.Add(new Inversion(bullet, weakestAboveBullet, weight - weakestAbove));
            }
            else
            {
                weakestAbove = weight;
                weakestAboveBullet = bullet;
            }
        }

        var diagnostics = inversions
            .OrderByDescending(inversion => inversion.Gap)
            .Select(ToDiagnostic)
            .ToArray();

        return Task.FromResult(DiagnosticBudget.Cap(
            diagnostics,
            remaining => $"{remaining} more bullets sit below weaker ones. The largest gaps are listed above."));
    }

    /// <summary>
    /// How much of the posting each bullet carries, in the same points the coverage score is
    /// counted in, so "stronger" here means the same thing it means in the number at the top.
    /// </summary>
    private static Dictionary<Guid, int> EvidenceWeightByBullet(DiagnosticContext context)
    {
        var weights = new Dictionary<Guid, int>();

        foreach (var evidence in context.Evidence)
        {
            var points = evidence.Requirement.Weight * CoverageScore.PointsFor(evidence.Strength);
            foreach (var citation in evidence.Citations)
            {
                weights[citation.Bullet.Id] = weights.GetValueOrDefault(citation.Bullet.Id) + points;
            }
        }

        return weights;
    }

    private CoverageDiagnosticDto ToDiagnostic(Inversion inversion)
    {
        return new CoverageDiagnosticDto(
            DiagnosticSeverityDto.Suggestion,
            Code,
            $"\"{DiagnosticBudget.Excerpt(inversion.Stronger)}\" evidences more of the posting than a bullet printed above it.",
            EvidenceMappings.AboutBullets(
                [inversion.Stronger, inversion.WeakerAbove],
                "Resumes are skimmed from the top, and the first bullet under a job is the one most likely to be read in full. "
                    + "Putting the bullet that answers more of the posting first costs nothing and changes what a reader takes away.",
                ["The stronger bullet moved above the weaker one."]),
            [inversion.Stronger.Id, inversion.WeakerAbove.Id]);
    }

    private readonly record struct Inversion(Bullet Stronger, Bullet WeakerAbove, int Gap);
}
