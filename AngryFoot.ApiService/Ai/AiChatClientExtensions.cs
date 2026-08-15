using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Ai;

/// <param name="Value">The parsed payload, or null when the model's answer was unusable.</param>
/// <param name="RawText">
/// The model's answer verbatim. Kept even on success so callers can log it when a payload parses
/// but fails their own validation.
/// </param>
/// <param name="SchemaEnforced">
/// False when the request went out without a JSON schema - either the type could not be expressed
/// as one, or the deployment rejected it and the call was retried without.
/// </param>
internal readonly record struct AiJsonResponse<T>(T? Value, string RawText, bool SchemaEnforced)
{
    public bool Succeeded => Value is not null;
}

internal static class AiChatClientExtensions
{
    public static async Task<string> GetTextResponseAsync(this IChatClient chatClient, string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var response = await chatClient.GetResponseAsync(
            BuildMessages(systemPrompt, userPrompt),
            cancellationToken: cancellationToken);

        return response.Text ?? string.Empty;
    }

    /// <summary>
    /// Asks for a response constrained to <typeparamref name="T"/>'s JSON schema, so the model is
    /// prevented from drifting into a different shape rather than being asked nicely not to.
    /// </summary>
    /// <remarks>
    /// Schema support depends on the deployment and the pinned <c>AzureOpenAI:ServiceVersion</c>,
    /// so a rejected schema retries once without it rather than failing the feature. The tolerant
    /// parse is still applied either way: a schema guarantees shape, not that the provider honoured
    /// it, and it can never guarantee the values are meaningful - callers keep validating those.
    /// </remarks>
    public static async Task<AiJsonResponse<T>> GetJsonResponseAsync<T>(
        this IChatClient chatClient,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        var messages = BuildMessages(systemPrompt, userPrompt);
        var options = TryBuildSchemaOptions<T>();

        if (options is null)
        {
            logger?.LogWarning(
                "No JSON schema could be built for {PayloadType}, so its shape is only requested in the prompt.",
                typeof(T).Name);
        }

        ChatResponse response;
        var schemaEnforced = options is not null;

        try
        {
            response = await chatClient.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (options is not null && LooksLikeSchemaRejection(ex))
        {
            // The deployment or API version would not take a json_schema response format. Falling
            // back keeps every AI feature working on older Azure OpenAI service versions - but it
            // silently gives up the shape guarantee, so it is worth saying so out loud.
            logger?.LogWarning(
                ex,
                "The AI deployment rejected a JSON schema for {PayloadType}. Retrying without it; shape is no longer guaranteed. Check AzureOpenAI:ServiceVersion.",
                typeof(T).Name);

            schemaEnforced = false;
            response = await chatClient.GetResponseAsync(messages, options: null, cancellationToken);
        }

        var text = response.Text ?? string.Empty;
        AiJsonUtilities.TryDeserialize<T>(text, out var value);

        return new AiJsonResponse<T>(value, text, schemaEnforced);
    }

    /// <summary>
    /// Whether the service turned the request down over the schema itself, as opposed to failing
    /// for any of the ordinary reasons. Retrying a timeout or a dead connection without the schema
    /// would just spend a second call to fail the same way, and would blame the wrong thing in the
    /// log while doing it - so the error has to name the response format to earn a retry.
    /// </summary>
    private static bool LooksLikeSchemaRejection(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("response_format", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("json_schema", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Null when <typeparamref name="T"/> has no expressible schema - an unbounded dictionary or a
    /// bare <see cref="object"/> cannot be described in a closed schema, and neither can a root
    /// that is not an object.
    /// </summary>
    private static ChatOptions? TryBuildSchemaOptions<T>()
    {
        try
        {
            return new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema<T>(
                    AiJsonUtilities.SerializerOptions,
                    schemaName: typeof(T).Name,
                    schemaDescription: null)
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<ChatMessage> BuildMessages(string systemPrompt, string userPrompt) =>
    [
        new(ChatRole.System, systemPrompt),
        new(ChatRole.User, userPrompt)
    ];
}
