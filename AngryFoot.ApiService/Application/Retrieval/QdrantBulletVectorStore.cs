using AngryFoot.ApiService.Domain;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AngryFoot.ApiService.Application.Retrieval;


/// <summary>
/// Embeds bullet text via <see cref="ITextEmbedder"/> and indexes it in Qdrant. The vector store
/// only ever returns bullet ids + similarity scores; the caller
/// (<see cref="Generation.GenerationOrchestrator"/>) loads the matching rows from SQLite, which
/// remains the single source of truth for bullet content.
/// </summary>
internal sealed class QdrantBulletVectorStore(
    QdrantClient client,
    ITextEmbedder embedder,
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

            var vector = await EmbedOneAsync(BulletEmbeddingText.For(bullet), cancellationToken);
            if (vector is null)
            {
                return false;
            }

            var point = new PointStruct
            {
                Id = bullet.Id,
                Vectors = vector
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

            var vector = await EmbedOneAsync(queryText, cancellationToken);
            if (vector is null)
            {
                return [];
            }

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

    private async Task<float[]?> EmbedOneAsync(string text, CancellationToken cancellationToken)
        => (await embedder.EmbedAsync([text], cancellationToken))?.SingleOrDefault();

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

            var vector = await EmbedOneAsync("retrieval health check", cancellationToken);
            if (vector is null)
            {
                return new RetrievalHealth(false, "The embedding deployment could not be reached.");
            }

            if (vector.Length != options.EmbeddingDimensions)
            {
                return new RetrievalHealth(
                    false,
                    $"Embedding deployment returned {vector.Length} dimensions, but Qdrant collection expects {options.EmbeddingDimensions}.");
            }

            return new RetrievalHealth(true, "Semantic retrieval is ready.");
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            return new RetrievalHealth(false, $"Cancelled externally before retrieval could be completed.");
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

}