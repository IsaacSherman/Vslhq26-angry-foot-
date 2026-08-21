using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence;

/// <summary>
/// Decides how well one bullet evidences one requirement.
/// <para>
/// Whether a bullet counts at all is <see cref="BulletEvidence"/>'s decision and is not
/// second-guessed here. This adds only the question of how <em>well</em> it counts, so a resume
/// that name-drops a technology is not scored the same as one that shows a result from using it.
/// </para>
/// </summary>
internal static class EvidenceStrengthRule
{
    /// <summary>
    /// Named technologies and acronyms need whole-word matching - the "aws" inside "laws" is not
    /// evidence of AWS. Used for every requirement kind, unlike the benchmark's stem matching,
    /// which exists only because O*NET's published terms are deliberately clipped.
    /// </summary>
    private const EvidenceMatch Match = EvidenceMatch.WholeWord;

    /// <summary>Null when this bullet is not evidence for this requirement at all.</summary>
    /// <param name="semanticConfidence">
    /// An embedding's similarity between the two, when one was computed. Consulted only after both
    /// lexical branches decline, so a bullet that names the requirement is never described as merely
    /// resembling it.
    /// </param>
    public static EvidenceCitation? Cite(Bullet bullet, Requirement requirement, double? semanticConfidence = null)
    {
        // Every wording the posting used, not only the one shown: merging "Microsoft Azure" into
        // "Azure" must not stop a bullet that wrote it out in full from counting.
        if (requirement.Terms.FirstOrDefault(x => BulletEvidence.SupportsInMetadata(bullet, x, Match)) is { } tagged)
        {
            return new EvidenceCitation(
                bullet,
                tagged,
                EvidenceMatchKindDto.ExactTerm,
                Confidence: null,
                EvidenceStrengthDto.Strong,
                $"\"{tagged}\" is one of this bullet's extracted skills or technologies, so the bullet is about it rather than mentioning it in passing.");
        }

        if (requirement.Terms.FirstOrDefault(x => BulletEvidence.SupportsInText(bullet, x, Match)) is { } term)
        {
            return BulletQualityHeuristics.HasMeasurableImpact(bullet.BulletText)
                ? new EvidenceCitation(
                    bullet,
                    term,
                    EvidenceMatchKindDto.ExactTerm,
                    Confidence: null,
                    EvidenceStrengthDto.Strong,
                    $"\"{term}\" appears in this bullet, and the bullet quantifies what came of it.")
                : new EvidenceCitation(
                    bullet,
                    term,
                    EvidenceMatchKindDto.ExactTerm,
                    Confidence: null,
                    EvidenceStrengthDto.Weak,
                    $"\"{term}\" appears in this bullet, but the bullet does not quantify a result, so it reads as a mention rather than a demonstration.");
        }

        // Capped at Weak by construction rather than by a later check: full credit means the resume
        // states the requirement, and a vector saying two things are close is not the resume saying
        // anything. The same bar the AI reviewer's citations have to clear.
        return semanticConfidence is { } confidence
            ? new EvidenceCitation(
                bullet,
                requirement.Term,
                EvidenceMatchKindDto.Semantic,
                confidence,
                EvidenceStrengthDto.Weak,
                $"This bullet does not use the word \"{requirement.Term}\", but reads as evidence for it "
                    + $"(similarity {confidence:0.00}, above the {MinimumConfidenceLabel} needed to count).")
            : null;
    }

    private static string MinimumConfidenceLabel => SemanticEvidenceMatcher.MinimumConfidence.ToString("0.00");

    public static EvidenceStrengthDto StrengthOf(IReadOnlyList<EvidenceCitation> citations)
    {
        return citations.Count == 0
            ? EvidenceStrengthDto.Missing
            : citations.Max(citation => citation.Strength);
    }
}
