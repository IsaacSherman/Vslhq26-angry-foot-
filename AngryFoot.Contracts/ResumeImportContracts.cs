namespace AngryFoot.Contracts;

/// <summary>Which comparison produced the duplicate scores in a preview.</summary>
public enum DuplicateDetectionModeDto
{
    /// <summary>Embedding cosine similarity via the vector store.</summary>
    Semantic,

    /// <summary>Deterministic text comparison, used when semantic retrieval is unavailable.</summary>
    Lexical
}

public enum DuplicateWarningKindDto
{
    /// <summary>The candidate resembles a bullet already in the library.</summary>
    ExistingBullet,

    /// <summary>The candidate resembles another candidate in the same import batch.</summary>
    BatchCandidate
}

/// <summary>
/// One near-duplicate match for a candidate. Exactly one of <paramref name="ExistingBulletId"/>
/// and <paramref name="CandidateIndex"/> is set, according to <paramref name="Kind"/>.
/// </summary>
public sealed record DuplicateWarningDto(
    DuplicateWarningKindDto Kind,
    Guid? ExistingBulletId,
    int? CandidateIndex,
    string MatchedText,
    double Similarity);

public sealed record CandidateBulletDto(
    int Index,
    string BulletText,
    string? SuggestedEmployer,
    IReadOnlyList<DuplicateWarningDto> Duplicates);

public sealed record ResumeImportPreviewRequest(string ResumeText);

public sealed record ResumeImportPreviewResponse(
    IReadOnlyList<CandidateBulletDto> Candidates,
    DuplicateDetectionModeDto DetectionMode,
    string? DetectionMessage);

/// <summary>
/// A duplicate warning the user dismissed, echoed back from the corresponding
/// <see cref="DuplicateWarningDto"/> so the ignored pair can be recorded with its real score.
/// </summary>
public sealed record IgnoredDuplicateDecision(
    Guid? ExistingBulletId,
    int? CandidateIndex,
    double Similarity);

/// <summary>
/// One candidate the user chose to import. <paramref name="Index"/> is the candidate's index from
/// the preview, which the ignore decisions reference.
/// </summary>
public sealed record ImportBulletItem(
    int Index,
    string BulletText,
    string? SourceEmployer,
    IReadOnlyList<IgnoredDuplicateDecision> IgnoredDuplicates);

public sealed record ConfirmResumeImportRequest(IReadOnlyList<ImportBulletItem> Bullets);

public sealed record ResumeImportResultDto(IReadOnlyList<BulletDto> Created, int IgnoredPairCount);
