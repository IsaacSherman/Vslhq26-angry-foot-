using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence;

internal static class EvidenceMappings
{
    public static EvidenceCitationDto ToDto(this EvidenceCitation citation)
    {
        return new EvidenceCitationDto(
            citation.Bullet.Id,
            citation.Bullet.BulletText,
            citation.MatchedTerm,
            citation.MatchKind,
            citation.Confidence,
            citation.Because);
    }

    public static RequirementCoverageDto ToDto(this RequirementEvidence evidence)
    {
        return new RequirementCoverageDto(
            evidence.Requirement.Term,
            evidence.Requirement.Kind,
            evidence.Requirement.Weight,
            evidence.Strength,
            new EvidenceRationaleDto(
                evidence.Requirement.Term,
                evidence.Citations.Select(ToDto).ToArray(),
                EvidenceNarrative.MissingEvidence(evidence.Requirement, evidence.Strength, evidence.Citations),
                evidence.Reasoning),
            evidence.Requirement.MergedFrom.Count == 0 ? null : evidence.Requirement.MergedFrom);
    }

    /// <summary>
    /// The rationale for something said about the resume as a whole rather than about one
    /// requirement - an ordering or wording diagnostic, say, which has bullets to cite but no
    /// requirement behind it.
    /// </summary>
    /// <summary>
    /// A citation that quotes a bullet without claiming it matched anything - what a diagnostic or a
    /// build-log entry needs. The empty <c>MatchedTerm</c> is what marks it as such, and is why the
    /// UI shows no match-kind badge for one.
    /// </summary>
    public static EvidenceCitationDto PointerTo(Bullet bullet, string because)
    {
        return new EvidenceCitationDto(
            bullet.Id,
            bullet.BulletText,
            MatchedTerm: string.Empty,
            EvidenceMatchKindDto.ExactTerm,
            Confidence: null,
            because);
    }

    public static EvidenceRationaleDto AboutBullets(
        IReadOnlyList<Bullet> bullets,
        string reasoning,
        IReadOnlyList<string>? missingEvidence = null)
    {
        return new EvidenceRationaleDto(
            Requirement: null,
            bullets.Select(bullet => PointerTo(bullet, "This bullet is what the note above is about.")).ToArray(),
            missingEvidence ?? [],
            reasoning);
    }
}
