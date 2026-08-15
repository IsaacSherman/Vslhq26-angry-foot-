using System.Text.Json.Serialization;

namespace AngryFoot.Contracts;

/// <summary>
/// What became of one candidate bullet. Combinable, because a bullet can be moved <em>and</em>
/// reworded, and recording both beats picking whichever single label looked most notable and
/// leaving the reader to find the rest in prose.
/// <para>
/// Exactly one of <see cref="Selected"/> and <see cref="Omitted"/> is always set.
/// <see cref="Revised"/> and <see cref="Reordered"/> only ever accompany <see cref="Selected"/>.
/// </para>
/// </summary>
/// <remarks>
/// Serialized as names rather than the bit pattern. Every other enum in these contracts goes over
/// the wire as an integer, which is survivable when one number means one thing - but a combined
/// flags value reaches the client as <c>5</c>, and a payload that has to be decoded against this
/// file before it can be read is the opposite of what the rest of this report is for. The converter
/// is declared on the type so both the API and the client honour it without either configuring
/// anything.
/// </remarks>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<BulletDecisionKindDto>))]
public enum BulletDecisionKindDto
{
    /// <summary>Not a decision. Present because a flags enum needs a zero, and never emitted.</summary>
    None = 0,

    /// <summary>Kept. On its own, means kept in the ranker's position and in the candidate's words.</summary>
    Selected = 1 << 0,

    /// <summary>Its wording was tailored to the posting.</summary>
    Revised = 1 << 1,

    /// <summary>It moved from where the ranker put it.</summary>
    Reordered = 1 << 2,

    /// <summary>Left off this resume.</summary>
    Omitted = 1 << 3
}

/// <param name="OriginalText">The bullet as it stands in the library.</param>
/// <param name="FinalText">How it reads on this resume; null when it was left off.</param>
/// <param name="RankerPosition">Where the ranker placed it among the candidates, counting from 1.</param>
/// <param name="ResumePosition">Where it appears on the resume, counting from 1; null when omitted.</param>
public sealed record BulletDecisionDto(
    Guid BulletId,
    string OriginalText,
    string? FinalText,
    BulletDecisionKindDto Kind,
    int RankerPosition,
    int? ResumePosition,
    EvidenceRationaleDto Why);

/// <summary>
/// Why this resume holds the bullets it holds. Every candidate the generator considered appears
/// exactly once, including the ones it left out - an explanation that only covered the bullets
/// that made it would be the more flattering half of the story rather than the whole of it.
/// </summary>
public sealed record GenerationExplanationDto(
    string Summary,
    IReadOnlyList<BulletDecisionDto> Decisions);
