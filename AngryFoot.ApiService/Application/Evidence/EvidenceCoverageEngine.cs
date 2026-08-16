using AngryFoot.ApiService.Domain;

namespace AngryFoot.ApiService.Application.Evidence;

/// <summary>
/// Links each requirement to the bullets that evidence it. Pure: no database, no AI, no clock.
/// This is the whole report when no AI is configured, and the floor an AI review is allowed to
/// adjust rather than replace.
/// </summary>
internal static class EvidenceCoverageEngine
{
    /// <summary>
    /// A requirement backed by eight bullets is not eight times better evidenced than one backed
    /// by three, and a reader will not read eight. The strongest few make the case.
    /// </summary>
    private const int MaxCitationsPerRequirement = 3;

    public static IReadOnlyList<RequirementEvidence> Evaluate(
        IReadOnlyList<Requirement> requirements,
        IReadOnlyList<Bullet> bullets)
    {
        return requirements.Select(requirement => Evaluate(requirement, bullets)).ToArray();
    }

    private static RequirementEvidence Evaluate(Requirement requirement, IReadOnlyList<Bullet> bullets)
    {
        var citations = bullets
            .Select(bullet => EvidenceStrengthRule.Cite(bullet, requirement))
            .OfType<EvidenceCitation>()
            .OrderByDescending(citation => citation.Strength)
            .Take(MaxCitationsPerRequirement)
            .ToArray();

        var strength = EvidenceStrengthRule.StrengthOf(citations);

        return new RequirementEvidence(
            requirement,
            strength,
            citations,
            EvidenceNarrative.Reasoning(requirement, strength, citations.Length));
    }
}
