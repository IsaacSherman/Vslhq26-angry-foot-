using System.Text.Json;

namespace AngryFoot.ApiService.Ai;

internal static class AiJsonUtilities
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Pulls a JSON value of type <typeparamref name="T"/> out of an AI response, which may be
    /// wrapped in a code fence, buried in prose, or both. Several readings of the response are
    /// tried in turn: the parser, not a guess about where the text starts, decides which reading
    /// was actually JSON.
    /// </summary>
    public static bool TryDeserialize<T>(string? text, out T? value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var attempted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in ExtractJsonCandidates(text))
        {
            if (!attempted.Add(candidate))
            {
                continue;
            }

            try
            {
                value = JsonSerializer.Deserialize<T>(candidate, Options);
                if (value is not null)
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                // Expected: a candidate that is not valid JSON, or not this shape of JSON, simply
                // means the next reading of the response gets its turn.
            }
        }

        return false;
    }

    public static string ToJson<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    /// <summary>
    /// Renders an AI response for a log line. A parse failure is only diagnosable if the offending
    /// text is in the log, but a cover letter would otherwise flood it - so this caps the length
    /// and reports what was cut. Control characters are escaped rather than written raw, since an
    /// unescaped newline inside a JSON string is one of the failures this is meant to expose, and
    /// a raw one would silently split the log line instead of showing up.
    /// </summary>
    public static string ForLog(string? text, int maxLength = 1500)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text is null ? "(null)" : "(empty)";
        }

        var truncated = text.Length <= maxLength ? text : text[..maxLength];
        var escaped = truncated
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

        return text.Length <= maxLength
            ? escaped
            : $"{escaped} ...[truncated, {text.Length} chars total]";
    }

    /// <summary>
    /// Every plausible reading of a response, best first. Deciding by position alone is what used
    /// to break on chatty output - "Here is the revision [per your critique]: {...}" put a bracket
    /// in front of an object, and the prose won.
    /// </summary>
    private static IEnumerable<string> ExtractJsonCandidates(string text)
    {
        var trimmed = StripCodeFence(text.Trim());

        var objectStart = trimmed.IndexOf('{');
        var arrayStart = trimmed.IndexOf('[');

        // Whichever delimiter opens first is the likelier start, but the other one still gets a
        // turn if it does not pan out.
        var starts = objectStart >= 0 && (arrayStart < 0 || objectStart < arrayStart)
            ? new[] { objectStart, arrayStart }
            : new[] { arrayStart, objectStart };

        foreach (var start in starts)
        {
            if (start >= 0 && ReadBalancedValue(trimmed, start) is { } balanced)
            {
                yield return balanced;
            }
        }

        // Last resort: the widest span between matching delimiters. Rescues output the balanced
        // scan gives up on, such as a response truncated mid-value that still closes at the end.
        foreach (var (open, close) in new[] { ('{', '}'), ('[', ']') })
        {
            var start = trimmed.IndexOf(open);
            var end = trimmed.LastIndexOf(close);
            if (start >= 0 && end > start)
            {
                yield return trimmed[start..(end + 1)];
            }
        }
    }

    private static string StripCodeFence(string trimmed)
    {
        if (!trimmed.StartsWith("```") || !trimmed.Contains('\n'))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```");

        return lastFence > firstNewLine
            ? trimmed[(firstNewLine + 1)..lastFence].Trim()
            : trimmed;
    }

    /// <summary>
    /// Reads the delimiter at <paramref name="start"/> through to its match, counting nesting and
    /// ignoring delimiters inside string literals - a bullet reading "cut costs [by 15%]" must not
    /// close an array that never opened. Null when the value never closes.
    /// </summary>
    private static string? ReadBalancedValue(string text, int start)
    {
        var open = text[start];
        var close = open == '{' ? '}' : ']';
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var character = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == open)
            {
                depth++;
            }
            else if (character == close && --depth == 0)
            {
                return text[start..(i + 1)];
            }
        }

        return null;
    }
}
