namespace AngryFoot.Contracts;

/// <param name="JobDescription">
/// Optional. Supplying one adds a coverage report for the resume as written; without one the review
/// is about how the document reads, which is a question a posting is not needed to answer.
/// </param>
public sealed record ResumeReviewRequest(string ResumeText, string? JobDescription = null);

/// <param name="Findings">
/// The same diagnostic type the evidence report uses, so a problem with a bullet is described in the
/// same words and carries the same "Why" panel wherever the user meets it.
/// </param>
/// <param name="Suggestions">
/// What would strengthen this bullet, one line each. Deliberately advice rather than a rewrite: the
/// review never proposes wording, because wording it did not earn would invent the missing facts.
/// </param>
public sealed record ResumeBulletFeedbackDto(
    int Index,
    string Text,
    string? Employer,
    IReadOnlyList<CoverageDiagnosticDto> Findings,
    IReadOnlyList<string> Suggestions);

/// <summary>
/// What can be said about an uploaded resume. Carries no score: a document has no denominator, and a
/// single number over someone's resume reads as a grade on them rather than a list of fixable things.
/// </summary>
/// <param name="Coverage">Present only when the request supplied a job description.</param>
public sealed record ResumeReviewReportDto(
    string Summary,
    IReadOnlyList<CoverageDiagnosticDto> SpotChecks,
    IReadOnlyList<ResumeBulletFeedbackDto> Bullets,
    CoverageSourceDto Source,
    string Disclaimer,
    EvidenceCoverageReportDto? Coverage = null);
