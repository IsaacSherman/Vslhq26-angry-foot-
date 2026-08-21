namespace AngryFoot.ApiService.Application.Retrieval;

/// <summary>
/// Turns text into vectors. The one door to the embedding deployment: <see cref="IBulletVectorStore"/>
/// indexes through it, near-duplicate detection compares through it, and evidence coverage matches
/// through it, so none of them can disagree about how a piece of text is embedded.
/// <para>
/// Available whenever an embedding deployment is configured, <em>independently of Qdrant</em> - a
/// vector database is only needed to store vectors, not to compute them, and the features that
/// compare a bounded set in memory should not need a container to run.
/// </para>
/// </summary>
public interface ITextEmbedder
{
    /// <summary>False when no embedding deployment is configured; callers must fall back to a
    /// lexical comparison in that case.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Embeds every input in one call, returning vectors in the order given. Null - never a partial
    /// list - when embedding is unavailable or the call failed, because a caller comparing vectors
    /// pairwise cannot do anything useful with some of them.
    /// </summary>
    Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
