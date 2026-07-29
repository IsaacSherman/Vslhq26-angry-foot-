using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Generation;

internal sealed class BulletRankingService
{
    public IReadOnlyList<RankedBullet> Rank(IReadOnlyList<Bullet> bullets, JobAnalysisDto analysis, int maxBullets)
    {
        var required = analysis.RequiredSkills.Select(x => x.ToLowerInvariant()).ToHashSet();
        var preferred = analysis.PreferredSkills.Select(x => x.ToLowerInvariant()).ToHashSet();
        var technologies = analysis.Technologies.Select(x => x.ToLowerInvariant()).ToHashSet();
        var keywords = analysis.Keywords.Select(x => x.ToLowerInvariant()).ToHashSet();

        return bullets
            .Select(b => new RankedBullet(b, ScoreBullet(b, required, preferred, technologies, keywords)))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Bullet.ModifiedDate)
            .Take(Math.Max(1, maxBullets))
            .ToArray();
    }

    private static int ScoreBullet(
        Bullet bullet,
        HashSet<string> required,
        HashSet<string> preferred,
        HashSet<string> technologies,
        HashSet<string> keywords)
    {
        var score = 0;
        var text = bullet.BulletText.ToLowerInvariant();

        score += MatchScore(text, required, 8);
        score += MatchScore(text, preferred, 4);
        score += MatchScore(text, technologies, 6);
        score += MatchScore(text, keywords, 2);

        score += IntersectionScore(bullet.Skills, required, 12);
        score += IntersectionScore(bullet.Skills, preferred, 6);
        score += IntersectionScore(bullet.Technologies, technologies, 10);
        score += IntersectionScore(bullet.Tags, keywords, 3);

        return score;
    }

    private static int MatchScore(string text, IEnumerable<string> terms, int weight)
    {
        var hits = terms.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
        return hits * weight;
    }

    private static int IntersectionScore(IEnumerable<string> values, HashSet<string> terms, int weight)
    {
        var hits = values.Count(value => terms.Contains(value.ToLowerInvariant()));
        return hits * weight;
    }
}
