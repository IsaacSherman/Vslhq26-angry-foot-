using System.Text;

namespace AngryFoot.ApiService.Application.Benchmarks;

internal enum OccupationMatchConfidence
{
    None,
    Fuzzy,
    Exact
}

internal sealed record OccupationMatch(
    BenchmarkOccupation Occupation,
    OccupationMatchConfidence Confidence,
    string MatchedTitle);

/// <summary>
/// Maps a job title to an occupation in the bundled dataset. Deliberately deterministic and
/// AI-free: the mapping needs to be explainable and identical between runs, and every
/// occupational title it matches against is published data rather than a model's guess.
/// </summary>
internal static class OccupationTitleMatcher
{
    /// <summary>
    /// Dice coefficient over word tokens below which a candidate is not considered a match at all.
    /// </summary>
    private const double FuzzyThreshold = 0.5;

    /// <summary>
    /// A fuzzy match has to agree on at least two words. Without this, "Engineer" alone scores
    /// 0.67 against "Software Engineer" and any role ending in a generic noun would map to
    /// whichever occupation happened to be listed first.
    /// </summary>
    private const int MinSharedTokens = 2;

    // Level and seniority markers describe where in a career ladder a role sits, not what the
    // occupation is, and O*NET occupations are not ladder-specific.
    private static readonly HashSet<string> IgnoredTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "senior", "sr", "junior", "jr", "staff", "principal", "lead", "associate", "entry",
        "level", "mid", "intermediate", "chief", "head", "of", "the", "and", "a", "an",
        "i", "ii", "iii", "iv", "v", "1", "2", "3", "4", "5"
    };

    public static OccupationMatch? Match(string? jobTitle, IReadOnlyList<BenchmarkOccupation> occupations)
    {
        if (string.IsNullOrWhiteSpace(jobTitle) || occupations.Count == 0)
        {
            return null;
        }

        var tokens = Tokenize(jobTitle);
        if (tokens.Count == 0)
        {
            return null;
        }

        var normalized = string.Join(' ', tokens);

        OccupationMatch? best = null;
        var bestScore = 0.0;

        foreach (var occupation in occupations)
        {
            foreach (var candidate in Candidates(occupation))
            {
                var candidateTokens = Tokenize(candidate);
                if (candidateTokens.Count == 0)
                {
                    continue;
                }

                if (string.Join(' ', candidateTokens) == normalized)
                {
                    return new OccupationMatch(occupation, OccupationMatchConfidence.Exact, candidate);
                }

                var shared = SharedTokenCount(tokens, candidateTokens);
                if (shared < MinSharedTokens)
                {
                    continue;
                }

                var score = 2.0 * shared / (tokens.Count + candidateTokens.Count);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new OccupationMatch(occupation, OccupationMatchConfidence.Fuzzy, candidate);
                }
            }
        }

        return bestScore >= FuzzyThreshold ? best : null;
    }

    private static IEnumerable<string> Candidates(BenchmarkOccupation occupation)
    {
        yield return occupation.Title;
        foreach (var alternate in occupation.AlternateTitles)
        {
            yield return alternate;
        }
    }

    /// <summary>
    /// Lower-cases, drops punctuation and seniority/level markers, and singularizes each word so
    /// a posting for "Data Scientist" reaches the occupation O*NET calls "Data Scientists".
    /// </summary>
    internal static List<string> Tokenize(string title)
    {
        var builder = new StringBuilder(title.Length);
        foreach (var character in title)
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !IgnoredTokens.Contains(token))
            .Select(Singularize)
            .ToList();
    }

    private static string Singularize(string token)
    {
        if (token.Length > 3 && token.EndsWith("ies", StringComparison.Ordinal))
        {
            return string.Concat(token.AsSpan(0, token.Length - 3), "y");
        }

        // "analysts" -> "analyst", but never "analysis" -> "analysi".
        if (token.Length > 3 && token.EndsWith('s') && !token.EndsWith("ss", StringComparison.Ordinal)
            && !token.EndsWith("is", StringComparison.Ordinal) && !token.EndsWith("us", StringComparison.Ordinal))
        {
            return token[..^1];
        }

        return token;
    }

    private static int SharedTokenCount(IEnumerable<string> left, IReadOnlyCollection<string> right)
    {
        return left.Distinct(StringComparer.Ordinal)
            .Count(token => right.Contains(token, StringComparer.Ordinal));
    }
}
