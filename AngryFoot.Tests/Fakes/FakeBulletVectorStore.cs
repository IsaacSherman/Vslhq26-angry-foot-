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

    public bool UpsertSucceeds { get; set; } = true;

    public RetrievalHealth Health { get; set; } = new(true, "Semantic retrieval is ready.");

    public List<Bullet> Upserted { get; } = [];

    public List<Guid> Deleted { get; } = [];

    public IReadOnlyList<BulletSimilarityMatch> SearchResults { get; set; } = [];

    private readonly HashSet<Guid> _indexedIds = [];

    public Task<bool> UpsertAsync(Bullet bullet, CancellationToken cancellationToken)
    {
        Upserted.Add(bullet);
        if (UpsertSucceeds)
        {
            _indexedIds.Add(bullet.Id);
        }

        return Task.FromResult(UpsertSucceeds);
    }

    public Task DeleteAsync(Guid bulletId, CancellationToken cancellationToken)
    {
        Deleted.Add(bulletId);
        _indexedIds.Remove(bulletId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BulletSimilarityMatch>> SearchAsync(string queryText, int topK, CancellationToken cancellationToken)
        => Task.FromResult(SearchResults);

    public Task<IReadOnlySet<Guid>> GetIndexedIdsAsync(IReadOnlyCollection<Guid> bulletIds, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlySet<Guid>>(_indexedIds.Intersect(bulletIds).ToHashSet());

    public Task<RetrievalHealth> CheckHealthAsync(CancellationToken cancellationToken)
        => Task.FromResult(Health);
}
