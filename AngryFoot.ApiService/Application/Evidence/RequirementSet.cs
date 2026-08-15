using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence;

/// <summary>
/// Turns an extracted job analysis into the weighted requirements the coverage score is computed
/// over. The single home for the weighting rule: everything downstream reads weights from here
/// rather than deciding for itself what a posting emphasises.
/// </summary>
internal static class RequirementSet
{
    private const int RequiredWeight = 2;
    private const int PreferredWeight = 1;

    public static IReadOnlyList<Requirement> From(JobAnalysisDto analysis)
    {
        var candidates = analysis.RequiredSkills.Select(term => new Requirement(term, RequirementKindDto.Required, RequiredWeight))
            .Concat(analysis.Technologies.Select(term => new Requirement(term, RequirementKindDto.Technology, RequiredWeight)))
            .Concat(analysis.PreferredSkills.Select(term => new Requirement(term, RequirementKindDto.Preferred, PreferredWeight)))
            .Where(requirement => !string.IsNullOrWhiteSpace(requirement.Term))
            .Select(requirement => requirement with { Term = requirement.Term.Trim() });

        return candidates
            .GroupBy(requirement => requirement.Term, StringComparer.OrdinalIgnoreCase)
            // A term the posting lists twice is one requirement, counted at its highest weight.
            // Required outranks Technology at equal weight because it is the stronger claim about
            // what the posting will not do without.
            .Select(group => group.OrderByDescending(x => x.Weight).ThenBy(x => x.Kind).First())
            .ToArray();
    }
}
