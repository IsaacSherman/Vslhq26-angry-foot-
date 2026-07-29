using AngryFoot.ApiService.Application.Bullets;
using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Ai;

public sealed class OpenAiBulletTagger(IChatClient chatClient) : IBulletTagger
{
    private sealed record TagResponse(
        IReadOnlyList<string> Skills,
        IReadOnlyList<string> Technologies,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> JobCategories,
        IReadOnlyList<string> Impact);

    public async Task<BulletTagging> TagAsync(string bulletText, CancellationToken cancellationToken)
    {
        var systemPrompt = "You extract metadata from resume bullets. Return strict JSON with arrays: skills, technologies, tags, jobCategories, impact. Use only information grounded in the bullet.";
        var userPrompt = $"Bullet: {bulletText}";

        try
        {
            var responseText = await chatClient.GetTextResponseAsync(systemPrompt, userPrompt, cancellationToken);
            if (AiJsonUtilities.TryDeserialize<TagResponse>(responseText, out var parsed) && parsed is not null)
            {
                return new BulletTagging(
                    parsed.Tags ?? [],
                    parsed.Skills ?? [],
                    parsed.Technologies ?? [],
                    parsed.JobCategories ?? [],
                    parsed.Impact ?? []);
            }
        }
        catch
        {
        }

        return new BulletTagging([], [], [], [], []);
    }
}
