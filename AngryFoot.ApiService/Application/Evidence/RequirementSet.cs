using System.Text.RegularExpressions;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence;

/// <summary>
/// Turns an extracted job analysis into the weighted requirements the coverage score is computed
/// over. The single home for the weighting rule: everything downstream reads weights from here
/// rather than deciding for itself what a posting emphasises.
/// <para>
/// It is also where near-duplicate requirements are merged. That happens here rather than in the job
/// analyzer for two reasons: the analyzer normalizes each list separately and so structurally cannot
/// see that "Azure" in technologies and "Microsoft Azure" in required skills are one thing, and
/// rewriting its output would falsify <c>GenerationArtifact.JobAnalysisJson</c>, which records what
/// the posting actually asked for rather than what we made of it.
/// </para>
/// <para>
/// Merging is deliberately limited to terms that reduce to the same thing once the posting's own
/// phrasing is stripped. It does <em>not</em> merge a term into a longer one that contains it, because
/// nothing syntactic separates "Kubernetes administration" - which is the same ask worded at length -
/// from "Azure DevOps", which is a different product. Losing a requirement the user pasted in is a
/// worse failure than scoring a narrow one twice, so the ambiguous case is left alone.
/// </para>
/// </summary>
internal static partial class RequirementSet
{
    private const int RequiredWeight = 2;
    private const int PreferredWeight = 1;

    /// <summary>
    /// Vendors whose name is dropped when deciding whether two terms are the same requirement. A
    /// posting that says "Microsoft Azure" in one list and "Azure" in another is not asking for two
    /// things, and scoring it as two costs the candidate points for a distinction nobody drew.
    /// </summary>
    private static readonly string[] VendorPrefixes =
        ["microsoft ", "amazon ", "amazon web services ", "aws ", "google ", "apache ", "oracle ", "adobe ", "red hat "];

    /// <summary>
    /// Phrasing a posting wraps a requirement in. Stripped only for comparison; the requirement is
    /// still shown to the user in the words the posting used.
    /// </summary>
    private static readonly string[] LeadIns =
        ["experience with ", "experience in ", "expertise in ", "proficiency with ", "proficiency in ",
         "knowledge of ", "familiarity with ", "strong ", "hands-on ", "hands on ", "demonstrated ", "proven "];

    public static IReadOnlyList<Requirement> From(JobAnalysisDto analysis)
    {
        return analysis.RequiredSkills.Select(term => new Requirement(term, RequirementKindDto.Required, RequiredWeight))
            .Concat(analysis.Technologies.Select(term => new Requirement(term, RequirementKindDto.Technology, RequiredWeight)))
            .Concat(analysis.PreferredSkills.Select(term => new Requirement(term, RequirementKindDto.Preferred, PreferredWeight)))
            .Where(requirement => !string.IsNullOrWhiteSpace(requirement.Term))
            .Select(requirement => requirement with { Term = requirement.Term.Trim() })
            .GroupBy(requirement => Canonical(requirement.Term), StringComparer.Ordinal)
            // A term the posting lists twice is one requirement, counted at its highest weight.
            // Required outranks Technology at equal weight because it is the stronger claim about
            // what the posting will not do without.
            .Select(Merge)
            .ToArray();
    }

    /// <summary>
    /// One requirement out of every wording that reduces to the same thing. The weight and kind come
    /// from the strongest claim the posting made; the wording shown is the shortest, since that is
    /// the one stripped of the posting's phrasing and the one a bullet is likeliest to have used.
    /// </summary>
    private static Requirement Merge(IEnumerable<Requirement> group)
    {
        var all = group.ToArray();
        var strongest = all.OrderByDescending(x => x.Weight).ThenBy(x => x.Kind).First();
        var display = all
            .OrderBy(x => x.Term.Length)
            .ThenBy(x => x.Term, StringComparer.OrdinalIgnoreCase)
            .First();

        var rest = all
            .Where(x => !string.Equals(x.Term, display.Term, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Term)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return display with { Weight = strongest.Weight, Kind = strongest.Kind, MergedFrom = rest };
    }

    /// <summary>
    /// The form two terms are compared in: case, punctuation, vendor name, and the phrasing a posting
    /// wraps a requirement in all removed, because none of them change what is being asked for.
    /// </summary>
    private static string Canonical(string term)
    {
        var value = Punctuation().Replace(term.ToLowerInvariant(), " ");
        value = Whitespace().Replace(value, " ").Trim();

        bool stripped;
        do
        {
            stripped = false;
            foreach (var leadIn in LeadIns.Concat(VendorPrefixes))
            {
                if (value.StartsWith(leadIn, StringComparison.Ordinal) && value.Length > leadIn.Length)
                {
                    value = value[leadIn.Length..];
                    stripped = true;
                }
            }

            var years = YearsOfPrefix().Match(value);
            if (years.Success && value.Length > years.Length)
            {
                value = value[years.Length..];
                stripped = true;
            }
        }
        while (stripped);

        return value;
    }

    // Keeps ".NET", "C#" and "C++" from losing the characters that identify them: they collapse to a
    // space here only for comparison, which is enough to align "C#" with "C #" without merging it
    // with "C".
    [GeneratedRegex(@"[^a-z0-9+#.\s]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\d+\+?\s*(years?|yrs?)\s*(of\s*)?")]
    private static partial Regex YearsOfPrefix();
}
