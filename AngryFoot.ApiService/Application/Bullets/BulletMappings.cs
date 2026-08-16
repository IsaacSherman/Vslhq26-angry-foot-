using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Bullets;

public static class BulletMappings
{
    public static BulletDto ToDto(this Bullet bullet, bool isIndexed = false)
    {
        return new BulletDto(
            bullet.Id,
            bullet.BulletText,
            bullet.Tags,
            bullet.Skills,
            bullet.Technologies,
            bullet.JobCategories,
            bullet.Impact,
            bullet.SourceEmployer,
            bullet.EnrichmentState switch
            {
                EnrichmentState.Pending => EnrichmentStateDto.Pending,
                EnrichmentState.Enriched => EnrichmentStateDto.Enriched,
                _ => EnrichmentStateDto.Failed
            },
            bullet.CreatedDate,
            bullet.ModifiedDate,
            isIndexed,
            BulletQualityScorer.Score(bullet));
    }

    /// <param name="bullet">
    /// The revision's parent, needed to answer whether the revision still describes current wording.
    /// </param>
    public static BulletRevisionDto ToDto(this BulletRevision revision, Bullet bullet)
    {
        return new BulletRevisionDto(
            revision.Id,
            revision.BulletId,
            revision.Mode switch
            {
                BulletRevisionMode.Grammar => BulletRevisionModeDto.Grammar,
                BulletRevisionMode.StrongerWording => BulletRevisionModeDto.StrongerWording,
                BulletRevisionMode.Star => BulletRevisionModeDto.Star,
                BulletRevisionMode.Executive => BulletRevisionModeDto.Executive,
                BulletRevisionMode.Technical => BulletRevisionModeDto.Technical,
                _ => BulletRevisionModeDto.Ats
            },
            revision.RevisedText,
            revision.SourceText,
            revision.Version,
            revision.Rationale,
            revision.IsAiGenerated,
            // Stale means "written from wording that has since changed" - which a revision that has
            // itself been promoted has not, even though its source text is now out of date.
            IsStale: !string.Equals(revision.SourceText, bullet.BulletText, StringComparison.Ordinal)
                && !string.Equals(revision.RevisedText, bullet.BulletText, StringComparison.Ordinal),
            revision.CreatedDate,
            BulletQualityScorer.Score(bullet, revision.RevisedText));
    }
}
