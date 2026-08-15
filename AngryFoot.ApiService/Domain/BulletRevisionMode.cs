namespace AngryFoot.ApiService.Domain;

/// <summary>
/// What a revision was written for. Not a quality ladder - a posting that screens through an ATS
/// wants different wording from one a hiring manager reads, and the candidate keeps both.
/// </summary>
public enum BulletRevisionMode
{
    /// <summary>Fix the writing, change nothing about what is claimed.</summary>
    Grammar,

    /// <summary>Sharper verbs and tighter phrasing, same facts.</summary>
    StrongerWording,

    /// <summary>Situation, task, action, result - the shape interviewers are trained to ask for.</summary>
    Star,

    /// <summary>Scope, ownership, and business outcome, for readers who skim for those.</summary>
    Executive,

    /// <summary>Systems, techniques, and technologies, for readers who screen on them.</summary>
    Technical,

    /// <summary>Plain wording carrying the posting's own terms, for keyword screens.</summary>
    Ats
}
