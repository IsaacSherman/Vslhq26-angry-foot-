using Microsoft.Extensions.AI;

namespace AngryFoot.Tests.Fakes;

public sealed class ScriptedChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, string> responseFactory) : IChatClient
{
    private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, string> _responseFactory = responseFactory;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = _responseFactory(messages, options);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var text = _responseFactory(messages, options);
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
    }

    public object? GetService(Type serviceType, object? serviceKey) => null;

    public void Dispose()
    {
    }
}
