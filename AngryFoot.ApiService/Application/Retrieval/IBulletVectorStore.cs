using AngryFoot.ApiService.Domain;

namespace AngryFoot.ApiService.Application.Retrieval;

public sealed record BulletSimilarityMatch(Guid BulletId, float Score);

/// <summary>
/// Semantic index over bullet text, used to retrieve the bullets most relevant to a job
/// description instead of loading and keyword-scoring the entire bullet library.
/// </summary>
public interface IBulletVectorStore
{
    /// <summary>False when no embedding model / vector database is configured; callers must
    /// fall back to <see cref="Generation.BulletRankingService"/> in that case.</summary>
    bool IsAvailable { get; }

    Task UpsertAsync(Bullet bullet, CancellationToken cancellationToken);

    Task DeleteAsync(Guid bulletId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BulletSimilarityMatch>> SearchAsync(string queryText, int topK, CancellationToken cancellationToken);
}
