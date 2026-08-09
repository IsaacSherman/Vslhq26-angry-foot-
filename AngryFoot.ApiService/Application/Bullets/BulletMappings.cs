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
            isIndexed);
    }
}
