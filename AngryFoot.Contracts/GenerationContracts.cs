namespace AngryFoot.Contracts;

public sealed record GenerationRequest(
    string JobDescription,
    string? JobTitle,
    string? Company,
    int? MaxBullets);

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

public sealed record GenerationResultDto(
    Guid ArtifactId,
    string ResumeMarkdown,
    string CoverLetterMarkdown,
    JobAnalysisDto Analysis,
    IReadOnlyList<Guid> SelectedBulletIds);

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
    DateTime CreatedDate);
