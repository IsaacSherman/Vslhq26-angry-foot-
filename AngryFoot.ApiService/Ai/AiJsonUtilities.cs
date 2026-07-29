using System.Text.Json;

namespace AngryFoot.ApiService.Ai;

internal static class AiJsonUtilities
{
    public static bool TryDeserialize<T>(string? text, out T? value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var candidate = ExtractJson(text);
        if (candidate is null)
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(candidate, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return value is not null;
        }
        catch (JsonException)
        {
            // Invalid JSON is an expected outcome for AI responses; callers log and fall back.
            return false;
        }
    }

    public static string ToJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string? ExtractJson(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.StartsWith("```") && trimmed.Contains('\n'))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```");
            if (firstNewLine > -1 && lastFence > firstNewLine)
            {
                trimmed = trimmed[(firstNewLine + 1)..lastFence].Trim();
            }
        }

        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        var arrayStart = trimmed.IndexOf('[');
        var arrayEnd = trimmed.LastIndexOf(']');

        if (objectStart >= 0 && objectEnd > objectStart && (arrayStart < 0 || objectStart < arrayStart))
        {
            return trimmed.Substring(objectStart, objectEnd - objectStart + 1);
        }

        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            return trimmed.Substring(arrayStart, arrayEnd - arrayStart + 1);
        }

        return null;
    }
}
