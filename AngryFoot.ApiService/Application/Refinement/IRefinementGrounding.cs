namespace AngryFoot.ApiService.Application.Refinement;

/// <summary>
/// Supplies the critic and arbiter with excerpts from the candidate's own bullet library, so their
/// judgement is grounded in what the candidate has actually done rather than in the draft alone.
/// </summary>
internal interface IRefinementGrounding
{
    /// <summary>
    /// Returns a prompt-ready block of the most relevant bullets, or an empty string when there is
    /// nothing to ground against. Never throws: grounding is an enhancement, not a dependency.
    /// </summary>
    Task<string> BuildContextAsync(string queryText, CancellationToken cancellationToken);
}
