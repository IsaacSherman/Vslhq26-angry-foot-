using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence;

/// <param name="Weight">
/// How much the posting leans on this requirement. Set by <see cref="RequirementSet"/>; the only
/// input to the score other than evidence strength.
/// </param>
/// <param name="MergedFrom">
/// Other wordings the posting used for this same requirement, merged into it by
/// <see cref="RequirementSet"/>. Kept rather than discarded for two reasons: matching still tries
/// every form, so merging never costs a match; and the row can say what was merged, since a
/// requirement quietly vanishing from the list is the opacity this feature exists to remove.
/// </param>
internal sealed record Requirement(
    string Term,
    RequirementKindDto Kind,
    int Weight,
    IReadOnlyList<string> MergedFrom)
{
    public Requirement(string term, RequirementKindDto kind, int weight)
        : this(term, kind, weight, [])
    {
    }

    /// <summary>Every wording this requirement answers to, the chosen one first.</summary>
    public IEnumerable<string> Terms => MergedFrom.Prepend(Term);
}

/// <param name="Strength">
/// Never <see cref="EvidenceStrengthDto.Missing"/> - a citation that evidences nothing is not
/// recorded at all.
/// </param>
internal sealed record EvidenceCitation(
    Bullet Bullet,
    string MatchedTerm,
    EvidenceMatchKindDto MatchKind,
    double? Confidence,
    EvidenceStrengthDto Strength,
    string Because)
{
    public bool IsExactTermMatch => MatchKind == EvidenceMatchKindDto.ExactTerm;
}

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
