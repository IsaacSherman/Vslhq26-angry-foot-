using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Generation;

internal sealed record RankedBullet(Bullet Bullet, int Score);

internal sealed record RewrittenBullet(Bullet Bullet, string Text);

internal sealed record CoverLetterContext(
    string? JobTitle,
    string? Company,
    JobAnalysisDto Analysis,
    IReadOnlyList<RewrittenBullet> Bullets);

/// <param name="Recommended">The rewrite set the resume is built from.</param>
/// <param name="Refinement">
/// The deep-review versions, whose <see cref="DraftVersionDto.Text"/> is still the raw JSON the
/// agents exchanged. <see cref="GenerationOrchestrator"/> replaces it with the rendered resume for
/// the matching entry in <paramref name="VersionBullets"/> before anything reaches the user.
/// </param>
/// <param name="VersionBullets">Rewrite sets keyed by version label.</param>
internal sealed record BulletRewriteOutcome(
    IReadOnlyList<RewrittenBullet> Recommended,
    RefinementDto? Refinement,
    IReadOnlyDictionary<string, IReadOnlyList<RewrittenBullet>> VersionBullets)
{
    public static BulletRewriteOutcome WithoutRefinement(IReadOnlyList<RewrittenBullet> bullets)
        => new(bullets, null, new Dictionary<string, IReadOnlyList<RewrittenBullet>>());
}
