using System.Text.Json.Serialization;

namespace AngryFoot.ApiService.Domain;

/// <summary>
/// The four enrichment facets a user can edit, held together because every operation on them - what
/// the author added, what they removed - applies to all four the same way, and four parallel lists
/// per operation would be four chances to update three of them.
/// </summary>
/// <remarks>
/// <see cref="Bullet.Impact"/> is deliberately absent. It is extracted figures rather than
/// classification, there is nothing for an author to curate in it, and the bullet's own text is
/// already the record of what it claims.
/// </remarks>
public sealed record EnrichmentSet(
    List<string> Tags,
    List<string> Skills,
    List<string> Technologies,
    List<string> JobCategories)
{
    public static EnrichmentSet Empty() => new([], [], [], []);

    /// <summary>
    /// Ignored on the wire and in storage: this type is persisted as JSON, and a computed property
    /// would be written into the column, leaving the stored shape out of step with the literal the
    /// migration hard-codes as its default.
    /// </summary>
    [JsonIgnore]
    public bool IsEmpty => Tags.Count == 0 && Skills.Count == 0 && Technologies.Count == 0 && JobCategories.Count == 0;

    public bool Contains(EnrichmentFacet facet, string value)
        => For(facet).Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));

    public List<string> For(EnrichmentFacet facet) => facet switch
    {
        EnrichmentFacet.Tags => Tags,
        EnrichmentFacet.Skills => Skills,
        EnrichmentFacet.Technologies => Technologies,
        _ => JobCategories
    };

    public static EnrichmentSet From(Func<EnrichmentFacet, IEnumerable<string>> valuesFor)
        => new(
            valuesFor(EnrichmentFacet.Tags).ToList(),
            valuesFor(EnrichmentFacet.Skills).ToList(),
            valuesFor(EnrichmentFacet.Technologies).ToList(),
            valuesFor(EnrichmentFacet.JobCategories).ToList());
}

public enum EnrichmentFacet
{
    Tags,
    Skills,
    Technologies,
    JobCategories
}
