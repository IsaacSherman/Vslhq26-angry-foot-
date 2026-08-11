namespace AngryFoot.Contracts;

/// <summary>
/// A prefilled profile draft from a LinkedIn export, plus which sections the export
/// actually contained &#8212; LinkedIn's Profile-only download omits work history,
/// education, and certifications entirely, so the caller needs to know which of
/// those were simply absent from the archive rather than genuinely empty.
/// </summary>
public sealed record LinkedInImportResultDto(
    ProfileDto Profile,
    bool WorkHistoryFound,
    bool EducationFound,
    bool CertificationsFound);
