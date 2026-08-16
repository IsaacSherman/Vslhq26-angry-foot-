using AngryFoot.ApiService.Data;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Application.Bullets;

public interface IBulletRevisionService
{
    /// <summary>Null when the bullet does not exist, as distinct from a bullet with no revisions.</summary>
    Task<IReadOnlyList<BulletRevisionDto>?> GetAsync(Guid bulletId, CancellationToken cancellationToken);

    Task<BulletRevisionDto?> CreateAsync(Guid bulletId, CreateBulletRevisionRequest request, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid bulletId, Guid revisionId, CancellationToken cancellationToken);

    /// <summary>
    /// Makes a revision the bullet's canonical text. The wording it replaces is not lost: it is the
    /// revision's own <see cref="BulletRevision.SourceText"/>, which is kept.
    /// </summary>
    Task<PromoteBulletRevisionResponse?> PromoteAsync(Guid bulletId, Guid revisionId, CancellationToken cancellationToken);
}

internal sealed class BulletRevisionService(
    AngryFootDbContext dbContext,
    IBulletService bulletService,
    IBulletRewriteAssistant rewriteAssistant) : IBulletRevisionService
{
    public async Task<IReadOnlyList<BulletRevisionDto>?> GetAsync(Guid bulletId, CancellationToken cancellationToken)
    {
        var bullet = await LoadWithRevisionsAsync(bulletId, cancellationToken);
        return bullet is null ? null : Project(bullet);
    }

    public async Task<BulletRevisionDto?> CreateAsync(
        Guid bulletId,
        CreateBulletRevisionRequest request,
        CancellationToken cancellationToken)
    {
        var bullet = await LoadWithRevisionsAsync(bulletId, cancellationToken);
        if (bullet is null)
        {
            return null;
        }

        var rewrite = await rewriteAssistant.RewriteAsync(
            bullet.BulletText,
            request.DeepReview,
            cancellationToken,
            request.Mode,
            request.Guidance);

        var mode = ToDomain(request.Mode);
        var revision = new BulletRevision
        {
            Id = Guid.NewGuid(),
            BulletId = bullet.Id,
            Mode = mode,
            RevisedText = rewrite.RewrittenText.Trim(),
            SourceText = bullet.BulletText,
            Version = NextVersion(bullet, mode),
            Rationale = rewrite.Rationale,
            // The heuristic fallback explains itself through suggestions rather than a rationale,
            // which is what tells the two apart after the fact.
            IsAiGenerated = rewrite.Rationale is not null,
            CreatedDate = DateTime.UtcNow
        };

        dbContext.BulletRevisions.Add(revision);
        await dbContext.SaveChangesAsync(cancellationToken);

        return revision.ToDto(bullet);
    }

    public async Task<bool> DeleteAsync(Guid bulletId, Guid revisionId, CancellationToken cancellationToken)
    {
        var deleted = await dbContext.BulletRevisions
            .Where(x => x.Id == revisionId && x.BulletId == bulletId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }

    public async Task<PromoteBulletRevisionResponse?> PromoteAsync(
        Guid bulletId,
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        var bullet = await LoadWithRevisionsAsync(bulletId, cancellationToken);
        var revision = bullet?.Revisions.FirstOrDefault(x => x.Id == revisionId);
        if (bullet is null || revision is null)
        {
            return null;
        }

        // Through IBulletService so a promoted revision gets the same re-tagging and re-indexing as
        // any other edit; the bullet's text has one write path and this is not a second one.
        var updated = await bulletService.UpdateAsync(
            bulletId,
            new UpdateBulletRequest(revision.RevisedText, bullet.SourceEmployer),
            cancellationToken);

        if (updated is null)
        {
            return null;
        }

        var refreshed = await LoadWithRevisionsAsync(bulletId, cancellationToken);
        return new PromoteBulletRevisionResponse(updated, refreshed is null ? [] : Project(refreshed));
    }

    /// <remarks>
    /// Untracked deliberately. Promotion writes the bullet through <see cref="IBulletService"/>, and
    /// reading it back through the change tracker afterwards would hand out the pre-promotion text -
    /// which is exactly the wording a caller asks for in order to see that it changed.
    /// </remarks>
    private Task<Bullet?> LoadWithRevisionsAsync(Guid bulletId, CancellationToken cancellationToken)
        => dbContext.Bullets
            .AsNoTracking()
            .Include(x => x.Revisions)
            .FirstOrDefaultAsync(x => x.Id == bulletId, cancellationToken);

    private static IReadOnlyList<BulletRevisionDto> Project(Bullet bullet)
        => bullet.Revisions
            .OrderBy(x => x.Mode)
            .ThenByDescending(x => x.Version)
            .Select(revision => revision.ToDto(bullet))
            .ToArray();

    private static int NextVersion(Bullet bullet, BulletRevisionMode mode)
        => bullet.Revisions.Where(x => x.Mode == mode).Select(x => x.Version).DefaultIfEmpty(0).Max() + 1;

    private static BulletRevisionMode ToDomain(BulletRevisionModeDto mode) => mode switch
    {
        BulletRevisionModeDto.Grammar => BulletRevisionMode.Grammar,
        BulletRevisionModeDto.StrongerWording => BulletRevisionMode.StrongerWording,
        BulletRevisionModeDto.Star => BulletRevisionMode.Star,
        BulletRevisionModeDto.Executive => BulletRevisionMode.Executive,
        BulletRevisionModeDto.Technical => BulletRevisionMode.Technical,
        _ => BulletRevisionMode.Ats
    };
}
