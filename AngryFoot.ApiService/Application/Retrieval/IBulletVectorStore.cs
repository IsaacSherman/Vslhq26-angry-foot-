using AngryFoot.ApiService.Domain;

namespace AngryFoot.ApiService.Application.Retrieval;

public sealed record BulletSimilarityMatch(Guid BulletId, float Score);

public sealed record RetrievalHealth(bool IsHealthy, string Message);

/// <summary>
/// Semantic index over bullet text, used to retrieve the bullets most relevant to a job
/// description instead of loading and keyword-scoring the entire bullet library. Embedding text is
/// <see cref="ITextEmbedder"/>'s job; this stores and searches what it produces.
/// </summary>
public interface IBulletVectorStore
{
    /// <summary>False when no embedding model / vector database is configured; callers must
    /// fall back to <see cref="Generation.BulletRankingService"/> in that case.</summary>
    bool IsAvailable { get; }

    Task<bool> UpsertAsync(Bullet bullet, CancellationToken cancellationToken);

    Task DeleteAsync(Guid bulletId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BulletSimilarityMatch>> SearchAsync(string queryText, int topK, CancellationToken cancellationToken);

    /// <summary>Which of the given bullet ids already have a point stored in the vector index.</summary>
    Task<IReadOnlySet<Guid>> GetIndexedIdsAsync(IReadOnlyCollection<Guid> bulletIds, CancellationToken cancellationToken);

    /// <summary>Checks whether configured retrieval dependencies are currently usable.</summary>
    Task<RetrievalHealth> CheckHealthAsync(CancellationToken cancellationToken);
}
