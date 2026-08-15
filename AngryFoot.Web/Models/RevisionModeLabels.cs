using AngryFoot.Contracts;

namespace AngryFoot.Web.Models;

/// <summary>
/// How each revision mode is named and described to the user. Separate from the API's own mode
/// instructions on purpose: those are written for a model, these are written for a person.
/// </summary>
public static class RevisionModeLabels
{
    public static string Describe(BulletRevisionModeDto mode) => mode switch
    {
        BulletRevisionModeDto.Grammar => "Grammar cleanup",
        BulletRevisionModeDto.StrongerWording => "Stronger wording",
        BulletRevisionModeDto.Star => "STAR format",
        BulletRevisionModeDto.Executive => "Executive",
        BulletRevisionModeDto.Technical => "Technical",
        _ => "ATS"
    };

    public static string Explain(BulletRevisionModeDto mode) => mode switch
    {
        BulletRevisionModeDto.Grammar =>
            "Fixes grammar, tense, and punctuation, and changes nothing else. For a bullet you already like.",
        BulletRevisionModeDto.StrongerWording =>
            "Same facts, sharper verbs, less filler, and the accomplishment before the context.",
        BulletRevisionModeDto.Star =>
            "Situation, task, action, result - the shape interviewers are trained to ask for. Parts your bullet does not supply are left out rather than invented.",
        BulletRevisionModeDto.Executive =>
            "Leads with outcome, scope, and ownership, and drops the implementation detail a non-engineer skips.",
        BulletRevisionModeDto.Technical =>
            "Foregrounds the systems, techniques, and the engineering decision behind the work.",
        _ =>
            "Plain wording and standard industry terms for keyword screens: no abbreviations your bullet does not use, no clever phrasing."
    };
}
