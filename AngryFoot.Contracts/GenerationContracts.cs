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
