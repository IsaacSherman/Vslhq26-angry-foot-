using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Bullets;

/// <summary>
/// What each revision mode asks for, in one place: the instruction the writer is given, the shape
/// the refinement stages are told to preserve, and what to suggest when there is no AI to ask.
/// </summary>
/// <remarks>
/// Every mode shares one rule, stated in <see cref="TruthClause"/> and repeated into each prompt:
/// a revision may change how an accomplishment is told and never what it claims. A rewrite that
/// invents a metric is worse than no rewrite, because the candidate is the one who has to defend it.
/// </remarks>
internal static class BulletRevisionModes
{
    private const string TruthClause =
        "Preserve factual truth exactly. Do not invent or inflate technologies, metrics, employers, timelines, "
        + "scope, seniority, or outcomes. If the original does not say something, the revision must not either.";

    public static string SystemPrompt(BulletRevisionModeDto mode) =>
        "You rewrite a single resume bullet. " + TruthClause + " " + Instruction(mode) + " "
        + "Return strict JSON object with fields: rewrittenText (string), suggestions (string[]), rationale (string, "
        + "one sentence on what you changed and why).";

    /// <summary>The prose contract handed to the deep-review stages so they refine toward the same target.</summary>
    public static string OutputContract(BulletRevisionModeDto mode) =>
        "a single resume bullet as plain text - one sentence or clause, no bullet marker, no markdown, no "
        + "surrounding quotes. " + Instruction(mode);

    public static string Title(BulletRevisionModeDto mode) => mode switch
    {
        BulletRevisionModeDto.Grammar => "Grammar cleanup",
        BulletRevisionModeDto.StrongerWording => "Stronger wording",
        BulletRevisionModeDto.Star => "STAR format",
        BulletRevisionModeDto.Executive => "Executive version",
        BulletRevisionModeDto.Technical => "Technical version",
        _ => "ATS version"
    };

    private static string Instruction(BulletRevisionModeDto mode) => mode switch
    {
        BulletRevisionModeDto.Grammar =>
            "Fix grammar, punctuation, tense consistency, and awkward phrasing only. Keep the wording and the claims "
            + "otherwise as close to the original as possible - this mode is for readers who like the bullet already.",

        BulletRevisionModeDto.StrongerWording =>
            "Replace weak verbs and filler with precise, active language, and put the accomplishment before the "
            + "context. Same facts, fewer and better words.",

        BulletRevisionModeDto.Star =>
            "Restructure into situation, task, action, and result, in that order, as one flowing bullet rather than "
            + "four labelled parts. Where the original does not supply one of the four, leave it out rather than "
            + "inventing it.",

        BulletRevisionModeDto.Executive =>
            "Lead with business outcome, scope, and ownership. Drop implementation detail a non-engineer would skip. "
            + "Keep any figure the original gives, and never scale one up.",

        BulletRevisionModeDto.Technical =>
            "Foreground the systems, techniques, and technologies, and the engineering decision behind the work. "
            + "Name only technologies the original names.",

        _ =>
            "Write plainly for automated keyword screening: standard industry terms rather than internal names, no "
            + "abbreviations the original does not use, no special characters, no clever phrasing. Spell out any "
            + "technology the original abbreviates, and keep the abbreviation alongside it where natural."
    };

    /// <summary>
    /// What to tell the user when there is no AI to write the revision. Each mode says what it would
    /// have done, so the feature still teaches something rather than silently doing nothing.
    /// </summary>
    public static IReadOnlyList<string> FallbackSuggestions(BulletRevisionModeDto mode, string bulletText) => mode switch
    {
        BulletRevisionModeDto.Grammar =>
            ["Check tense consistency and punctuation; the automatic pass only capitalised the opening and closed the sentence."],

        BulletRevisionModeDto.StrongerWording => BulletQualityHeuristics.WeakOpener(bulletText) is { } opener
            ? [$"Replace the opening \"{opener}\" with a verb naming what you did."]
            : ["Look for filler between the verb and the outcome - the shortest path from action to result usually reads strongest."],

        BulletRevisionModeDto.Star =>
            ["Name the situation this addressed and the result it produced; STAR needs both, and this bullet supplies at most one."],

        BulletRevisionModeDto.Executive => BulletQualityHeuristics.HasMeasurableImpact(bulletText)
            ? ["Lead with the figure already here - an executive reader wants the outcome before the method."]
            : ["Add the business outcome: what it saved, earned, unblocked, or prevented."],

        BulletRevisionModeDto.Technical => BulletQualityHeuristics.NamesTechnology(bulletText)
            ? ["Say what the engineering decision was, not only which technology was used."]
            : ["Name the systems and techniques involved; this bullet describes the work without them."],

        _ =>
            ["Spell out abbreviations and use the industry-standard name for each technology, since keyword screens match on exact terms."]
    };
}
