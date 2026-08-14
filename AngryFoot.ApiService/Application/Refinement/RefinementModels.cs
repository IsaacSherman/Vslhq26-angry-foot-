namespace AngryFoot.ApiService.Application.Refinement;

/// <summary>
/// Everything the critique-and-revise agents need about one draft. The pipeline itself is
/// content-agnostic: a draft is text, and <see cref="OutputContract"/> tells every agent what
/// shape that text has to keep.
/// </summary>
/// <param name="ArtifactKind">
/// What is being refined, in prose - "resume bullet", "cover letter". Used in the prompts so the
/// agents know what they are looking at.
/// </param>
/// <param name="OutputContract">
/// The exact format each agent must return the content in, e.g. "a single resume bullet as plain
/// text" or "a JSON array of { bulletId, rewritten }". Enforced only by the prompt; callers must
/// still validate what comes back.
/// </param>
/// <param name="SourceMaterial">
/// The facts the draft was written from - the user's original text, the job analysis - so the
/// critic can spot claims the source does not support.
/// </param>
/// <param name="Draft">The first draft (v1), as produced today.</param>
/// <param name="GroundingQuery">
/// Text to retrieve library grounding with. Defaults to the draft when null.
/// </param>
/// <param name="UserGuidance">
/// The candidate's own clarification of what their material means. Binding: it outranks the
/// critique, because the agents are guessing at an ambiguity the candidate can simply resolve.
/// </param>
internal sealed record RefinementRequest(
    string ArtifactKind,
    string OutputContract,
    string SourceMaterial,
    string Draft,
    string? GroundingQuery = null,
    string? UserGuidance = null);

/// <summary>
/// The reviewer's verdict on a draft: what is wrong with it, and the reviewer's own attempt at
/// doing better. Surfaced on its own so the user can weigh in before the later stages run.
/// </summary>
internal sealed record RefinementCritique(string Critique, string? Alternative);
