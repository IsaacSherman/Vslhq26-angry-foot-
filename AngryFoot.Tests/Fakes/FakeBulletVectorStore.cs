using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Domain;

namespace AngryFoot.Tests.Fakes;

/// <summary>
/// In-memory stand-in for a configured vector store: records upserts/deletes and returns
/// pre-scripted search results, without needing a live Qdrant instance or embedding model.
/// </summary>
internal sealed class FakeBulletVectorStore : IBulletVectorStore
{
    public bool IsAvailable { get; set; } = true;

    public List<Bullet> Upserted { get; } = [];

    public List<Guid> Deleted { get; } = [];

    public IReadOnlyList<BulletSimilarityMatch> SearchResults { get; set; } = [];

    public Task UpsertAsync(Bullet bullet, CancellationToken cancellationToken)
    {
        Upserted.Add(bullet);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid bulletId, CancellationToken cancellationToken)
    {
        Deleted.Add(bulletId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BulletSimilarityMatch>> SearchAsync(string queryText, int topK, CancellationToken cancellationToken)
        => Task.FromResult(SearchResults);
}
