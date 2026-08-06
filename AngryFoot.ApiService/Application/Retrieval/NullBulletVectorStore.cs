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

    public Task UpsertAsync(Bullet bullet, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task DeleteAsync(Guid bulletId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<BulletSimilarityMatch>> SearchAsync(string queryText, int topK, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<BulletSimilarityMatch>>([]);
}
