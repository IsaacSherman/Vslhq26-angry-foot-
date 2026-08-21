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
            BulletQualityScorer.Score(bullet),
            bullet.ToEnrichmentDto());
    }

    public static BulletEnrichmentDto ToEnrichmentDto(this Bullet bullet)
    {
        return new BulletEnrichmentDto(
            Describe(bullet, EnrichmentFacet.Tags),
            Describe(bullet, EnrichmentFacet.Skills),
            Describe(bullet, EnrichmentFacet.Technologies),
            Describe(bullet, EnrichmentFacet.JobCategories),
            Suppressed(bullet));
    }

    public static EnrichmentFacet ToFacet(this EnrichmentFacetDto facet) => facet switch
    {
        EnrichmentFacetDto.Tags => EnrichmentFacet.Tags,
        EnrichmentFacetDto.Skills => EnrichmentFacet.Skills,
        EnrichmentFacetDto.Technologies => EnrichmentFacet.Technologies,
        _ => EnrichmentFacet.JobCategories
    };

    public static EnrichmentFacetDto ToDto(this EnrichmentFacet facet) => facet switch
    {
        EnrichmentFacet.Tags => EnrichmentFacetDto.Tags,
        EnrichmentFacet.Skills => EnrichmentFacetDto.Skills,
        EnrichmentFacet.Technologies => EnrichmentFacetDto.Technologies,
        _ => EnrichmentFacetDto.JobCategories
    };

    private static IReadOnlyList<EnrichmentValueDto> Describe(Bullet bullet, EnrichmentFacet facet)
    {
        return Values(bullet, facet)
            .Select(value => new EnrichmentValueDto(
                value,
                bullet.UserAuthored.Contains(facet, value) ? EnrichmentOriginDto.Authored : EnrichmentOriginDto.Suggested))
            .ToArray();
    }

    private static IReadOnlyList<EnrichmentValueDto> Suppressed(Bullet bullet)
    {
        return Enum.GetValues<EnrichmentFacet>()
            .SelectMany(facet => bullet.Suppressed.For(facet).Select(value => new EnrichmentValueDto(value, EnrichmentOriginDto.Suggested)))
            .ToArray();
    }

    private static IReadOnlyList<string> Values(Bullet bullet, EnrichmentFacet facet) => facet switch
    {
        EnrichmentFacet.Tags => bullet.Tags,
        EnrichmentFacet.Skills => bullet.Skills,
        EnrichmentFacet.Technologies => bullet.Technologies,
        _ => bullet.JobCategories
    };

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

    /// <summary>
    /// Reuses tagging the caller already paid for. Converting to the same type the tagger returns is
    /// what lets one merge rule serve both paths.
    /// </summary>
    public static BulletTagging ToTagging(this BulletTaggingDto dto)
        => new(dto.Tags, dto.Skills, dto.Technologies, dto.JobCategories, dto.Impact);
}
