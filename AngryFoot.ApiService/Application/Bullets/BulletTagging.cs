namespace AngryFoot.ApiService.Application.Bullets;

public sealed record BulletTagging(
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Skills,
    IReadOnlyList<string> Technologies,
    IReadOnlyList<string> JobCategories,
    IReadOnlyList<string> Impact);

public interface IBulletTagger
{
    Task<BulletTagging> TagAsync(string bulletText, CancellationToken cancellationToken);
}

public sealed class NoOpBulletTagger : IBulletTagger
{
    public Task<BulletTagging> TagAsync(string bulletText, CancellationToken cancellationToken)
    {
        return Task.FromResult(new BulletTagging([], [], [], [], []));
    }
}
