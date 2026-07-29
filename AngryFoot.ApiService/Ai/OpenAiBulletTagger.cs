using System.Text.RegularExpressions;
using AngryFoot.ApiService.Application.Bullets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AngryFoot.ApiService.Ai;

public sealed partial class OpenAiBulletTagger(IChatClient chatClient, ILogger<OpenAiBulletTagger> logger) : IBulletTagger
{
    private static readonly string[] KnownTechnologies =
    [
        ".net", "c#", "asp.net", "blazor", "azure", "sql", "sqlite", "postgresql", "mysql", "redis", "python", "java", "javascript", "typescript", "react", "angular", "docker", "kubernetes", "aws", "gcp", "github", "git"
    ];

    private sealed record TagResponse(
        IReadOnlyList<string> Skills,
        IReadOnlyList<string> Technologies,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> JobCategories,
        IReadOnlyList<string> Impact);

    public async Task<BulletTagging> TagAsync(string bulletText, CancellationToken cancellationToken)
    {
        var fallback = TagHeuristically(bulletText);
        var systemPrompt = "You extract metadata from resume bullets. Return strict JSON with arrays: skills, technologies, tags, jobCategories, impact. Use only information grounded in the bullet.";
        var userPrompt = $"Bullet: {bulletText}";

        try
        {
            var responseText = await chatClient.GetTextResponseAsync(systemPrompt, userPrompt, cancellationToken);
            if (AiJsonUtilities.TryDeserialize<TagResponse>(responseText, out var parsed) && parsed is not null)
            {
                var aiResult = Normalize(new BulletTagging(
                    parsed.Tags ?? [],
                    parsed.Skills ?? [],
                    parsed.Technologies ?? [],
                    parsed.JobCategories ?? [],
                    parsed.Impact ?? []));

                return HasMetadata(aiResult) ? aiResult : fallback;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bullet enrichment AI call failed. Using heuristic fallback.");
        }

        return fallback;
    }

    private static BulletTagging TagHeuristically(string bulletText)
    {
        var lower = bulletText.ToLowerInvariant();

        var technologies = KnownTechnologies
            .Where(t => lower.Contains(t, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var skills = new List<string>();
        AddIf(lower, skills, "autom", "Automation");
        AddIf(lower, skills, "validat", "Validation");
        AddIf(lower, skills, "review", "Quality Assurance");
        AddIf(lower, skills, "api", "API Design");
        AddIf(lower, skills, "architect", "Architecture");
        AddIf(lower, skills, "perform", "Performance Optimization");
        AddIf(lower, skills, "test", "Testing");
        AddIf(lower, skills, "deploy", "Delivery");
        AddIf(lower, skills, "lead", "Leadership");
        AddIf(lower, skills, "mentor", "Mentorship");
        AddIf(lower, skills, "process", "Process Improvement");

        var tags = new List<string>();
        AddIf(lower, tags, "implement", "Implementation");
        AddIf(lower, tags, "design", "Design");
        AddIf(lower, tags, "build", "Delivery");
        AddIf(lower, tags, "reduc", "Impact");
        AddIf(lower, tags, "increas", "Impact");
        AddIf(lower, tags, "improv", "Impact");

        if (ImpactPattern().IsMatch(bulletText))
        {
            tags.Add("Quantified Results");
        }

        var categories = new List<string>();
        AddIf(lower, categories, "backend", "Backend Engineering");
        AddIf(lower, categories, "frontend", "Frontend Engineering");
        AddIf(lower, categories, "api", "Backend Engineering");
        AddIf(lower, categories, "data", "Data Engineering");
        AddIf(lower, categories, "cloud", "Cloud Engineering");
        AddIf(lower, categories, "devops", "DevOps");
        AddIf(lower, categories, "test", "Quality Engineering");

        var impact = ImpactPattern()
            .Matches(bulletText)
            .Select(x => x.Value.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Normalize(new BulletTagging(tags, skills, technologies, categories, impact));
    }

    private static void AddIf(string source, ICollection<string> values, string token, string value)
    {
        if (source.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    private static bool HasMetadata(BulletTagging tagging)
    {
        return tagging.Tags.Count > 0
            || tagging.Skills.Count > 0
            || tagging.Technologies.Count > 0
            || tagging.JobCategories.Count > 0
            || tagging.Impact.Count > 0;
    }

    private static BulletTagging Normalize(BulletTagging tagging)
    {
        return new BulletTagging(
            NormalizeValues(tagging.Tags),
            NormalizeValues(tagging.Skills),
            NormalizeValues(tagging.Technologies),
            NormalizeValues(tagging.JobCategories),
            NormalizeValues(tagging.Impact));
    }

    private static IReadOnlyList<string> NormalizeValues(IReadOnlyList<string> values)
    {
        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToArray();
    }

    [GeneratedRegex(@"\b(\d+%|\d+[\+]?)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex ImpactPattern();
}
