using AngryFoot.ApiService.Domain;

namespace AngryFoot.ApiService.Application.Retrieval;

/// <summary>
/// No-op vector store used when no embedding model or Qdrant instance is configured. Mirrors
/// <c>StaticResponseChatClient</c>'s role for <c>IChatClient</c>: the app must run identically
/// to before this feature existed unless a developer opts in.
/// </summary>
internal sealed class NullBulletVectorStore : IBulletVectorStore
{
    public bool IsAvailable => false;

    public Task<bool> UpsertAsync(Bullet bullet, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task DeleteAsync(Guid bulletId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<BulletSimilarityMatch>> SearchAsync(string queryText, int topK, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<BulletSimilarityMatch>>([]);

    public Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken)
        => Task.FromResult<float[]?>(null);

    public Task<IReadOnlySet<Guid>> GetIndexedIdsAsync(IReadOnlyCollection<Guid> bulletIds, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

    public Task<RetrievalHealth> CheckHealthAsync(CancellationToken cancellationToken)
        => Task.FromResult(new RetrievalHealth(false, "Semantic retrieval is not configured."));
}
