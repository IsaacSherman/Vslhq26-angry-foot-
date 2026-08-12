using AngryFoot.ApiService.Domain;

namespace AngryFoot.ApiService.Application.Generation;

/// <summary>
/// Decides whether a bullet counts as evidence for a requirement term. Shared by the fit
/// heuristic and the occupation benchmark so the two never disagree about what "covered" means.
/// </summary>
internal static class BulletEvidence
{
    public static bool Supports(Bullet bullet, string term)
    {
        return bullet.BulletText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || bullet.Skills.Any(s => s.Contains(term, StringComparison.OrdinalIgnoreCase))
            || bullet.Technologies.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    public static bool SupportsAny(Bullet bullet, IReadOnlyList<string> terms)
    {
        return terms.Any(term => Supports(bullet, term));
    }
}
