using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Ai;

internal static class AiChatClientExtensions
{
    public static async Task<string> GetTextResponseAsync(this IChatClient chatClient, string systemPrompt, string userPrompt, CancellationToken cancellationToken)
    {
        var response = await chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt)
            ],
            cancellationToken: cancellationToken);

        return response.Text ?? string.Empty;
    }
}
