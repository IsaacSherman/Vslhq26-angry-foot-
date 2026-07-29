namespace AngryFoot.Contracts;

public sealed record ProfileDto(
    Guid Id,
    string Name,
    string Email,
    string Phone,
    string LinkedIn,
    string GitHub,
    string ProfessionalSummary,
    IReadOnlyList<WorkHistoryDto> WorkHistory,
    IReadOnlyList<EducationDto> Education,
    IReadOnlyList<CertificationDto> Certifications,
    DateTime ModifiedDate);

public sealed record WorkHistoryDto(
    Guid Id,
    string Employer,
    string? Title,
    string? Location,
    string? StartDate,
    string? EndDate,
    int SortOrder);

public sealed record EducationDto(
    Guid Id,
    string Institution,
    string? Credential,
    string? Field,
    string? GraduationDate,
    int SortOrder);

public sealed record CertificationDto(
    Guid Id,
    string Name,
    string? Issuer,
    string? IssueDate,
    int SortOrder);
