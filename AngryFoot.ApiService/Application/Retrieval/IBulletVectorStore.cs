using AngryFoot.ApiService.Domain;

namespace AngryFoot.ApiService.Application.Retrieval;

public sealed record BulletSimilarityMatch(Guid BulletId, float Score);

public sealed record RetrievalHealth(bool IsHealthy, string Message);

/// <summary>
/// Semantic index over bullet text, used to retrieve the bullets most relevant to a job
/// description instead of loading and keyword-scoring the entire bullet library.
/// </summary>
public interface IBulletVectorStore
{
    /// <summary>False when no embedding model / vector database is configured; callers must
    /// fall back to <see cref="Generation.BulletRankingService"/> in that case.</summary>
    bool IsAvailable { get; }

    Task<bool> UpsertAsync(Bullet bullet, CancellationToken cancellationToken);

    Task DeleteAsync(Guid bulletId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BulletSimilarityMatch>> SearchAsync(string queryText, int topK, CancellationToken cancellationToken);

    /// <summary>
    /// Embeds arbitrary text without indexing it, so text that may never become a bullet (an
    /// imported candidate the user discards) can be compared without polluting the collection.
    /// Null when retrieval is unavailable or the embedding call fails.
    /// </summary>
    Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken);

    /// <summary>Which of the given bullet ids already have a point stored in the vector index.</summary>
    Task<IReadOnlySet<Guid>> GetIndexedIdsAsync(IReadOnlyCollection<Guid> bulletIds, CancellationToken cancellationToken);

    /// <summary>Checks whether configured retrieval dependencies are currently usable.</summary>
    Task<RetrievalHealth> CheckHealthAsync(CancellationToken cancellationToken);
}
