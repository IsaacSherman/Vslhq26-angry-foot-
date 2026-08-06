using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Data;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;
using SQLitePCL;

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

public sealed class BulletService(
    AngryFootDbContext dbContext,
    IBulletTagger bulletTagger,
    IBulletVectorStore vectorStore,
    ILogger<BulletService> logger) : IBulletService
{
    // Outer safety net; must exceed the tagger's own AI timeout so the tagger can
    // time out gracefully and still return its heuristic fallback.
    private static readonly TimeSpan TaggingTimeout = TimeSpan.FromSeconds(30);

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
            SourceEmployer = NormalizeEmployer(request.SourceEmployer),
            CreatedDate = now,
            ModifiedDate = now,
            EnrichmentState = EnrichmentState.Pending
        };

        await TryApplyTaggingAsync(bullet, cancellationToken);
        dbContext.Bullets.Add(bullet);
        await dbContext.SaveChangesAsync(cancellationToken);
        await vectorStore.UpsertAsync(bullet, cancellationToken);

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
        bullet.SourceEmployer = NormalizeEmployer(request.SourceEmployer);
        bullet.ModifiedDate = DateTime.UtcNow;
        bullet.EnrichmentState = EnrichmentState.Pending;

        await dbContext.SaveChangesAsync(cancellationToken);

        await TryApplyTaggingAsync(bullet, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await vectorStore.UpsertAsync(bullet, cancellationToken);

        return bullet.ToDto();
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await dbContext.Bullets.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
        {
            await vectorStore.DeleteAsync(id, cancellationToken);
        }

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

        await dbContext.SaveChangesAsync(cancellationToken);

        await TryApplyTaggingAsync(bullet, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await vectorStore.UpsertAsync(bullet, cancellationToken);

        return bullet.ToDto();
    }

    private async Task TryApplyTaggingAsync(Bullet bullet, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TaggingTimeout);

        try
        {
            await ApplyTaggingAsync(bullet, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            bullet.EnrichmentState = EnrichmentState.Failed;
        }
        catch (Exception ex)
        {
            if(logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "Failed to enrich bullet with ID {BulletId}", bullet.Id);
            }
            bullet.EnrichmentState = EnrichmentState.Failed;
            
        }
        finally
        {
            if (bullet.EnrichmentState == EnrichmentState.Pending)
            {
                if(logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning("Failed without exception- bullet with ID {BulletId} is still pending after enrichment attempt", bullet.Id);
                }
                bullet.EnrichmentState = EnrichmentState.Failed;
            }
        }
    }

    private async Task ApplyTaggingAsync(Bullet bullet, CancellationToken cancellationToken)
    {
        var tagging = await bulletTagger.TagAsync(bullet.BulletText, cancellationToken);

        bullet.Tags = Normalize(tagging.Tags);
        bullet.Skills = Normalize(tagging.Skills);
        bullet.Technologies = Normalize(tagging.Technologies);
        bullet.JobCategories = Normalize(tagging.JobCategories);
        bullet.Impact = Normalize(tagging.Impact);

        var hasMetadata = bullet.Tags.Count > 0
            || bullet.Skills.Count > 0
            || bullet.Technologies.Count > 0
            || bullet.JobCategories.Count > 0
            || bullet.Impact.Count > 0;

        bullet.EnrichmentState = hasMetadata ? EnrichmentState.Enriched : EnrichmentState.Failed;
    }

    private static string? NormalizeEmployer(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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
