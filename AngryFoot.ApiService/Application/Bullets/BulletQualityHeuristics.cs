using System.Text.RegularExpressions;

namespace AngryFoot.ApiService.Application.Bullets;

/// <summary>
/// What can be said about a bullet's writing without asking an AI. Shared by the rewrite
/// assistant's fallback suggestions, the evidence strength rule, and the coverage diagnostics, so
/// "this bullet quantifies a result" means one thing across the product rather than three.
/// </summary>
/// <remarks>
/// English- and industry-keyword-centric, in the same way the offline tagging and ranking
/// heuristics are. These decide what to suggest, never what to store.
/// </remarks>
internal static partial class BulletQualityHeuristics
{
    private const int MinimumContentTokenLength = 3;

    private static readonly string[] OutcomeKeywords =
        ["improv", "increas", "reduc", "faster", "quality", "reliab", "efficien"];

    private static readonly string[] TechnologyKeywords =
        [".net", "c#", "api", "sql", "azure", "blazor", "docker", "github"];

    /// <summary>
    /// Openers that spend the bullet's most valuable words describing a job description rather
    /// than an accomplishment.
    /// </summary>
    private static readonly string[] WeakOpeners =
    [
        "responsible for",
        "worked on",
        "helped with",
        "assisted with",
        "participated in",
        "involved in",
        "duties included",
        "tasked with"
    ];

    /// <summary>
    /// Words too common to mean anything when they recur across bullets. Deliberately short: the
    /// overused-wording diagnostic also excludes requirement terms and tagged technologies, which
    /// is what keeps a genuinely repeated technology from being mistaken for filler.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "the", "for", "with", "that", "this", "from", "into", "across", "over", "under",
        "was", "were", "are", "have", "has", "had", "its", "their", "our", "his", "her", "them",
        "who", "which", "when", "while", "than", "then", "also", "more", "most", "such", "using",
        "used", "use", "new", "all", "any", "one", "two", "per", "via", "each", "both", "other",
        "team", "teams", "work", "working", "project", "projects", "company", "role", "roles"
    };

    public static bool HasMeasurableImpact(string text) => ImpactPattern().IsMatch(text);

    public static bool MentionsOutcome(string text) => ContainsAny(text, OutcomeKeywords);

    public static bool NamesTechnology(string text) => ContainsAny(text, TechnologyKeywords);

    /// <summary>The weak opener this bullet starts with, or null when it starts with something better.</summary>
    public static string? WeakOpener(string text)
    {
        var trimmed = text.TrimStart();
        return Array.Find(WeakOpeners, opener => trimmed.StartsWith(opener, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The distinct meaningful words in a bullet, lowercased. Distinct so a word repeated within
    /// one bullet counts once - the question the caller is asking is how many bullets use a word,
    /// not how many times it appears.
    /// </summary>
    public static IReadOnlyCollection<string> ContentTokens(string text)
    {
        return WordPattern().Matches(text)
            .Select(match => match.Value.ToLowerInvariant())
            .Where(token => token.Length >= MinimumContentTokenLength && !StopWords.Contains(token))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool ContainsAny(string text, string[] keywords)
    {
        return Array.Exists(keywords, keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex("\\b(\\d+%|\\$?\\d+[\\d,]*(\\.\\d+)?|\\d+\\s*(x|hrs?|hours?|days?|weeks?|months?))\\b", RegexOptions.IgnoreCase)]
    private static partial Regex ImpactPattern();

    [GeneratedRegex("[A-Za-z][A-Za-z0-9#+.]*")]
    private static partial Regex WordPattern();
}
