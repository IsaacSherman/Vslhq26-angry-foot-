using System.Text.Json.Serialization;

namespace AngryFoot.Contracts;

/// <summary>
/// One labeled candidate produced by the optional critique-and-revise ("deep review") pass.
/// </summary>
/// <param name="Label">Stable identifier used to select this version: "v1", "v2", "v1a", "synthesis".</param>
/// <param name="Title">Human-readable name for the version, e.g. "Critic's alternative".</param>
/// <param name="Rationale">One line on where this version came from or what changed in it.</param>
/// <param name="Text">The version's content, in whatever format the refined artifact uses.</param>
public sealed record DraftVersionDto(
    string Label,
    string Title,
    string Rationale,
    string Text);

/// <summary>
/// The versions produced for a single refined artifact, plus the critique that drove them.
/// A null <c>RefinementDto</c> means deep review was not requested, or was skipped because there
/// was no AI draft to critique.
/// </summary>
/// <param name="RecommendedLabel">
/// The version shown by default. The synthesis when it exists, otherwise the author's revision,
/// otherwise the initial draft - never the critic's unreviewed alternative.
/// </param>
public sealed record RefinementDto(
    string RecommendedLabel,
    string? Critique,
    IReadOnlyList<DraftVersionDto> Versions)
{
    /// <summary>Convenience lookup; derived from <see cref="Versions"/>, never serialized.</summary>
    [JsonIgnore]
    public DraftVersionDto? Recommended =>
        Versions.FirstOrDefault(x => x.Label == RecommendedLabel) ?? Versions.LastOrDefault();
}

/// <summary>
/// Labels of the deep-review versions the user picked for a stored generation. A null label
/// leaves that document as it is.
/// </summary>
public sealed record SelectArtifactVersionsRequest(
    string? ResumeVersionLabel,
    string? CoverLetterVersionLabel);

/// <summary>Well-known <see cref="DraftVersionDto.Label"/> values.</summary>
public static class DraftVersionLabels
{
    public const string InitialDraft = "v1";
    public const string CriticAlternative = "v2";
    public const string AuthorRevision = "v1a";
    public const string Synthesis = "synthesis";
}
