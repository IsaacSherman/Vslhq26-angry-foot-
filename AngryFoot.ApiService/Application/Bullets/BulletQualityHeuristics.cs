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

    /// <summary>
    /// Verbs that claim the work rather than describe proximity to it. Not an exhaustive list of
    /// good openers - a bullet starting with any past-tense verb also counts - but these are the
    /// ones that additionally say the accomplishment was the writer's.
    /// </summary>
    private static readonly string[] OwnershipVerbs =
    [
        "led", "owned", "drove", "architected", "designed", "built", "created", "founded",
        "established", "spearheaded", "initiated", "launched", "delivered", "rebuilt", "rearchitected"
    ];

    /// <summary>
    /// Wording that hands the accomplishment to a group. A bullet can honestly describe team work,
    /// but a reader cannot tell what the candidate did from one.
    /// </summary>
    private static readonly string[] CollectiveHedges = ["we ", "we'", "our team", "the team", "team's"];

    public static bool HasMeasurableImpact(string text) => ImpactPattern().IsMatch(text);

    /// <summary>
    /// The bullet leads with something the writer did. A past-tense verb or a known action verb in
    /// first position, and not one of the openers that describe an assignment instead.
    /// </summary>
    public static bool OpensWithAction(string text)
    {
        if (WeakOpener(text) is not null)
        {
            return false;
        }

        var first = WordPattern().Match(text.TrimStart());
        if (!first.Success)
        {
            return false;
        }

        var word = first.Value.ToLowerInvariant();
        return word.EndsWith("ed", StringComparison.Ordinal) || Array.Exists(OwnershipVerbs, verb => verb == word);
    }

    /// <summary>The bullet claims the work rather than reporting a group's.</summary>
    public static bool ClaimsOwnership(string text)
    {
        var padded = " " + text.ToLowerInvariant();
        return !Array.Exists(CollectiveHedges, hedge => padded.Contains(" " + hedge, StringComparison.Ordinal))
            && Array.Exists(OwnershipVerbs, verb => ContainsWord(padded, verb));
    }

    /// <summary>
    /// The bullet names something in particular - a product, a system, a tool - rather than
    /// describing a category of work. Detected as a capitalised word that is not simply the start
    /// of a sentence, which is what a proper noun looks like without parsing English.
    /// </summary>
    public static bool IsSpecific(string text)
    {
        return WordPattern().Matches(text)
            .Skip(1)
            .Any(match => char.IsUpper(match.Value[0]));
    }

    /// <summary>
    /// Words as a reader counts them, which includes "40%" and "3x". Deliberately not
    /// <see cref="ContentTokens"/>'s notion of a word: that one exists to compare vocabulary
    /// between bullets and drops anything that is not a word to repeat.
    /// </summary>
    public static int WordCount(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

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

    /// <summary>
    /// Whole-word containment over already-lowercased, space-prefixed text - "led" must not match
    /// inside "fulfilled".
    /// </summary>
    private static bool ContainsWord(string paddedLowerText, string word)
    {
        var index = paddedLowerText.IndexOf(" " + word, StringComparison.Ordinal);
        while (index >= 0)
        {
            var end = index + word.Length + 1;
            if (end == paddedLowerText.Length || !char.IsLetterOrDigit(paddedLowerText[end]))
            {
                return true;
            }

            index = paddedLowerText.IndexOf(" " + word, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    [GeneratedRegex("\\b(\\d+%|\\$?\\d+[\\d,]*(\\.\\d+)?|\\d+\\s*(x|hrs?|hours?|days?|weeks?|months?))\\b", RegexOptions.IgnoreCase)]
    private static partial Regex ImpactPattern();

    [GeneratedRegex("[A-Za-z][A-Za-z0-9#+.]*")]
    private static partial Regex WordPattern();
}
