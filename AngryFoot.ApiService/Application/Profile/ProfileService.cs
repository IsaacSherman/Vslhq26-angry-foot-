using AngryFoot.ApiService.Data;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Application.Profile;

public interface IProfileService
{
    Task<ProfileDto> GetAsync(CancellationToken cancellationToken);
    Task<ProfileDto> UpsertAsync(ProfileDto profileDto, CancellationToken cancellationToken);
}

public sealed class ProfileService(AngryFootDbContext dbContext) : IProfileService
{
    public async Task<ProfileDto> GetAsync(CancellationToken cancellationToken)
    {
        var profile = await GetOrCreateProfileWithChildrenAsync(cancellationToken);
        return profile.ToDto();
    }

    public async Task<ProfileDto> UpsertAsync(ProfileDto profileDto, CancellationToken cancellationToken)
    {
        var profile = await GetOrCreateProfileAsync(cancellationToken);

        profile.Name = TrimOrEmpty(profileDto.Name);
        profile.Email = TrimOrEmpty(profileDto.Email);
        profile.Phone = TrimOrEmpty(profileDto.Phone);
        profile.LinkedIn = TrimOrEmpty(profileDto.LinkedIn);
        profile.GitHub = TrimOrEmpty(profileDto.GitHub);
        profile.ProfessionalSummary = TrimOrEmpty(profileDto.ProfessionalSummary);
        profile.ModifiedDate = DateTime.UtcNow;

        // Materialize all replacement rows before touching the database so a bad
        // payload cannot fail after the existing rows have been deleted.
        var workHistory = (profileDto.WorkHistory ?? [])
            .OrderBy(x => x.SortOrder)
            .Select(item => new WorkHistory
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                Employer = TrimOrEmpty(item.Employer),
                Title = item.Title?.Trim(),
                Location = item.Location?.Trim(),
                StartDate = item.StartDate?.Trim(),
                EndDate = item.EndDate?.Trim(),
                SortOrder = item.SortOrder
            })
            .ToArray();

        var education = (profileDto.Education ?? [])
            .OrderBy(x => x.SortOrder)
            .Select(item => new Education
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                Institution = TrimOrEmpty(item.Institution),
                Credential = item.Credential?.Trim(),
                Field = item.Field?.Trim(),
                GraduationDate = item.GraduationDate?.Trim(),
                SortOrder = item.SortOrder
            })
            .ToArray();

        var certifications = (profileDto.Certifications ?? [])
            .OrderBy(x => x.SortOrder)
            .Select(item => new Certification
            {
                Id = Guid.NewGuid(),
                ProfileId = profile.Id,
                Name = TrimOrEmpty(item.Name),
                Issuer = item.Issuer?.Trim(),
                IssueDate = item.IssueDate?.Trim(),
                SortOrder = item.SortOrder
            })
            .ToArray();

        // ExecuteDeleteAsync commits immediately outside a transaction, so wrap the
        // delete-and-replace in one to keep the upsert atomic.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.WorkHistory.Where(x => x.ProfileId == profile.Id).ExecuteDeleteAsync(cancellationToken);
        await dbContext.Education.Where(x => x.ProfileId == profile.Id).ExecuteDeleteAsync(cancellationToken);
        await dbContext.Certifications.Where(x => x.ProfileId == profile.Id).ExecuteDeleteAsync(cancellationToken);

        dbContext.WorkHistory.AddRange(workHistory);
        dbContext.Education.AddRange(education);
        dbContext.Certifications.AddRange(certifications);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var updated = await GetOrCreateProfileWithChildrenAsync(cancellationToken);
        return updated.ToDto();
    }

    private static string TrimOrEmpty(string? value) => value?.Trim() ?? string.Empty;

    private async Task<Domain.Profile> GetOrCreateProfileAsync(CancellationToken cancellationToken)
    {
        var profile = await dbContext.Profiles.FirstOrDefaultAsync(cancellationToken);
        if (profile is not null)
        {
            return profile;
        }

        profile = Domain.Profile.CreateEmpty();
        dbContext.Profiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);
        return profile;
    }

    private async Task<Domain.Profile> GetOrCreateProfileWithChildrenAsync(CancellationToken cancellationToken)
    {
        var profile = await dbContext.Profiles
            .Include(x => x.WorkHistory)
            .Include(x => x.Education)
            .Include(x => x.Certifications)
            .FirstOrDefaultAsync(cancellationToken);

        if (profile is not null)
        {
            return profile;
        }

        profile = Domain.Profile.CreateEmpty();
        dbContext.Profiles.Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken);

        await dbContext.Entry(profile).Collection(x => x.WorkHistory).LoadAsync(cancellationToken);
        await dbContext.Entry(profile).Collection(x => x.Education).LoadAsync(cancellationToken);
        await dbContext.Entry(profile).Collection(x => x.Certifications).LoadAsync(cancellationToken);

        return profile;
    }
}
