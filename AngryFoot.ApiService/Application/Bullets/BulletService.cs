using AngryFoot.ApiService.Data;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Application.Bullets;

public interface IBulletService
{
    Task<IReadOnlyList<BulletDto>> SearchAsync(string? search, string? tag, string? skill, string? technology, string? category, CancellationToken cancellationToken);
    Task<BulletDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<BulletDto> CreateAsync(CreateBulletRequest request, CancellationToken cancellationToken);
    Task<BulletDto?> UpdateAsync(Guid id, UpdateBulletRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<BulletDto?> EnrichAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class BulletService(AngryFootDbContext dbContext, IBulletTagger bulletTagger) : IBulletService
{
    public async Task<IReadOnlyList<BulletDto>> SearchAsync(string? search, string? tag, string? skill, string? technology, string? category, CancellationToken cancellationToken)
    {
        var bullets = await dbContext.Bullets
            .AsNoTracking()
            .OrderByDescending(x => x.ModifiedDate)
            .ToListAsync(cancellationToken);

        IEnumerable<Bullet> query = bullets;

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x => x.BulletText.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            query = query.Where(x => ContainsIgnoreCase(x.Tags, tag));
        }

        if (!string.IsNullOrWhiteSpace(skill))
        {
            query = query.Where(x => ContainsIgnoreCase(x.Skills, skill));
        }

        if (!string.IsNullOrWhiteSpace(technology))
        {
            query = query.Where(x => ContainsIgnoreCase(x.Technologies, technology));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(x => ContainsIgnoreCase(x.JobCategories, category));
        }

        return query.Select(x => x.ToDto()).ToArray();
    }

    public async Task<BulletDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var bullet = await dbContext.Bullets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return bullet?.ToDto();
    }

    public async Task<BulletDto> CreateAsync(CreateBulletRequest request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var bullet = new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = request.BulletText.Trim(),
            CreatedDate = now,
            ModifiedDate = now,
            EnrichmentState = EnrichmentState.Pending
        };

        await ApplyTaggingAsync(bullet, cancellationToken);

        dbContext.Bullets.Add(bullet);
        await dbContext.SaveChangesAsync(cancellationToken);

        return bullet.ToDto();
    }

    public async Task<BulletDto?> UpdateAsync(Guid id, UpdateBulletRequest request, CancellationToken cancellationToken)
    {
        var bullet = await dbContext.Bullets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (bullet is null)
        {
            return null;
        }

        bullet.BulletText = request.BulletText.Trim();
        bullet.ModifiedDate = DateTime.UtcNow;
        bullet.EnrichmentState = EnrichmentState.Pending;

        await ApplyTaggingAsync(bullet, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return bullet.ToDto();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await dbContext.Bullets.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    public async Task<BulletDto?> EnrichAsync(Guid id, CancellationToken cancellationToken)
    {
        var bullet = await dbContext.Bullets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (bullet is null)
        {
            return null;
        }

        bullet.ModifiedDate = DateTime.UtcNow;
        bullet.EnrichmentState = EnrichmentState.Pending;

        await ApplyTaggingAsync(bullet, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return bullet.ToDto();
    }

    private async Task ApplyTaggingAsync(Bullet bullet, CancellationToken cancellationToken)
    {
        var tagging = await bulletTagger.TagAsync(bullet.BulletText, cancellationToken);

        bullet.Tags = Normalize(tagging.Tags);
        bullet.Skills = Normalize(tagging.Skills);
        bullet.Technologies = Normalize(tagging.Technologies);
        bullet.JobCategories = Normalize(tagging.JobCategories);
        bullet.Impact = Normalize(tagging.Impact);
        bullet.EnrichmentState = EnrichmentState.Enriched;
    }

    private static bool ContainsIgnoreCase(IEnumerable<string> values, string value)
    {
        return values.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> Normalize(IReadOnlyList<string> values)
    {
        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
