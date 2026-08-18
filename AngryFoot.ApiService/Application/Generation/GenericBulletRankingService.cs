using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Generation;

/// <summary>
/// Everything the generic ranker weighs that is not a property of the bullet itself: what the
/// candidate is aiming at, and where each of their employers sits in time.
/// </summary>
internal sealed record GenericRankingContext(TitleRelevance Title, EmployerRecency Recency)
{
    /// <summary>No target title and no work history - a pure strength-and-breadth ranking.</summary>
    public static GenericRankingContext Neutral { get; } =
        new(TitleRelevance.None, EmployerRecency.None);
}

/// <summary>
/// Picks bullets for a resume with no posting to aim at: the strongest ones, spread deliberately
/// across skills, technologies, and employers.
/// <para>
/// Greedy rather than a straight sort, because the question at each slot is not "which bullet is
/// best" but "which bullet is best <em>given the ones already chosen</em>". A straight sort by
/// quality hands back the same narrow cluster the candidate happens to have written most about -
/// six Kubernetes bullets, all excellent, describing one year of one job.
/// </para>
/// </summary>
/// <remarks>
/// Deterministic and free: no AI call, and no dependence on the semantic index, which needs query
/// text this mode does not have.
/// </remarks>
internal sealed class GenericBulletRankingService
{
    /// <summary>
    /// What full relevance to the target job title is worth, on the same 0-100 scale as
    /// <see cref="BulletQualityScorer"/>. The largest weight here, and larger than any single
    /// quality signal, because a title is a statement about what the resume is <em>for</em>: asked
    /// for a machine learning role, a solid machine learning bullet has to beat a beautifully
    /// written one about something else. Zero for every bullet when no title was given, which is
    /// what makes the blank-title case a pure strength-and-breadth ranking.
    /// </summary>
    private const double TitleRelevanceWeight = 60;

    /// <summary>
    /// What full novelty is worth, on the same 0-100 scale as <see cref="BulletQualityScorer"/>.
    /// Deliberately below the 30 a measurable result earns: breadth is the tie-breaker between
    /// good bullets, not a reason to print a weak one.
    /// </summary>
    private const double DiversityWeight = 35;

    /// <summary>
    /// What the current role is worth over the earliest one. Small: old work that is the best
    /// evidence of something still belongs on the resume, and this only settles which of two
    /// comparable bullets goes in. Bullets whose employer is not in the work history score the
    /// midpoint, so this never becomes a penalty for untagged material.
    /// </summary>
    private const double RecencyWeight = 22;

    /// <summary>
    /// What identical wording costs. The largest of the three, and larger than any single quality
    /// signal, because a near-duplicate is the one selection a reader will notice and hold against
    /// the whole document.
    /// </summary>
    private const double RedundancyWeight = 60;

    /// <summary>
    /// Overlap below this is what unrelated bullets in one person's library share anyway - a trade,
    /// a seniority, the same handful of verbs. Only the excess above it is penalised, so ordinary
    /// shared vocabulary is not mistaken for repetition.
    /// </summary>
    private const double RedundancyFloor = 0.25;

    /// <summary>How many newly covered facets to name in a reason before it stops being read.</summary>
    private const int MaxNewFacetsNamed = 3;

    /// <summary>How much of a rival bullet to quote when reporting overlap.</summary>
    private const int QuotedTextLength = 60;

    /// <param name="take">
    /// How many bullets to order. The caller asks for more than the resume holds so the bench and
    /// the runners-up are ordered too; bullets past that are never reached, which is what keeps
    /// this quadratic loop bounded by the resume's size rather than by the library's.
    /// </param>
    /// <param name="context">
    /// What the candidate said they are aiming at, and where each employer sits in their history.
    /// <see cref="GenericRankingContext.Neutral"/> for neither, which reduces this to strength and
    /// breadth.
    /// </param>
    public IReadOnlyList<RankedBullet> Rank(
        IReadOnlyList<Bullet> bullets, int take, GenericRankingContext context)
    {
        var remaining = bullets.Select(bullet => Candidate.From(bullet, context)).ToList();
        var selected = new List<RankedBullet>();
        var coveredFacets = new HashSet<string>(StringComparer.Ordinal);
        var chosen = new List<Candidate>();

        var slots = Math.Min(Math.Max(1, take), remaining.Count);

        for (var slot = 0; slot < slots; slot++)
        {
            var best = remaining
                .Select(candidate => Assess(candidate, coveredFacets, chosen))
                .OrderByDescending(x => x.Marginal)
                .ThenByDescending(x => x.Candidate.Quality)
                .ThenByDescending(x => x.Candidate.Bullet.ModifiedDate)
                // Two bullets can be identical in every respect above; ordering by id keeps the
                // same library producing the same resume twice running.
                .ThenBy(x => x.Candidate.Bullet.Id)
                .First();

            selected.Add(new RankedBullet(
                best.Candidate.Bullet,
                (int)Math.Round(best.Marginal, MidpointRounding.AwayFromZero),
                best.Reasons));

            remaining.Remove(best.Candidate);
            chosen.Add(best.Candidate);
            coveredFacets.UnionWith(best.Candidate.Facets.Select(x => x.Key));
        }

        return selected;
    }

    private static Assessment Assess(
        Candidate candidate,
        HashSet<string> coveredFacets,
        IReadOnlyList<Candidate> chosen)
    {
        var reasons = new List<RankingReason>();

        reasons.Add(new RankingReason(
            RankingReasonKind.Quality,
            candidate.EarnedSignals.Count > 0
                ? $"Scores {candidate.Quality} of 100 on how it is written: {string.Join(", ", candidate.EarnedSignals).ToLowerInvariant()}."
                : $"Scores {candidate.Quality} of 100 on how it is written, earning none of the signals the checks look for."));

        var newFacets = candidate.Facets.Where(x => !coveredFacets.Contains(x.Key)).ToArray();
        // A bullet with no enrichment at all has nothing to be novel with. Scored 0 rather than
        // penalised: missing metadata is a fact about the library, not about the achievement.
        var novelty = candidate.Facets.Count == 0
            ? 0
            : newFacets.Length / (double)candidate.Facets.Count;

        if (newFacets.Length > 0 && chosen.Count > 0)
        {
            var named = newFacets.Take(MaxNewFacetsNamed).Select(x => x.Display);
            reasons.Add(new RankingReason(
                RankingReasonKind.Breadth,
                $"The only bullet so far covering {string.Join(", ", named)}."));
        }

        if (candidate.TitleRelevanceReason is { } titleReason)
        {
            reasons.Add(new RankingReason(RankingReasonKind.TitleRelevance, titleReason));
        }

        if (candidate.RecencyReason is { } recencyReason)
        {
            reasons.Add(new RankingReason(RankingReasonKind.Recency, recencyReason));
        }

        var (redundancy, rival) = Redundancy(candidate, chosen);
        if (rival is not null)
        {
            reasons.Add(new RankingReason(
                RankingReasonKind.Overlap,
                $"Repeats {Percent(rival.Value.Overlap)} of the wording of \"{Quote(rival.Value.Text)}\", which is already selected."));
        }

        var marginal = candidate.Quality
            + (TitleRelevanceWeight * candidate.TitleRelevance)
            + (DiversityWeight * novelty)
            + (RecencyWeight * candidate.Recency)
            - (RedundancyWeight * redundancy);

        return new Assessment(candidate, marginal, reasons);
    }

    /// <summary>
    /// How much this bullet repeats one already chosen, and which one. Compared over
    /// <see cref="BulletQualityHeuristics.ContentTokens"/> - the product's existing notion of "the
    /// words in a bullet worth comparing" - so repetition means the same thing here as it does in
    /// the overused-wording diagnostic.
    /// </summary>
    private static (double Penalty, (double Overlap, string Text)? Rival) Redundancy(
        Candidate candidate,
        IReadOnlyList<Candidate> chosen)
    {
        (double Overlap, string Text)? worst = null;

        foreach (var other in chosen)
        {
            var overlap = Jaccard(candidate.Tokens, other.Tokens);
            if (overlap > (worst?.Overlap ?? RedundancyFloor))
            {
                worst = (overlap, other.Bullet.BulletText);
            }
        }

        if (worst is null)
        {
            return (0, null);
        }

        return ((worst.Value.Overlap - RedundancyFloor) / (1 - RedundancyFloor), worst);
    }

    private static double Jaccard(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right)
    {
        if (left.Count == 0 || right.Count == 0)
        {
            return 0;
        }

        var intersection = left.Count(right.Contains);
        var union = left.Count + right.Count - intersection;
        return union == 0 ? 0 : intersection / (double)union;
    }

    private static string Percent(double value)
        => $"{(int)Math.Round(value * 100, MidpointRounding.AwayFromZero)}%";

    private static string Quote(string text)
        => text.Length <= QuotedTextLength ? text : text[..QuotedTextLength].TrimEnd() + "...";

    private sealed record Assessment(Candidate Candidate, double Marginal, IReadOnlyList<RankingReason> Reasons);

    /// <summary>One facet of a bullet, keyed so the same word under two kinds counts as two.</summary>
    private sealed record Facet(string Key, string Display);

    /// <summary>A bullet with everything the loop would otherwise recompute on every pass.</summary>
    private sealed record Candidate(
        Bullet Bullet,
        int Quality,
        IReadOnlyList<string> EarnedSignals,
        IReadOnlyList<Facet> Facets,
        IReadOnlyCollection<string> Tokens,
        double TitleRelevance,
        string? TitleRelevanceReason,
        double Recency,
        string? RecencyReason)
    {
        public static Candidate From(Bullet bullet, GenericRankingContext context)
        {
            var quality = BulletQualityScorer.Score(bullet);

            return new Candidate(
                bullet,
                quality.Score,
                quality.Signals
                    .Where(signal => signal.Earned && signal.Name != BulletQualitySignals.Ownership)
                    .Select(signal => signal.Label)
                    .ToArray(),
                FacetsOf(bullet),
                BulletQualityHeuristics.ContentTokens(bullet.BulletText),
                context.Title.For(bullet.Id),
                context.Title.Describe(bullet.Id),
                context.Recency.For(bullet.SourceEmployer),
                context.Recency.Describe(bullet.SourceEmployer));
        }

        /// <summary>
        /// Ordered by how much a reader learns from hearing the facet named: a technology tells them
        /// more about breadth than a tag does.
        /// </summary>
        private static IReadOnlyList<Facet> FacetsOf(Bullet bullet)
        {
            return Kind("tech", bullet.Technologies)
                .Concat(Kind("skill", bullet.Skills))
                .Concat(Kind("role", bullet.JobCategories))
                .Concat(Kind("tag", bullet.Tags))
                .ToArray();
        }

        private static IEnumerable<Facet> Kind(string kind, IEnumerable<string> values)
        {
            return values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => new Facet($"{kind}:{value.Trim().ToLowerInvariant()}", value.Trim()))
                .DistinctBy(x => x.Key, StringComparer.Ordinal);
        }
    }
}
