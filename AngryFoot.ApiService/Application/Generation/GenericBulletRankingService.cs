using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Generation;

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
    /// What full novelty is worth, on the same 0-100 scale as <see cref="BulletQualityScorer"/>.
    /// Deliberately below the 30 a measurable result earns: breadth is the tie-breaker between
    /// good bullets, not a reason to print a weak one.
    /// </summary>
    private const double DiversityWeight = 35;

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

    /// <summary>
    /// What a resume drawn entirely from one employer costs. Small, and reached slowly: bullets are
    /// printed grouped under their employer, so several from one job is the normal shape of a
    /// resume rather than a flaw in it.
    /// </summary>
    private const double EmployerConcentrationWeight = 20;

    /// <summary>Bullets from one employer past which the penalty stops growing.</summary>
    private const int ComfortableBulletsPerEmployer = 4;

    /// <summary>How many newly covered facets to name in a reason before it stops being read.</summary>
    private const int MaxNewFacetsNamed = 3;

    /// <summary>How much of a rival bullet to quote when reporting overlap.</summary>
    private const int QuotedTextLength = 60;

    /// <param name="take">
    /// How many bullets to order. The caller asks for more than the resume holds so the bench and
    /// the runners-up are ordered too; bullets past that are never reached, which is what keeps
    /// this quadratic loop bounded by the resume's size rather than by the library's.
    /// </param>
    public IReadOnlyList<RankedBullet> Rank(IReadOnlyList<Bullet> bullets, int take)
    {
        var remaining = bullets.Select(Candidate.From).ToList();
        var selected = new List<RankedBullet>();
        var coveredFacets = new HashSet<string>(StringComparer.Ordinal);
        var employerCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var chosen = new List<Candidate>();

        var slots = Math.Min(Math.Max(1, take), remaining.Count);

        for (var slot = 0; slot < slots; slot++)
        {
            var best = remaining
                .Select(candidate => Assess(candidate, coveredFacets, employerCounts, chosen))
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
            if (best.Candidate.Employer is { } employer)
            {
                employerCounts[employer] = employerCounts.GetValueOrDefault(employer) + 1;
            }
        }

        return selected;
    }

    private static Assessment Assess(
        Candidate candidate,
        HashSet<string> coveredFacets,
        Dictionary<string, int> employerCounts,
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

        var (redundancy, rival) = Redundancy(candidate, chosen);
        if (rival is not null)
        {
            reasons.Add(new RankingReason(
                RankingReasonKind.Overlap,
                $"Repeats {Percent(rival.Value.Overlap)} of the wording of \"{Quote(rival.Value.Text)}\", which is already selected."));
        }

        var concentration = 0.0;
        if (candidate.Employer is { } employer && employerCounts.TryGetValue(employer, out var already))
        {
            concentration = Math.Min(1, already / (double)ComfortableBulletsPerEmployer);
            reasons.Add(new RankingReason(
                RankingReasonKind.Concentration,
                $"{candidate.Bullet.SourceEmployer} already has {already} bullet{(already == 1 ? string.Empty : "s")} among those selected."));
        }

        var marginal = candidate.Quality
            + (DiversityWeight * novelty)
            - (RedundancyWeight * redundancy)
            - (EmployerConcentrationWeight * concentration);

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
        string? Employer)
    {
        public static Candidate From(Bullet bullet)
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
                string.IsNullOrWhiteSpace(bullet.SourceEmployer) ? null : bullet.SourceEmployer.Trim());
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
