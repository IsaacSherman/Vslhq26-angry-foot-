using AngryFoot.ApiService.Application.Retrieval;

namespace AngryFoot.Tests.Fakes;

/// <summary>
/// In-memory stand-in for a configured embedding deployment.
/// <para>
/// Scripting is deliberately all-or-nothing, mirroring the real contract: if any text in a batch has
/// no scripted vector the whole call returns null, so a caller can never be handed a partial set of
/// vectors and quietly compare the ones it happened to get. It also means a test that scripts nothing
/// exercises the lexical fallback without having to say so.
/// </para>
/// </summary>
internal sealed class FakeTextEmbedder : ITextEmbedder
{
    public bool IsAvailable { get; set; } = true;

    /// <summary>Vectors by exact text; text with no entry embeds as null.</summary>
    public Dictionary<string, float[]> Vectors { get; } = [];

    /// <summary>Thrown by <see cref="EmbedAsync"/> when set, for callers that must survive it.</summary>
    public Exception? EmbedException { get; set; }

    public List<IReadOnlyList<string>> Batches { get; } = [];

    public Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Batches.Add(texts);

        if (EmbedException is not null)
        {
            return Task.FromException<IReadOnlyList<float[]>?>(EmbedException);
        }

        if (!IsAvailable)
        {
            return Task.FromResult<IReadOnlyList<float[]>?>(null);
        }

        var vectors = new List<float[]>(texts.Count);
        foreach (var text in texts)
        {
            if (!Vectors.TryGetValue(text, out var vector))
            {
                return Task.FromResult<IReadOnlyList<float[]>?>(null);
            }

            vectors.Add(vector);
        }

        return Task.FromResult<IReadOnlyList<float[]>?>(vectors);
    }
}
