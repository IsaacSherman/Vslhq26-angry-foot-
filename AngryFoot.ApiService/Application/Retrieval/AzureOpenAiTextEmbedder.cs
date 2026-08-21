using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Application.Retrieval;

internal sealed class AzureOpenAiTextEmbedder(
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ILogger<AzureOpenAiTextEmbedder> logger) : ITextEmbedder
{
    public bool IsAvailable => true;

    public async Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        try
        {
            // One request for the whole batch. The callers here embed a bullet library or a
            // requirement set at once, and a per-item loop turns that into dozens of round trips.
            var embeddings = await embeddingGenerator.GenerateAsync(texts, cancellationToken: cancellationToken);
            return embeddings.Select(embedding => embedding.Vector.ToArray()).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to embed {Count} texts. Callers fall back to lexical comparison.", texts.Count);
            return null;
        }
    }
}
