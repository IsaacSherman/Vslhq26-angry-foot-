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

/// <summary>
/// Who the generic resume is being written for. There is no posting to tailor to, so this is the
/// only thing the rewrite knows about its reader - it changes wording, never selection.
/// </summary>
public enum ResumeAudienceDto
{
    /// <summary>Screening for role fit, at speed, without domain depth.</summary>
    Recruiter,

    /// <summary>Hiring for their own team; reads for outcomes and scope.</summary>
    HiringManager,

    /// <summary>Reads for depth, systems, and named technologies.</summary>
    TechnicalLeader,

    /// <summary>Reads for business impact and the size of what was owned.</summary>
    Executive
}

/// <summary>
/// A resume built from the whole bullet library with no posting to aim at - the strongest bullets,
/// spread deliberately across skills, technologies, and employers.
/// </summary>
/// <param name="TargetTitle">
/// The role the candidate is aiming at, or null. Shapes the rewrite's wording; unlike
/// <see cref="GenerationRequest.JobTitle"/> it describes an ambition rather than a posting.
/// </param>
/// <param name="Guidance">The same clarification of the candidate's own material as on a tailored generation.</param>
public sealed record GenericGenerationRequest(
    ResumeAudienceDto Audience,
    string? TargetTitle = null,
    int? MaxBullets = null,
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

/// <param name="Coverage">
/// Always present. The deterministic engine produces a complete report on its own, so this is
/// populated with no AI configured; <see cref="EvidenceCoverageReportDto.Source"/> says which
/// path produced it.
/// </param>
/// <param name="Benchmark">
/// How the library compares against aggregate occupational data for the mapped occupation.
/// Null when no benchmark dataset is available.
/// </param>
public sealed record JobEvidenceAnalysisDto(
    JobAnalysisDto Job,
    EvidenceCoverageReportDto Coverage,
    OccupationBenchmarkDto? Benchmark = null);

/// <param name="ResumeMarkdown">The recommended resume version; also what the artifact stores.</param>
/// <param name="ResumeRefinement">
/// Deep-review versions of the whole resume, one per version of the underlying bullet rewrites.
/// Null when deep review was not requested or not applicable.
/// </param>
/// <param name="Coverage">
/// Evidence coverage of the bullets that made it into this resume, in the order they appear in it.
/// Always the deterministic engine's report - a generation already chains several AI calls, and
/// adding an evidence review to it would cost another one per generation.
/// </param>
public sealed record GenerationResultDto(
    Guid ArtifactId,
    string ResumeMarkdown,
    string CoverLetterMarkdown,
    JobAnalysisDto Analysis,
    IReadOnlyList<Guid> SelectedBulletIds,
    RefinementDto? ResumeRefinement = null,
    RefinementDto? CoverLetterRefinement = null,
    EvidenceCoverageReportDto? Coverage = null,
    GenerationExplanationDto? Explanation = null);

/// <param name="IsGeneric">
/// True when this was generated with no posting. <paramref name="JobTitle"/> is then the title the
/// candidate was aiming at rather than one a posting advertised, and <paramref name="Company"/> is
/// always null.
/// </param>
public sealed record ArtifactSummaryDto(
    Guid Id,
    string? JobTitle,
    string? Company,
    DateTime CreatedDate,
    bool IsGeneric = false,
    ResumeAudienceDto? Audience = null);

/// <param name="Coverage">
/// The coverage report as of the moment this resume was generated. Null for artifacts created
/// before the report existed, for any row whose stored JSON no longer reads back, and for generic
/// generations, which have no posting to be covered against.
/// </param>
/// <param name="IsGeneric">
/// True when this was generated with no posting. <paramref name="JobDescription"/> and
/// <paramref name="CoverLetterMarkdown"/> are then both empty.
/// </param>
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
    RefinementDto? CoverLetterRefinement = null,
    EvidenceCoverageReportDto? Coverage = null,
    GenerationExplanationDto? Explanation = null,
    bool IsGeneric = false,
    ResumeAudienceDto? Audience = null);
