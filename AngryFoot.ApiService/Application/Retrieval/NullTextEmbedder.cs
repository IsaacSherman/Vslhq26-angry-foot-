namespace AngryFoot.ApiService.Application.Retrieval;

/// <summary>
/// Used when no embedding deployment is configured. Mirrors <c>StaticResponseChatClient</c>'s role
/// for <c>IChatClient</c>: the dependency always resolves, so a service can never test for
/// embeddings by null-checking it, and the app runs identically to before this feature existed.
/// </summary>
internal sealed class NullTextEmbedder : ITextEmbedder
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<float[]>?>(null);
}
