using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence.Diagnostics;

/// <summary>
/// Wording that repeats until it stops carrying information, and openers that describe a job
/// description rather than an accomplishment.
/// </summary>
internal sealed class OverusedWordingAnalyzer : IEvidenceDiagnosticAnalyzer
{
    /// <summary>
    /// Twice is a coincidence. Three times is a habit the reader starts skimming past.
    /// </summary>
    private const int RepetitionThreshold = 3;

    public string Code => CoverageDiagnosticCodes.OverusedWording;

    public Task<IReadOnlyList<CoverageDiagnosticDto>> AnalyzeAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken)
    {
        var diagnostics = RepeatedWords(context)
            .Concat(WeakOpeners(context))
            .ToArray();

        return Task.FromResult(DiagnosticBudget.Cap(
            diagnostics,
            remaining => $"{remaining} more wording notes were held back."));
    }

    private IEnumerable<CoverageDiagnosticDto> RepeatedWords(DiagnosticContext context)
    {
        var bullets = context.Scope.Bullets;

        // A posting's own vocabulary is not filler: a library where "Kubernetes" appears in five
        // bullets is evidencing Kubernetes five times, which is the point.
        var exempt = context.Evidence
            .SelectMany(evidence => BulletQualityHeuristics.ContentTokens(evidence.Requirement.Term))
            .Concat(bullets.SelectMany(bullet => bullet.Technologies)
                .SelectMany(BulletQualityHeuristics.ContentTokens))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var bulletsByWord = new Dictionary<string, List<Bullet>>(StringComparer.Ordinal);
        foreach (var bullet in bullets)
        {
            foreach (var token in BulletQualityHeuristics.ContentTokens(bullet.BulletText))
            {
                if (exempt.Contains(token))
                {
                    continue;
                }

                if (!bulletsByWord.TryGetValue(token, out var users))
                {
                    users = [];
                    bulletsByWord[token] = users;
                }

                users.Add(bullet);
            }
        }

        return bulletsByWord
            .Where(entry => entry.Value.Count >= RepetitionThreshold)
            .OrderByDescending(entry => entry.Value.Count)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new CoverageDiagnosticDto(
                DiagnosticSeverityDto.Suggestion,
                Code,
                $"\"{entry.Key}\" appears in {entry.Value.Count} bullets. Repeated wording flattens the differences between them.",
                EvidenceMappings.AboutBullets(
                    entry.Value,
                    "A word a reader has already seen three times stops registering, and the bullets after the first start "
                        + "to read as the same accomplishment. Varying the verb is usually enough to separate them again.",
                    [$"A different verb in all but one of these bullets, chosen to say what was actually distinct about each."]),
                entry.Value.Select(bullet => bullet.Id).ToArray()));
    }

    private IEnumerable<CoverageDiagnosticDto> WeakOpeners(DiagnosticContext context)
    {
        return context.Scope.Bullets
            .Select(bullet => (Bullet: bullet, Opener: BulletQualityHeuristics.WeakOpener(bullet.BulletText)))
            .Where(candidate => candidate.Opener is not null)
            .Select(candidate => new CoverageDiagnosticDto(
                DiagnosticSeverityDto.Suggestion,
                Code,
                $"\"{DiagnosticBudget.Excerpt(candidate.Bullet)}\" opens on an assignment rather than an achievement.",
                EvidenceMappings.AboutBullets(
                    [candidate.Bullet],
                    $"Opening with \"{candidate.Opener}\" spends the words most likely to be read on what you were asked "
                        + "to do, leaving what you actually did to compete for attention further along the line.",
                    ["An action verb in the first position, naming what you did rather than what you were assigned."]),
                [candidate.Bullet.Id]));
    }
}
