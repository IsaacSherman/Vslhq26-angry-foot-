using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence;

/// <summary>
/// Every sentence the deterministic engine says to the user about a requirement, in one place, so
/// the report reads consistently whether or not an AI reviewed it.
/// </summary>
/// <remarks>
/// The wording is deliberately about the document: what the bullets say and do not say, never what
/// the candidate can or cannot do (issue #18).
/// </remarks>
internal static class EvidenceNarrative
{
    /// <param name="citations">
    /// Needed in full rather than as a count because "mentions it" is only true of a bullet that
    /// used the word. A requirement carried entirely by bullets an embedding matched is weak for a
    /// different reason, and telling the user the wrong one sends them to fix the wrong thing.
    /// </param>
    public static string Reasoning(
        Requirement requirement,
        EvidenceStrengthDto strength,
        IReadOnlyList<EvidenceCitation> citations)
    {
        var posting = Posting(requirement);

        if (strength == EvidenceStrengthDto.Missing)
        {
            return $"{posting} No bullet in your library mentions it.";
        }

        if (strength == EvidenceStrengthDto.Strong)
        {
            return $"{posting} {Bullets(citations.Count)} in your library evidence it.";
        }

        return NamesTheRequirement(citations)
            ? $"{posting} {Bullets(citations.Count)} mention it, but none of them show what came of the work, so a reader has only your word for it."
            : $"{posting} No bullet uses that wording, but {Bullets(citations.Count)} read as evidence for it. A reader screening on the words themselves would not find it.";
    }

    public static IReadOnlyList<string> MissingEvidence(
        Requirement requirement,
        EvidenceStrengthDto strength,
        IReadOnlyList<EvidenceCitation> citations)
    {
        if (strength == EvidenceStrengthDto.Strong)
        {
            return [];
        }

        if (strength == EvidenceStrengthDto.Missing)
        {
            return [$"A bullet describing hands-on {requirement.Term} work and what it achieved."];
        }

        return NamesTheRequirement(citations)
            ?
            [
                $"A measurable outcome on one of the bullets that already mentions {requirement.Term} - what changed, by how much, or how quickly.",
                $"Or re-enrich that bullet so {requirement.Term} is picked up as one of its skills rather than passing text."
            ]
            :
            [
                $"The word \"{requirement.Term}\" in the bullet that is already about this work, so a keyword screen finds it too.",
                $"Or add {requirement.Term} to that bullet's skills, which counts as fully as the wording does."
            ];
    }

    /// <summary>Whether any bullet cited here actually used the requirement's own words.</summary>
    private static bool NamesTheRequirement(IReadOnlyList<EvidenceCitation> citations)
        => citations.Any(citation => citation.MatchKind == EvidenceMatchKindDto.ExactTerm);

    public static string Summary(CoverageTotals totals, int strongCount, int weakCount, int missingCount, bool hasBullets)
    {
        if (!hasBullets)
        {
            return "Your bullet library is empty, so there is nothing for this posting's requirements to be evidenced by yet. "
                + "Every requirement below is a starting point for a bullet to write.";
        }

        if (totals.TotalWeight == 0)
        {
            return "No requirements could be extracted from this posting, so there is nothing to measure coverage against. "
                + "Check that the full description text was pasted in.";
        }

        var counted = $"Of {strongCount + weakCount + missingCount} extracted requirements, "
            + $"{strongCount} are evidenced by a bullet that shows a result, {weakCount} are only mentioned, and {missingCount} appear nowhere in your library.";

        var advice = missingCount > 0
            ? " The requirements with no evidence are where a new bullet changes this number most."
            : weakCount > 0
                ? " Adding a measurable outcome to the bullets that only mention a requirement is the cheapest way to raise this number."
                : " Every extracted requirement is evidenced by a bullet that shows a result.";

        return counted + advice;
    }

    private static string Posting(Requirement requirement)
    {
        var opening = requirement.Kind switch
        {
            RequirementKindDto.Required => $"This posting lists \"{requirement.Term}\" as a requirement.",
            RequirementKindDto.Preferred => $"This posting lists \"{requirement.Term}\" as preferred.",
            _ => $"This posting names \"{requirement.Term}\" among its technologies."
        };

        // A requirement the user pasted in that has no row of its own needs accounting for, or the
        // list looks like it lost something.
        return requirement.MergedFrom.Count == 0
            ? opening
            : $"{opening} It also asks for {Quoted(requirement.MergedFrom)}, counted here as the same requirement rather than twice.";
    }

    private static string Quoted(IReadOnlyList<string> terms)
    {
        var quoted = terms.Select(term => $"\"{term}\"").ToArray();
        return quoted.Length == 1
            ? quoted[0]
            : string.Join(", ", quoted[..^1]) + " and " + quoted[^1];
    }

    private static string Bullets(int count) => count == 1 ? "1 bullet" : $"{count} bullets";
}
