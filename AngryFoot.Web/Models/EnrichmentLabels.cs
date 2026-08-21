using AngryFoot.Contracts;

namespace AngryFoot.Web.Models;

/// <summary>
/// Display-only wording for bullet enrichment, held here rather than in markup so the facets and
/// origins read the same wherever they appear. Nothing in here is a rule; the rules live in the API
/// service.
/// </summary>
public static class EnrichmentLabels
{
    public static string Facet(EnrichmentFacetDto facet) => facet switch
    {
        EnrichmentFacetDto.Skills => "Skills",
        EnrichmentFacetDto.Technologies => "Technologies",
        EnrichmentFacetDto.Tags => "Tags",
        _ => "Job categories"
    };

    public static string Singular(EnrichmentFacetDto facet) => facet switch
    {
        EnrichmentFacetDto.Skills => "Skill",
        EnrichmentFacetDto.Technologies => "Technology",
        EnrichmentFacetDto.Tags => "Tag",
        _ => "Job category"
    };

    public static string OriginBadge(EnrichmentOriginDto origin)
        => origin == EnrichmentOriginDto.Authored ? "text-bg-primary" : "text-bg-light text-muted";

    /// <summary>A mark as well as a colour, so the two origins survive being read without colour.</summary>
    public static string OriginMark(EnrichmentOriginDto origin)
        => origin == EnrichmentOriginDto.Authored ? "✓" : "○";

    public static string OriginTooltip(EnrichmentOriginDto origin) => origin == EnrichmentOriginDto.Authored
        ? "You added this. Re-running AI enrichment will keep it."
        : "AI suggested this from the bullet's wording. Re-running enrichment may replace it.";

    /// <summary>
    /// The facet a value belongs to, and the set with that facet replaced. Kept as extensions on the
    /// request type so the editor can build up a change without four near-identical branches at every
    /// call site.
    /// </summary>
    public static IReadOnlyList<string> For(this SetBulletEnrichmentRequest request, EnrichmentFacetDto facet) => facet switch
    {
        EnrichmentFacetDto.Skills => request.Skills,
        EnrichmentFacetDto.Technologies => request.Technologies,
        EnrichmentFacetDto.Tags => request.Tags,
        _ => request.JobCategories
    };

    public static SetBulletEnrichmentRequest With(
        this SetBulletEnrichmentRequest request,
        EnrichmentFacetDto facet,
        IReadOnlyList<string> values) => facet switch
    {
        EnrichmentFacetDto.Skills => request with { Skills = values },
        EnrichmentFacetDto.Technologies => request with { Technologies = values },
        EnrichmentFacetDto.Tags => request with { Tags = values },
        _ => request with { JobCategories = values }
    };
}
