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
    /// Verbs common enough at the start of a resume bullet to be recognised as actions without a
    /// past-tense ending. Not a test of ownership - see <see cref="SharedCreditMarker"/> for that.
    /// </summary>
    private static readonly string[] ActionVerbs =
    [
        // Irregular past tenses, which the "-ed" test cannot reach.
        "led", "built", "rebuilt", "drove", "ran", "wrote", "rewrote", "taught", "won", "grew",
        "took", "made", "brought", "began", "set", "cut", "oversaw", "sold", "sent", "kept",
        "held", "met", "chose", "drew", "found", "gave", "left", "put", "spoke", "spent", "dealt",
        // Base forms, for a library written in the present tense.
        "lead", "own", "drive", "build", "run", "ship", "grow", "win", "write", "teach", "mentor",
        "rebuild", "oversee", "begin", "bring", "take", "make"
    ];

    /// <summary>
    /// Wording that hands the accomplishment to a group.
    /// <para>
    /// A resume elides its subject by convention: every bullet is read as the author's own work
    /// unless it says otherwise. So the question is not "does this prove ownership" - nothing short
    /// of writing "I" would - but "does this give the credit away", which only these do. Anything
    /// that names the author's role in the work, in any of the hundreds of verbs English offers for
    /// it, is left alone.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Possessives are deliberately absent. "the software team's first CI/CD server" says whose
    /// server it was, not who built it, and reading that as shared credit is how a heuristic ends
    /// up arguing with someone about their own work.
    /// </remarks>
    private static readonly string[] SharedCreditMarkers =
    [
        "we", "we've", "we'd", "we're", "our team", "the team and i", "assisted with", "assisted in",
        "contributed to", "helped with", "helped to", "participated in", "part of a team",
        "part of the team", "supported the team", "collaborated with the team"
    ];

    public static bool HasMeasurableImpact(string text) => MeasurableImpact(text) is not null;

    /// <summary>The figure the bullet quantifies its result with, or null when it states none.</summary>
    public static string? MeasurableImpact(string text)
    {
        var match = ImpactPattern().Match(text);
        return match.Success ? match.Value : null;
    }

    public static bool OpensWithAction(string text) => OpeningAction(text) is not null;

    /// <summary>
    /// The action the bullet leads with, or null when it leads with something else. A past-tense
    /// verb or a known action verb in first position, and not an opener that describes an
    /// assignment instead.
    /// </summary>
    public static string? OpeningAction(string text)
    {
        if (WeakOpener(text) is not null)
        {
            return null;
        }

        var first = WordPattern().Match(text.TrimStart());
        if (!first.Success)
        {
            return null;
        }

        var lowered = first.Value.ToLowerInvariant();

        return lowered.EndsWith("ed", StringComparison.Ordinal) || Array.Exists(ActionVerbs, verb => verb == lowered)
            ? first.Value
            : null;
    }

    /// <summary>
    /// The wording that gives this bullet's credit away, or null when it reads as the author's own
    /// work - which, on a resume, is what the absence of such wording means.
    /// </summary>
    public static string? SharedCreditMarker(string text)
    {
        // A curly apostrophe is what most editors produce, and "we've" has to read the same either way.
        var padded = " " + text.ToLowerInvariant().Replace('’', '\'');
        return Array.Find(SharedCreditMarkers, marker => ContainsWord(padded, marker));
    }

    public static bool IsSpecific(string text) => ProperNoun(text) is not null;

    /// <summary>
    /// The particular thing this bullet names - a product, a system, a tool - or null when it
    /// describes a category of work instead. Detected as a capitalised word that is not simply the
    /// start of a sentence, which is what a proper noun looks like without parsing English.
    /// </summary>
    public static string? ProperNoun(string text)
    {
        return WordPattern().Matches(text)
            .Skip(1)
            .FirstOrDefault(match => char.IsUpper(match.Value[0]))
            ?.Value;
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

    /// <remarks>
    /// Boundaries are per-alternative rather than wrapped around the whole group. A trailing
    /// boundary after the group cannot follow "40%" - percent to full stop is not a word boundary -
    /// so the engine backtracked onto the bare-number branch and reported the figure as "40".
    /// Invisible while this only answered yes or no; wrong the moment the figure is quoted back.
    /// </remarks>
    [GeneratedRegex("(\\b\\d+%|\\b\\$?\\d+[\\d,]*(\\.\\d+)?\\b|\\b\\d+\\s*(x|hrs?|hours?|days?|weeks?|months?)\\b)", RegexOptions.IgnoreCase)]
    private static partial Regex ImpactPattern();

    [GeneratedRegex("[A-Za-z][A-Za-z0-9#+.]*")]
    private static partial Regex WordPattern();
}
