using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Bullets;

/// <summary>
/// Scores how a bullet is written, independently of any posting.
/// <para>
/// The score is the sum of the weights of the signals it earns, and every signal ships with what the
/// check saw. Diagnostics state what is absent or present; they do not coach, encourage, or explain
/// why the author should care.
/// </para>
/// </summary>
internal static class BulletQualityScorer
{
    /// <summary>
    /// Ownership is worth a fraction of the others because it is the only one the text cannot
    /// settle. A resume elides its subject, so no wording proves authorship and the check can only
    /// spot credit being given away - a narrow thing to be worth much, and a costly thing to be
    /// wrong about.
    /// </summary>
    private const int OwnershipWeight = 5;

    /// <summary>
    /// Long enough that a reader's attention runs out before the bullet does. Advisory only - it
    /// costs no points, because a long bullet packed with evidence beats a short empty one.
    /// </summary>
    private const int LongBulletWordCount = 45;

    /// <param name="text">
    /// The wording to score, defaulting to the bullet's own. A revision is a rewording of the same
    /// accomplishment, so it is scored against the bullet's enrichment: which technologies were
    /// involved and which job families the work belongs to are facts about the work, not about the
    /// sentence.
    /// </param>
    public static BulletQualityDto Score(Bullet bullet, string? text = null)
    {
        var wording = string.IsNullOrWhiteSpace(text) ? bullet.BulletText : text;
        var declared = bullet.AcknowledgedQualitySignals;

        var opener = BulletQualityHeuristics.OpeningAction(wording);
        var figure = BulletQualityHeuristics.MeasurableImpact(wording);
        var sharedCredit = BulletQualityHeuristics.SharedCreditMarker(wording);
        var properNoun = BulletQualityHeuristics.ProperNoun(wording);
        var isEnriched = bullet.EnrichmentState == EnrichmentState.Enriched;

        BulletQualitySignalDto[] signals =
        [
            Signal(BulletQualitySignals.OpensWithAction, "Opens with an action", 20, declared,
                earned: opener is not null,
                detail: opener is not null
                    ? $"Opens with \"{opener}\"."
                    : BulletQualityHeuristics.WeakOpener(wording) is { } weak
                        ? $"Opens with \"{weak}\"."
                        : "First word is not an action verb."),

            Signal(BulletQualitySignals.MeasurableImpact, "Measurable result", 30, declared,
                earned: figure is not null,
                detail: figure is not null ? $"States \"{figure}\"." : "No figure present."),

            Signal(BulletQualitySignals.Ownership, "Sole credit", OwnershipWeight, declared,
                earned: sharedCredit is null,
                detail: sharedCredit is null
                    ? "No shared-credit wording."
                    : $"Shared credit: \"{sharedCredit}\".",
                isContestable: true),

            Signal(BulletQualitySignals.Specificity, "Names specifics", 20, declared,
                earned: properNoun is not null,
                detail: properNoun is not null ? $"Names \"{properNoun}\"." : "No named system, product, or tool."),

            Signal(BulletQualitySignals.Technology, "Names technology", 15, declared,
                earned: bullet.Technologies.Count > 0 || BulletQualityHeuristics.NamesTechnology(wording),
                detail: bullet.Technologies.Count > 0
                    ? $"Tagged: {string.Join(", ", bullet.Technologies)}."
                    : BulletQualityHeuristics.NamesTechnology(wording)
                        ? "Named in the text."
                        : isEnriched ? "None named or tagged." : "Not enriched yet."),

            Signal(BulletQualitySignals.RoleRelevance, "Maps to a role", 10, declared,
                earned: bullet.JobCategories.Count > 0,
                detail: bullet.JobCategories.Count > 0
                    ? $"Tagged: {string.Join(", ", bullet.JobCategories)}."
                    : isEnriched ? "Enrichment placed it in no job family." : "Not enriched yet.")
        ];

        return new BulletQualityDto(
            signals.Where(signal => signal.Earned).Sum(signal => signal.Weight),
            signals,
            BulletQualityHeuristics.WordCount(wording),
            BuildDiagnostics(bullet, wording, signals));
    }

    /// <summary>
    /// A signal the author has settled scores and reports as declared, so the panel never presents
    /// their word as something the wording demonstrated.
    /// </summary>
    private static BulletQualitySignalDto Signal(
        string name,
        string label,
        int weight,
        IReadOnlyList<string> declared,
        bool earned,
        string detail,
        bool isContestable = false)
    {
        var isDeclared = !earned && isContestable && declared.Contains(name, StringComparer.OrdinalIgnoreCase);

        return new BulletQualitySignalDto(
            name,
            label,
            earned || isDeclared,
            weight,
            isDeclared ? $"{detail} Settled by the author." : detail,
            isDeclared,
            isContestable);
    }

    private static IReadOnlyList<CoverageDiagnosticDto> BuildDiagnostics(
        Bullet bullet,
        string wording,
        BulletQualitySignalDto[] signals)
    {
        var diagnostics = new List<CoverageDiagnosticDto>();

        foreach (var signal in signals)
        {
            // A settled signal is not raised again. Being told twice that a check disagrees with
            // the person who did the work is what turns an assessment into an argument.
            if (signal.Earned)
            {
                continue;
            }

            if (Describe(signal) is { } message)
            {
                diagnostics.Add(Diagnostic(bullet, signal, message));
            }
        }

        var wordCount = BulletQualityHeuristics.WordCount(wording);
        if (wordCount > LongBulletWordCount)
        {
            diagnostics.Add(new CoverageDiagnosticDto(
                DiagnosticSeverityDto.Info,
                CoverageDiagnosticCodes.OverusedWording,
                $"{wordCount} words. Past about {LongBulletWordCount} a bullet stops being skimmed.",
                EvidenceMappings.AboutBullets([bullet], $"{wordCount} words.", ["Two shorter bullets, or fewer qualifying clauses."]),
                [bullet.Id]));
        }

        return diagnostics;
    }

    /// <summary>Null for signals whose absence is not worth a line of its own.</summary>
    private static string? Describe(BulletQualitySignalDto signal) => signal.Name switch
    {
        BulletQualitySignals.MeasurableImpact => "No measurable outcome stated.",
        BulletQualitySignals.OpensWithAction => $"Does not open on an action. {signal.Detail}",
        BulletQualitySignals.Ownership => $"Credit reads as shared. {signal.Detail}",
        BulletQualitySignals.Specificity => "Describes a kind of work rather than a named one.",
        _ => null
    };

    private static CoverageDiagnosticDto Diagnostic(
        Bullet bullet,
        BulletQualitySignalDto signal,
        string message)
    {
        var severity = signal.Name == BulletQualitySignals.MeasurableImpact
            ? DiagnosticSeverityDto.Suggestion
            : DiagnosticSeverityDto.Info;

        return new CoverageDiagnosticDto(
            severity,
            CodeFor(signal.Name),
            message,
            EvidenceMappings.AboutBullets([bullet], signal.Detail, [Remedy(signal.Name)]),
            [bullet.Id]);
    }

    private static string CodeFor(string signalName) => signalName switch
    {
        BulletQualitySignals.MeasurableImpact => CoverageDiagnosticCodes.NoMeasurableImpact,
        BulletQualitySignals.OpensWithAction => CoverageDiagnosticCodes.OverusedWording,
        _ => CoverageDiagnosticCodes.WeakEvidence
    };

    private static string Remedy(string signalName) => signalName switch
    {
        BulletQualitySignals.MeasurableImpact => "A figure: percentage, duration, cost, or volume.",
        BulletQualitySignals.OpensWithAction => "An action verb in first position.",
        BulletQualitySignals.Ownership => "Wording that names the author's part, or settle this signal to keep it as written.",
        _ => "The name of the system, product, or tool."
    };
}
