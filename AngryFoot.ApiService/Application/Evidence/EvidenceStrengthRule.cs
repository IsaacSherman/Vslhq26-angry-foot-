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
    public static EvidenceCitation? Cite(Bullet bullet, Requirement requirement)
    {
        var term = requirement.Term;

        if (BulletEvidence.SupportsInMetadata(bullet, term, Match))
        {
            return new EvidenceCitation(
                bullet,
                term,
                IsExactTermMatch: true,
                EvidenceStrengthDto.Strong,
                $"\"{term}\" is one of this bullet's extracted skills or technologies, so the bullet is about it rather than mentioning it in passing.");
        }

        if (!BulletEvidence.SupportsInText(bullet, term, Match))
        {
            return null;
        }

        return BulletQualityHeuristics.HasMeasurableImpact(bullet.BulletText)
            ? new EvidenceCitation(
                bullet,
                term,
                IsExactTermMatch: true,
                EvidenceStrengthDto.Strong,
                $"\"{term}\" appears in this bullet, and the bullet quantifies what came of it.")
            : new EvidenceCitation(
                bullet,
                term,
                IsExactTermMatch: true,
                EvidenceStrengthDto.Weak,
                $"\"{term}\" appears in this bullet, but the bullet does not quantify a result, so it reads as a mention rather than a demonstration.");
    }

    public static EvidenceStrengthDto StrengthOf(IReadOnlyList<EvidenceCitation> citations)
    {
        return citations.Count == 0
            ? EvidenceStrengthDto.Missing
            : citations.Max(citation => citation.Strength);
    }
}
