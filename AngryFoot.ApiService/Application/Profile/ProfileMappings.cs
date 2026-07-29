using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Profile;

public static class ProfileMappings
{
    public static ProfileDto ToDto(this Domain.Profile profile)
    {
        return new ProfileDto(
            profile.Id,
            profile.Name,
            profile.Email,
            profile.Phone,
            profile.LinkedIn,
            profile.GitHub,
            profile.ProfessionalSummary,
            profile.WorkHistory
                .OrderBy(x => x.SortOrder)
                .Select(x => new WorkHistoryDto(
                    x.Id,
                    x.Employer,
                    x.Title,
                    x.Location,
                    x.StartDate,
                    x.EndDate,
                    x.SortOrder))
                .ToArray(),
            profile.Education
                .OrderBy(x => x.SortOrder)
                .Select(x => new EducationDto(
                    x.Id,
                    x.Institution,
                    x.Credential,
                    x.Field,
                    x.GraduationDate,
                    x.SortOrder))
                .ToArray(),
            profile.Certifications
                .OrderBy(x => x.SortOrder)
                .Select(x => new CertificationDto(
                    x.Id,
                    x.Name,
                    x.Issuer,
                    x.IssueDate,
                    x.SortOrder))
                .ToArray(),
            profile.ModifiedDate);
    }
}
