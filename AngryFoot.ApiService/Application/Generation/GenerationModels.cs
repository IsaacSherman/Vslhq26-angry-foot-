using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Generation;

/// <summary>What a ranking reason was about, so the explanation can sort credits from costs.</summary>
internal enum RankingReasonKind
{
    /// <summary>How the bullet is written.</summary>
    Quality,

    /// <summary>Ground it covered that nothing already selected did.</summary>
    Breadth,

    /// <summary>Wording it repeats from a bullet already selected.</summary>
    Overlap,

    /// <summary>Employers already well represented by the selected bullets.</summary>
    Concentration
}

internal sealed record RankingReason(RankingReasonKind Kind, string Text);

/// <param name="Reasons">
/// Why the ranker placed this bullet where it did, in the ranker's own terms. Null for rankers that
/// score against a posting - <see cref="GenerationExplanationService"/> explains those from the
/// requirements the bullet evidences, which is a better account than any sentence the ranker could
/// write. The generic ranker has no requirements to point at, so it says what it was weighing.
/// </param>
internal sealed record RankedBullet(Bullet Bullet, int Score, IReadOnlyList<RankingReason>? Reasons = null);

internal sealed record RewrittenBullet(Bullet Bullet, string Text);

/// <summary>
/// What the rewrite is aiming at: a posting, or - when there is none - a stated audience. The one
/// place the two generation modes differ once bullets have been picked, so everything downstream of
/// here is written once.
/// </summary>
/// <param name="Brief">The line the prompts carry, describing what to write toward.</param>
/// <param name="OrderingRule">
/// What "first" means when deep review sequences the bullets. A resume is read top-down, so the
/// order is an editorial choice, and with no posting it cannot be made against one.
/// </param>
internal sealed record RewriteTarget(string Brief, string OrderingRule)
{
    public static RewriteTarget ForPosting(JobAnalysisDto analysis)
        => new(
            $"Job analysis: {Ai.AiJsonUtilities.ToJson(analysis)}",
            "strongest evidence for this job first");

    public static RewriteTarget ForAudience(string? targetTitle, ResumeAudienceDto audience)
    {
        var title = string.IsNullOrWhiteSpace(targetTitle)
            ? "The candidate has not named a target role."
            : $"The candidate is aiming at: {targetTitle.Trim()}.";

        return new RewriteTarget(
            $"There is no job posting. {title} This resume goes to {Describe(audience)} "
            + "Reword for that reader. Do not tailor to any specific company, and do not invent a "
            + "target role, employer, or posting the candidate has not named.",
            "strongest and most broadly representative first");
    }

    private static string Describe(ResumeAudienceDto audience) => audience switch
    {
        ResumeAudienceDto.Recruiter =>
            "a recruiter, who is screening for role fit at speed and without domain depth, so plain "
            + "language and recognisable role and technology names matter more than architectural detail.",
        ResumeAudienceDto.HiringManager =>
            "a hiring manager filling a role on their own team, who reads for outcomes, scope, and "
            + "what the candidate owned end to end.",
        ResumeAudienceDto.TechnicalLeader =>
            "a technical leader, who reads for depth, systems, and named technologies, and who will "
            + "notice a claim that skips how the work was actually done.",
        ResumeAudienceDto.Executive =>
            "an executive, who reads for business impact and the size of what was owned - cost, "
            + "revenue, risk, headcount, and time.",
        _ => "a general professional audience."
    };
}

/// <summary>
/// The empty analysis a generic generation carries where a tailored one carries a posting's. Every
/// downstream consumer takes one, and a neutral instance is a truer statement of "there was no
/// posting" than a null that each of them would have to interpret.
/// </summary>
internal static class JobAnalysis
{
    public static JobAnalysisDto Neutral { get; } = new([], [], [], [], [], null, null);
}

internal sealed record CoverLetterContext(
    string? JobTitle,
    string? Company,
    JobAnalysisDto Analysis,
    IReadOnlyList<RewrittenBullet> Bullets);

/// <param name="Recommended">The rewrite set the resume is built from.</param>
/// <param name="Refinement">
/// The deep-review versions, whose <see cref="DraftVersionDto.Text"/> is still the raw JSON the
/// agents exchanged. <see cref="GenerationOrchestrator"/> replaces it with the rendered resume for
/// the matching entry in <paramref name="VersionBullets"/> before anything reaches the user.
/// </param>
/// <param name="VersionBullets">Rewrite sets keyed by version label.</param>
internal sealed record BulletRewriteOutcome(
    IReadOnlyList<RewrittenBullet> Recommended,
    RefinementDto? Refinement,
    IReadOnlyDictionary<string, IReadOnlyList<RewrittenBullet>> VersionBullets)
{
    public static BulletRewriteOutcome WithoutRefinement(IReadOnlyList<RewrittenBullet> bullets)
        => new(bullets, null, new Dictionary<string, IReadOnlyList<RewrittenBullet>>());
}
