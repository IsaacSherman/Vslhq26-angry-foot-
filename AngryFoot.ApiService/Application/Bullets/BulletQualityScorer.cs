using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Bullets;

/// <summary>
/// Scores how a bullet is written, independently of any posting.
/// <para>
/// The score is the sum of the weights of the signals it earns, and the signals ship on the DTO
/// alongside it - the same rule evidence coverage follows, for the same reason: a number the user
/// cannot take apart is a number they have to take on trust.
/// </para>
/// </summary>
internal static class BulletQualityScorer
{
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

        BulletQualitySignalDto[] signals =
        [
            new(BulletQualitySignals.OpensWithAction, "Opens with an action",
                BulletQualityHeuristics.OpensWithAction(wording), 20),
            new(BulletQualitySignals.MeasurableImpact, "Measurable result",
                BulletQualityHeuristics.HasMeasurableImpact(wording), 25),
            new(BulletQualitySignals.Ownership, "Clear ownership",
                BulletQualityHeuristics.ClaimsOwnership(wording), 15),
            new(BulletQualitySignals.Specificity, "Names specifics",
                BulletQualityHeuristics.IsSpecific(wording), 15),
            new(BulletQualitySignals.Technology, "Names technology",
                bullet.Technologies.Count > 0 || BulletQualityHeuristics.NamesTechnology(wording), 15),
            new(BulletQualitySignals.RoleRelevance, "Maps to a role",
                bullet.JobCategories.Count > 0, 10)
        ];

        return new BulletQualityDto(
            signals.Where(signal => signal.Earned).Sum(signal => signal.Weight),
            signals,
            BulletQualityHeuristics.WordCount(wording),
            BuildDiagnostics(bullet, wording, signals));
    }

    private static bool Earned(BulletQualitySignalDto[] signals, string name)
        => signals.First(signal => signal.Name == name).Earned;

    private static IReadOnlyList<CoverageDiagnosticDto> BuildDiagnostics(
        Bullet bullet,
        string wording,
        BulletQualitySignalDto[] signals)
    {
        var diagnostics = new List<CoverageDiagnosticDto>();

        if (!Earned(signals, BulletQualitySignals.MeasurableImpact))
        {
            diagnostics.Add(Diagnostic(
                bullet,
                DiagnosticSeverityDto.Suggestion,
                CoverageDiagnosticCodes.NoMeasurableImpact,
                "This bullet says what you did but not what changed because of it.",
                "A figure is also what lifts a requirement from mentioned to evidenced when a posting asks for it.",
                "A percentage, a duration, a cost, a volume - whatever the work actually moved."));
        }

        if (!Earned(signals, BulletQualitySignals.OpensWithAction))
        {
            var opener = BulletQualityHeuristics.WeakOpener(wording);
            diagnostics.Add(Diagnostic(
                bullet,
                DiagnosticSeverityDto.Suggestion,
                CoverageDiagnosticCodes.OverusedWording,
                opener is null
                    ? "This bullet does not open on an action."
                    : $"This bullet opens with \"{opener}\", which describes an assignment rather than an achievement.",
                "The opening words are the ones most likely to be read.",
                "A verb in first position naming what you did."));
        }

        if (!Earned(signals, BulletQualitySignals.Ownership))
        {
            diagnostics.Add(Diagnostic(
                bullet,
                DiagnosticSeverityDto.Info,
                CoverageDiagnosticCodes.WeakEvidence,
                "This bullet does not make clear which part was yours.",
                "A reader who cannot tell what you did as opposed to what happened around you has to guess, "
                    + "and guesses go against the candidate.",
                "Wording that names your role in it - what you led, built, or decided."));
        }

        if (!Earned(signals, BulletQualitySignals.Specificity))
        {
            diagnostics.Add(Diagnostic(
                bullet,
                DiagnosticSeverityDto.Info,
                CoverageDiagnosticCodes.WeakEvidence,
                "This bullet describes a kind of work rather than a particular piece of it.",
                "Named systems, products, and tools are what make a bullet checkable, and they are also the words "
                    + "a posting's requirements are matched against.",
                "The name of the system, product, or tool involved."));
        }

        if (BulletQualityHeuristics.WordCount(wording) > LongBulletWordCount)
        {
            diagnostics.Add(Diagnostic(
                bullet,
                DiagnosticSeverityDto.Info,
                CoverageDiagnosticCodes.OverusedWording,
                $"This bullet runs to {BulletQualityHeuristics.WordCount(wording)} words.",
                "Past roughly forty words a bullet stops being skimmable, and the evidence in it competes with itself. "
                    + "Splitting it usually produces two bullets that each earn their place.",
                "Two shorter bullets, or the same claim with the qualifying clauses removed."));
        }

        return diagnostics;
    }

    private static CoverageDiagnosticDto Diagnostic(
        Bullet bullet,
        DiagnosticSeverityDto severity,
        string code,
        string message,
        string reasoning,
        string missingEvidence)
    {
        return new CoverageDiagnosticDto(
            severity,
            code,
            message,
            EvidenceMappings.AboutBullets([bullet], reasoning, [missingEvidence]),
            [bullet.Id]);
    }
}
