using AngryFoot.ApiService.Domain;
using Microsoft.Extensions.AI;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AngryFoot.ApiService.Application.Retrieval;

/// <summary>
/// Embeds bullet text via <see cref="IEmbeddingGenerator{String,Embedding}"/> and indexes it in
/// Qdrant. The vector store only ever returns bullet ids + similarity scores; the caller
/// (<see cref="Generation.GenerationOrchestrator"/>) loads the matching rows from SQLite, which
/// remains the single source of truth for bullet content.
/// </summary>
internal sealed class QdrantBulletVectorStore(
    QdrantClient client,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    RetrievalOptions options,
    ILogger<QdrantBulletVectorStore> logger) : IBulletVectorStore
{
    private const string CollectionName = "bullets";

    private readonly SemaphoreSlim _collectionLock = new(1, 1);
    private volatile bool _collectionEnsured;

    public bool IsAvailable => true;

    public async Task<bool> UpsertAsync(Bullet bullet, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureCollectionAsync(cancellationToken);

            var vector = await embeddingGenerator.GenerateVectorAsync(
                BuildEmbeddingText(bullet), cancellationToken: cancellationToken);

            var point = new PointStruct
            {
                Id = bullet.Id,
                Vectors = vector.ToArray()
            };

            await client.UpsertAsync(CollectionName, [point], cancellationToken: cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to index bullet {BulletId} in Qdrant. It will be missing from semantic retrieval until the next successful upsert; keyword ranking still covers it.",
                bullet.Id);
            return false;
        }
    }

    public async Task DeleteAsync(Guid bulletId, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureCollectionAsync(cancellationToken);
            await client.DeleteAsync(CollectionName, bulletId, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove bullet {BulletId} from Qdrant.", bulletId);
        }
    }

    public async Task<IReadOnlyList<BulletSimilarityMatch>> SearchAsync(string queryText, int topK, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureCollectionAsync(cancellationToken);

            var vector = await embeddingGenerator.GenerateVectorAsync(queryText, cancellationToken: cancellationToken);
            var results = await client.SearchAsync(
                CollectionName,
                vector,
                limit: (ulong)Math.Max(1, topK),
                cancellationToken: cancellationToken);

            return results
                .Where(x => x.Id.HasUuid && Guid.TryParse(x.Id.Uuid, out _))
                .Select(x => new BulletSimilarityMatch(Guid.Parse(x.Id.Uuid), x.Score))
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Qdrant similarity search failed. Falling back to keyword ranking.");
            return [];
        }
    }

    public async Task<IReadOnlySet<Guid>> GetIndexedIdsAsync(IReadOnlyCollection<Guid> bulletIds, CancellationToken cancellationToken)
    {
        if (bulletIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        try
        {
            await EnsureCollectionAsync(cancellationToken);

            var ids = bulletIds.Select(id => (PointId)id).ToList();
            var points = await client.RetrieveAsync(
                CollectionName, ids, withPayload: false, withVectors: false, cancellationToken: cancellationToken);

            return points
                .Where(x => x.Id.HasUuid && Guid.TryParse(x.Id.Uuid, out _))
                .Select(x => Guid.Parse(x.Id.Uuid))
                .ToHashSet();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to check Qdrant indexing status for {Count} bullets.", bulletIds.Count);
            return new HashSet<Guid>();
        }
    }

    public async Task<RetrievalHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureCollectionAsync(cancellationToken);

            var vector = await embeddingGenerator.GenerateVectorAsync(
                "retrieval health check", cancellationToken: cancellationToken);
            if (vector.Length != options.EmbeddingDimensions)
            {
                return new RetrievalHealth(
                    false,
                    $"Embedding deployment returned {vector.Length} dimensions, but Qdrant collection expects {options.EmbeddingDimensions}.");
            }

            return new RetrievalHealth(true, "Semantic retrieval is ready.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Qdrant retrieval health check failed.");
            return new RetrievalHealth(false, $"Semantic retrieval is configured but unavailable: {ex.Message}");
        }
    }

    private async Task EnsureCollectionAsync(CancellationToken cancellationToken)
    {
        if (_collectionEnsured)
        {
            return;
        }

        await _collectionLock.WaitAsync(cancellationToken);
        try
        {
            if (_collectionEnsured)
            {
                return;
            }

            if (!await client.CollectionExistsAsync(CollectionName, cancellationToken))
            {
                await client.CreateCollectionAsync(
                    CollectionName,
                    new VectorParams { Size = (ulong)options.EmbeddingDimensions, Distance = Distance.Cosine },
                    cancellationToken: cancellationToken);
            }

            _collectionEnsured = true;
        }
        finally
        {
            _collectionLock.Release();
        }
    }

    private static string BuildEmbeddingText(Bullet bullet)
    {
        var parts = new List<string> { bullet.BulletText };

        if (bullet.Skills.Count > 0)
        {
            parts.Add("Skills: " + string.Join(", ", bullet.Skills));
        }

        if (bullet.Technologies.Count > 0)
        {
            parts.Add("Technologies: " + string.Join(", ", bullet.Technologies));
        }

        if (bullet.JobCategories.Count > 0)
        {
            parts.Add("Job categories: " + string.Join(", ", bullet.JobCategories));
        }

        return string.Join(". ", parts);
    }
}
