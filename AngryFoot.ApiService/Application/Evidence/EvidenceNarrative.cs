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
    public static string Reasoning(Requirement requirement, EvidenceStrengthDto strength, int citationCount)
    {
        var posting = Posting(requirement);

        return strength switch
        {
            EvidenceStrengthDto.Strong =>
                $"{posting} {Bullets(citationCount)} in your library evidence it.",
            EvidenceStrengthDto.Weak =>
                $"{posting} {Bullets(citationCount)} mention it, but none of them show what came of the work, so a reader has only your word for it.",
            _ =>
                $"{posting} No bullet in your library mentions it."
        };
    }

    public static IReadOnlyList<string> MissingEvidence(Requirement requirement, EvidenceStrengthDto strength)
    {
        return strength switch
        {
            EvidenceStrengthDto.Strong => [],
            EvidenceStrengthDto.Weak =>
            [
                $"A measurable outcome on one of the bullets that already mentions {requirement.Term} - what changed, by how much, or how quickly.",
                $"Or re-enrich that bullet so {requirement.Term} is picked up as one of its skills rather than passing text."
            ],
            _ =>
            [
                $"A bullet describing hands-on {requirement.Term} work and what it achieved."
            ]
        };
    }

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

    private static string Posting(Requirement requirement) => requirement.Kind switch
    {
        RequirementKindDto.Required => $"This posting lists \"{requirement.Term}\" as a requirement.",
        RequirementKindDto.Preferred => $"This posting lists \"{requirement.Term}\" as preferred.",
        _ => $"This posting names \"{requirement.Term}\" among its technologies."
    };

    private static string Bullets(int count) => count == 1 ? "1 bullet" : $"{count} bullets";
}
