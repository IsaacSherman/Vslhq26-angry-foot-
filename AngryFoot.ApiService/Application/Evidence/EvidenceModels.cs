using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence;

/// <param name="Weight">
/// How much the posting leans on this requirement. Set by <see cref="RequirementSet"/>; the only
/// input to the score other than evidence strength.
/// </param>
internal sealed record Requirement(string Term, RequirementKindDto Kind, int Weight);

/// <param name="Strength">
/// Never <see cref="EvidenceStrengthDto.Missing"/> - a citation that evidences nothing is not
/// recorded at all.
/// </param>
internal sealed record EvidenceCitation(
    Bullet Bullet,
    string MatchedTerm,
    bool IsExactTermMatch,
    EvidenceStrengthDto Strength,
    string Because);

internal sealed record RequirementEvidence(
    Requirement Requirement,
    EvidenceStrengthDto Strength,
    IReadOnlyList<EvidenceCitation> Citations,
    string Reasoning);

/// <param name="Bullets">The bullets under analysis, in the order they will be read.</param>
/// <param name="IsOrderedDocument">
/// True when <paramref name="Bullets"/> is a resume in reading order, so sequence is an editorial
/// choice worth diagnosing. False for the whole library, where the order is only a modified date
/// and criticising it would be criticising nothing the user chose.
/// </param>
internal sealed record DiagnosticScope(IReadOnlyList<Bullet> Bullets, bool IsOrderedDocument)
{
    public static DiagnosticScope Library(IReadOnlyList<Bullet> bullets) => new(bullets, IsOrderedDocument: false);

    public static DiagnosticScope Resume(IReadOnlyList<Bullet> bullets) => new(bullets, IsOrderedDocument: true);
}

internal sealed record DiagnosticContext(
    JobAnalysisDto Analysis,
    IReadOnlyList<RequirementEvidence> Evidence,
    DiagnosticScope Scope);
