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
