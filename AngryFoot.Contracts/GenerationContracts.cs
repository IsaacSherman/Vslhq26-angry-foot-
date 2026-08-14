namespace AngryFoot.Contracts;

/// <param name="DeepReview">
/// Opt in to the critique-and-revise pass over the bullet rewrites and the cover letter. Six extra
/// AI calls; see the README's "Deep review" section for the latency this adds.
/// </param>
/// <param name="Guidance">
/// The candidate's own clarification of anything ambiguous in their bullets - what an acronym
/// means, which sense of a word applies, what a project actually was. Treated as fact by every
/// AI stage, and applied with or without <paramref name="DeepReview"/>.
/// </param>
public sealed record GenerationRequest(
    string JobDescription,
    string? JobTitle,
    string? Company,
    int? MaxBullets,
    bool DeepReview = false,
    string? Guidance = null);

public sealed record JobAnalysisDto(
    IReadOnlyList<string> RequiredSkills,
    IReadOnlyList<string> PreferredSkills,
    IReadOnlyList<string> Technologies,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> ExperienceThemes,
    string? InferredTitle,
    string? InferredSeniority);

/// <summary>
/// An assessment of how qualified the user is for a specific job, grounded in the
/// user's bullet library rather than a restatement of the job description.
/// </summary>
public sealed record FitAssessmentDto(
    int FitScore,
    string Verdict,
    IReadOnlyList<string> Strengths,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<string> BulletSuggestions);

/// <param name="Benchmark">
/// How the library compares against aggregate occupational data for the mapped occupation.
/// Null when no benchmark dataset is available.
/// </param>
public sealed record JobFitAnalysisDto(
    JobAnalysisDto Job,
    FitAssessmentDto Fit,
    OccupationBenchmarkDto? Benchmark = null);

/// <param name="ResumeMarkdown">The recommended resume version; also what the artifact stores.</param>
/// <param name="ResumeRefinement">
/// Deep-review versions of the whole resume, one per version of the underlying bullet rewrites.
/// Null when deep review was not requested or not applicable.
/// </param>
public sealed record GenerationResultDto(
    Guid ArtifactId,
    string ResumeMarkdown,
    string CoverLetterMarkdown,
    JobAnalysisDto Analysis,
    IReadOnlyList<Guid> SelectedBulletIds,
    RefinementDto? ResumeRefinement = null,
    RefinementDto? CoverLetterRefinement = null);

public sealed record ArtifactSummaryDto(
    Guid Id,
    string? JobTitle,
    string? Company,
    DateTime CreatedDate);

public sealed record GenerationArtifactDto(
    Guid Id,
    string? JobTitle,
    string? Company,
    string JobDescription,
    string ResumeMarkdown,
    string CoverLetterMarkdown,
    IReadOnlyList<Guid> SelectedBulletIds,
    string JobAnalysisJson,
    DateTime CreatedDate,
    RefinementDto? ResumeRefinement = null,
    RefinementDto? CoverLetterRefinement = null);
