using AngryFoot.ApiService.Ai;

namespace AngryFoot.ApiService.Application.Artifacts;

/// <summary>
/// Reads and writes the analysis a generation artifact carries alongside its documents - deep-review
/// versions, evidence coverage. All of it is opaque to queries and only ever read back whole, so a
/// column of JSON beats a table per shape.
/// </summary>
internal static class ArtifactJsonColumns
{
    public static string? ToJson<T>(T? value) where T : class
        => value is null ? null : AiJsonUtilities.ToJson(value);

    public static T? FromJson<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        // Written by us, so unparseable means a hand-edited or truncated row. The artifact is still
        // perfectly readable without whichever analysis failed to come back.
        return AiJsonUtilities.TryDeserialize<T>(json, out var value) ? value : null;
    }
}
