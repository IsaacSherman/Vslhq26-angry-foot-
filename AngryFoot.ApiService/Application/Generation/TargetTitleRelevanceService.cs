using AngryFoot.ApiService.Application.Benchmarks;
using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Domain;

namespace AngryFoot.ApiService.Application.Generation;

/// <summary>
/// How relevant each bullet is to the role the candidate says they are aiming at, on a 0-1 scale.
/// <para>
/// <see cref="Summary"/> states what the title did or did not do, because a title that steered
/// nothing has to say so - otherwise a user who typed "Machine Learning Specialist" and got their
/// usual bullets back has no way to tell whether the feature ignored them or their library simply
/// has no machine learning in it.
/// </para>
/// </summary>
internal sealed record TitleRelevance(
    IReadOnlyDictionary<Guid, double> Scores,
    IReadOnlyDictionary<Guid, string> Explanations,
    string Summary)
{
    /// <summary>No title given, or nothing to match it against. Every bullet scores zero, so the
    /// ranker falls back to strength and breadth alone.</summary>
    public static TitleRelevance None { get; } = new(
        new Dictionary<Guid, double>(),
        new Dictionary<Guid, string>(),
        Summary: string.Empty);

    public double For(Guid bulletId) => Scores.GetValueOrDefault(bulletId);

    public string? Describe(Guid bulletId) => Explanations.GetValueOrDefault(bulletId);

    public bool IsActive => Scores.Count > 0;
}

/// <summary>
/// Scores the bullet library against a target job title, from three independent signals.
/// <para>
/// <list type="bullet">
/// <item>The title's own subject words - "Machine Learning" out of "Machine Learning Specialist".</item>
/// <item>The occupation the title maps to in the bundled dataset, and what that occupation is
/// usually asked for.</item>
/// <item>Similarity against the semantic index, where one is configured.</item>
/// </list>
/// </para>
/// <para>
/// Three measurements of one thing rather than three parts of it, so the highest wins: summing
/// would let a bullet scoring moderately on all three outrank one that plainly names the subject
/// the user typed.
/// </para>
/// <para>
/// The word signal exists because the occupational dataset is uneven and small. It covers 21
/// technology occupations and no machine-learning role at all, and some of the occupations it does
/// carry - Data Scientists among them - list only named products, with no skill or knowledge
/// descriptors. Without this signal, "Machine Learning Specialist" would steer nothing on a machine
/// running without a semantic index, which is the configuration this app degrades to by default.
/// </para>
/// </summary>
internal sealed class TargetTitleRelevanceService(
    IOccupationBenchmarkDataset dataset,
    IBulletVectorStore vectorStore,
    ILogger<TargetTitleRelevanceService> logger)
{
    private const string TechnologyKind = "Technology";

    /// <summary>How many matched descriptors to name before the reason stops being read.</summary>
    private const int MaxNamedTerms = 3;

    /// <summary>
    /// Cosine similarity below which a semantic hit is noise. The same floor the tailored
    /// generation path uses for retrieval, for the same reason.
    /// </summary>
    private const float MinimumSemanticSimilarity = 0.35f;

    /// <summary>
    /// Share of the library above which a title has matched so much that it has discriminated
    /// nothing. Reached easily by broad titles: the occupational dataset's skill descriptors are
    /// clipped stems like "develop" and "system", which most of a software library satisfies.
    /// </summary>
    private const double BroadMatchShare = 0.9;

    /// <summary>
    /// Words that name a rung rather than a subject. Stripped so "Machine Learning Specialist"
    /// searches for machine learning: left in, they match most of a software library and the signal
    /// becomes noise. A title made only of these - "Engineer" - contributes no word signal at all,
    /// which is the honest outcome.
    /// </summary>
    private static readonly HashSet<string> GenericRoleNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "engineer", "specialist", "developer", "manager", "analyst", "architect", "scientist",
        "administrator", "consultant", "director", "officer", "technician", "programmer",
        "designer", "professional", "expert", "practitioner", "strategist", "coordinator",
        "supervisor", "technologist", "contractor", "generalist"
    };

    public async Task<TitleRelevance> BuildAsync(
        string? targetTitle, IReadOnlyList<Bullet> bullets, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetTitle) || bullets.Count == 0)
        {
            return TitleRelevance.None;
        }

        var title = targetTitle.Trim();
        var words = ScoreAgainstTitleWords(title, bullets);
        var occupational = ScoreAgainstOccupation(title, bullets);
        var semantic = await ScoreSemanticallyAsync(title, bullets, cancellationToken);

        var scores = new Dictionary<Guid, double>();
        var explanations = new Dictionary<Guid, string>();

        foreach (var bullet in bullets)
        {
            var byWords = words.Scores.GetValueOrDefault(bullet.Id);
            var byOccupation = occupational.Scores.GetValueOrDefault(bullet.Id);
            var bySimilarity = semantic.GetValueOrDefault(bullet.Id);
            var score = Math.Max(byWords, Math.Max(byOccupation, bySimilarity));

            if (score <= 0)
            {
                continue;
            }

            scores[bullet.Id] = score;
            explanations[bullet.Id] = Explain(bullet.Id, title, byWords, byOccupation, bySimilarity, words, occupational);
        }

        return new TitleRelevance(
            scores,
            explanations,
            Summarize(title, words.Subject, occupational.OccupationTitle, semantic.Count > 0, scores.Count, bullets.Count));
    }

    /// <summary>Names the strongest of the three signals, since that is the one that placed the bullet.</summary>
    private static string Explain(
        Guid bulletId,
        string title,
        double byWords,
        double byOccupation,
        double bySimilarity,
        TitleWordScores words,
        OccupationalScores occupational)
    {
        if (byWords >= byOccupation && byWords >= bySimilarity && words.Terms.TryGetValue(bulletId, out var matched))
        {
            return $"Names {string.Join(", ", matched)}, which is what \"{title}\" is about.";
        }

        if (byOccupation >= bySimilarity && occupational.Terms.TryGetValue(bulletId, out var terms))
        {
            return $"Evidences {string.Join(", ", terms)}, which {occupational.OccupationTitle} are typically asked for.";
        }

        return $"Reads as close to \"{title}\" work.";
    }

    private sealed record TitleWordScores(
        IReadOnlyDictionary<Guid, double> Scores,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> Terms,
        string? Subject);

    /// <summary>
    /// Scores each bullet on the subject words of the title itself. The whole subject phrase
    /// appearing together is the strong hit; individual words are scored by the share of them the
    /// bullet carries.
    /// </summary>
    private static TitleWordScores ScoreAgainstTitleWords(string title, IReadOnlyList<Bullet> bullets)
    {
        var subjectWords = OccupationTitleMatcher.Tokenize(title)
            .Where(token => !GenericRoleNouns.Contains(token))
            .ToArray();

        if (subjectWords.Length == 0)
        {
            return new TitleWordScores(new Dictionary<Guid, double>(), new Dictionary<Guid, IReadOnlyList<string>>(), null);
        }

        var phrase = string.Join(' ', subjectWords);
        var scores = new Dictionary<Guid, double>();
        var terms = new Dictionary<Guid, IReadOnlyList<string>>();

        foreach (var bullet in bullets)
        {
            // WordStart rather than whole-word: the tokenizer singularises, so "analytics" arrives
            // here as "analytic" and has to be able to reach the word it came from.
            if (subjectWords.Length > 1 && BulletEvidence.Supports(bullet, phrase, EvidenceMatch.WordStart))
            {
                scores[bullet.Id] = 1;
                terms[bullet.Id] = [phrase];
                continue;
            }

            var hits = subjectWords
                .Where(word => BulletEvidence.Supports(bullet, word, EvidenceMatch.WordStart))
                .ToArray();

            if (hits.Length == 0)
            {
                continue;
            }

            // Capped below a whole-phrase hit: a bullet that says "machine" and a bullet that says
            // "machine learning" are not equally about machine learning.
            scores[bullet.Id] = 0.8 * hits.Length / subjectWords.Length;
            terms[bullet.Id] = hits;
        }

        return new TitleWordScores(scores, terms, phrase);
    }

    private sealed record OccupationalScores(
        IReadOnlyDictionary<Guid, double> Scores,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> Terms,
        string? OccupationTitle);

    /// <summary>
    /// Scores each bullet by the importance-weighted share of the occupation's descriptors it
    /// evidences, reusing the same matcher, the same dataset, and the same whole-word/word-start
    /// rule as the occupation benchmark - so "evidences this" means one thing across the product.
    /// </summary>
    private OccupationalScores ScoreAgainstOccupation(string title, IReadOnlyList<Bullet> bullets)
    {
        var data = dataset.Data;
        var match = OccupationTitleMatcher.Match(title, data.Occupations);

        if (match is null)
        {
            return new OccupationalScores(new Dictionary<Guid, double>(), new Dictionary<Guid, IReadOnlyList<string>>(), null);
        }

        var items = match.Occupation.Items;
        var totalImportance = items.Sum(item => item.Importance);
        if (totalImportance == 0)
        {
            return new OccupationalScores(new Dictionary<Guid, double>(), new Dictionary<Guid, IReadOnlyList<string>>(), match.Occupation.Title);
        }

        var scores = new Dictionary<Guid, double>();
        var terms = new Dictionary<Guid, IReadOnlyList<string>>();

        foreach (var bullet in bullets)
        {
            var matched = items
                .Where(item => BulletEvidence.SupportsAny(
                    bullet,
                    item.EvidenceTerms,
                    item.Kind == TechnologyKind ? EvidenceMatch.WholeWord : EvidenceMatch.WordStart))
                .ToArray();

            if (matched.Length == 0)
            {
                continue;
            }

            // Share of the occupation's total importance this one bullet carries. A bullet is not
            // expected to cover a whole occupation, so this lands low in absolute terms - what
            // matters to the ranker is the ordering between bullets, not the magnitude.
            scores[bullet.Id] = matched.Sum(item => item.Importance) / (double)totalImportance;
            terms[bullet.Id] = matched
                .OrderByDescending(item => item.Importance)
                .Take(MaxNamedTerms)
                .Select(item => item.Name)
                .ToArray();
        }

        return Normalize(scores) is { } normalized
            ? new OccupationalScores(normalized, terms, match.Occupation.Title)
            : new OccupationalScores(scores, terms, match.Occupation.Title);
    }

    private async Task<IReadOnlyDictionary<Guid, double>> ScoreSemanticallyAsync(
        string title, IReadOnlyList<Bullet> bullets, CancellationToken cancellationToken)
    {
        if (!vectorStore.IsAvailable)
        {
            return new Dictionary<Guid, double>();
        }

        try
        {
            // Asking for the whole library: this is a ranking signal over every candidate, not a
            // shortlist, and a top-k cut here would silently zero the relevance of everything below
            // it rather than ordering it.
            var matches = await vectorStore.SearchAsync(
                $"Work performed by a {title}.", bullets.Count, cancellationToken);

            var scores = matches
                .Where(x => x.Score >= MinimumSemanticSimilarity)
                .ToDictionary(x => x.BulletId, x => (double)x.Score);

            return Normalize(scores) ?? scores;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Selection has to work without the index; it is an improvement to relevance, not a
            // dependency of it.
            logger.LogWarning(ex, "Semantic scoring of the target title failed. Falling back to occupational matching alone.");
            return new Dictionary<Guid, double>();
        }
    }

    /// <summary>
    /// Rescales onto 0-1 against the best-scoring bullet, so the two signals are comparable before
    /// they are compared. Null when there is nothing to scale.
    /// </summary>
    private static IReadOnlyDictionary<Guid, double>? Normalize(IReadOnlyDictionary<Guid, double> scores)
    {
        if (scores.Count == 0)
        {
            return null;
        }

        var best = scores.Values.Max();
        return best <= 0 ? null : scores.ToDictionary(x => x.Key, x => x.Value / best);
    }

    private static string Summarize(
        string title,
        string? subject,
        string? occupationTitle,
        bool usedSemantic,
        int scoredCount,
        int bulletCount)
    {
        if (scoredCount == 0)
        {
            return $"No bullet in your library matched what \"{title}\" work involves, so your target title did not "
                + "steer this selection - it was made on strength, breadth, and recency alone.";
        }

        var bases = new List<string>();
        if (subject is not null)
        {
            bases.Add($"on \"{subject}\"");
        }

        if (occupationTitle is not null)
        {
            bases.Add($"against the occupation \"{occupationTitle}\"");
        }

        if (usedSemantic)
        {
            bases.Add("against your indexed bullets");
        }

        // A title that matches nearly everything has not narrowed anything, and "51 of your 51
        // bullets carry relevant work" reads like a strong result rather than a null one. Say which
        // it was, since the ordering the user is looking at came from strength and recency instead.
        var reach = scoredCount >= bulletCount * BroadMatchShare
            ? $"{scoredCount} of your {bulletCount} bullets carry relevant work - nearly all of them, so this title "
              + "narrowed the field very little and the order below is mostly strength, breadth, and recency."
            : $"{scoredCount} of your {bulletCount} bullets carry relevant work.";

        return $"Selected for \"{title}\", matched {string.Join(" and ", bases)}: {reach}";
    }
}
