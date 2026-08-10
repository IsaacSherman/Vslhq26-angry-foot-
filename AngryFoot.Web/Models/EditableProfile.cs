using AngryFoot.Contracts;

namespace AngryFoot.Web.Models;

/// <summary>
/// Two-way-bindable view model for the profile review form, shared by the manual
/// edit page and the LinkedIn import review page &#8212; Blazor's @bind doesn't work
/// directly against the immutable <see cref="ProfileDto"/> record.
/// </summary>
public sealed class EditableProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string LinkedIn { get; set; } = string.Empty;
    public string GitHub { get; set; } = string.Empty;
    public string ProfessionalSummary { get; set; } = string.Empty;
    public List<EditableWorkHistory> WorkHistory { get; set; } = [];
    public List<EditableEducation> Education { get; set; } = [];
    public List<EditableCertification> Certifications { get; set; } = [];
    public DateTime ModifiedDate { get; set; }

    public ProfileDto ToDto()
    {
        return new ProfileDto(
            Id,
            Name?.Trim() ?? string.Empty,
            Email?.Trim() ?? string.Empty,
            Phone?.Trim() ?? string.Empty,
            LinkedIn?.Trim() ?? string.Empty,
            GitHub?.Trim() ?? string.Empty,
            ProfessionalSummary?.Trim() ?? string.Empty,
            WorkHistory.Select(x => new WorkHistoryDto(
                x.Id,
                x.Employer?.Trim() ?? string.Empty,
                x.Title?.Trim(),
                x.Location?.Trim(),
                x.StartDate?.Trim(),
                x.EndDate?.Trim(),
                x.SortOrder)).ToArray(),
            Education.Select(x => new EducationDto(
                x.Id,
                x.Institution?.Trim() ?? string.Empty,
                x.Credential?.Trim(),
                x.Field?.Trim(),
                x.GraduationDate?.Trim(),
                x.SortOrder)).ToArray(),
            Certifications.Select(x => new CertificationDto(
                x.Id,
                x.Name?.Trim() ?? string.Empty,
                x.Issuer?.Trim(),
                x.IssueDate?.Trim(),
                x.SortOrder)).ToArray(),
            ModifiedDate);
    }

    public static EditableProfile FromDto(ProfileDto dto)
    {
        return new EditableProfile
        {
            Id = dto.Id,
            Name = dto.Name,
            Email = dto.Email,
            Phone = dto.Phone,
            LinkedIn = dto.LinkedIn,
            GitHub = dto.GitHub,
            ProfessionalSummary = dto.ProfessionalSummary,
            ModifiedDate = dto.ModifiedDate,
            WorkHistory = dto.WorkHistory.Select(x => new EditableWorkHistory
            {
                Id = x.Id,
                Employer = x.Employer,
                Title = x.Title,
                Location = x.Location,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                SortOrder = x.SortOrder
            }).ToList(),
            Education = dto.Education.Select(x => new EditableEducation
            {
                Id = x.Id,
                Institution = x.Institution,
                Credential = x.Credential,
                Field = x.Field,
                GraduationDate = x.GraduationDate,
                SortOrder = x.SortOrder
            }).ToList(),
            Certifications = dto.Certifications.Select(x => new EditableCertification
            {
                Id = x.Id,
                Name = x.Name,
                Issuer = x.Issuer,
                IssueDate = x.IssueDate,
                SortOrder = x.SortOrder
            }).ToList()
        };
    }
}

public sealed class EditableWorkHistory
{
    public Guid Id { get; set; }
    public string Employer { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Location { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public int SortOrder { get; set; }
}

public sealed class EditableEducation
{
    public Guid Id { get; set; }
    public string Institution { get; set; } = string.Empty;
    public string? Credential { get; set; }
    public string? Field { get; set; }
    public string? GraduationDate { get; set; }
    public int SortOrder { get; set; }
}

public sealed class EditableCertification
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Issuer { get; set; }
    public string? IssueDate { get; set; }
    public int SortOrder { get; set; }
}
