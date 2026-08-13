using System.Text.RegularExpressions;
using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Application.Refinement;
using AngryFoot.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AngryFoot.ApiService.Application.Bullets;

public interface IBulletRewriteAssistant
{
    /// <param name="deepReview">
    /// Run the critique-and-revise pass over the AI rewrite. Ignored when the rewrite falls back to
    /// heuristics: there is no AI draft to critique.
    /// </param>
    Task<RewriteBulletResponse> RewriteAsync(string bulletText, bool deepReview, CancellationToken cancellationToken);
}

internal sealed partial class BulletRewriteAssistant(
    IChatClient chatClient,
    IDraftRefinementPipeline refinementPipeline,
    ILogger<BulletRewriteAssistant> logger) : IBulletRewriteAssistant
{
    private sealed record RewritePayload(string RewrittenText, IReadOnlyList<string> Suggestions);

    public async Task<RewriteBulletResponse> RewriteAsync(string bulletText, bool deepReview, CancellationToken cancellationToken)
    {
        var trimmed = bulletText.Trim();
        var fallback = CreateFallback(trimmed);

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return fallback;
        }

        var systemPrompt = "You improve resume bullets while preserving factual truth. Do not invent technologies, metrics, employers, timelines, scope, or outcomes. Return strict JSON object with fields: rewrittenText (string), suggestions (string[]). Suggestions should highlight missing impact/metrics/context if relevant.";
        var userPrompt = $"Bullet: {trimmed}";

        try
        {
            var text = await chatClient.GetTextResponseAsync(systemPrompt, userPrompt, cancellationToken);
            if (!AiJsonUtilities.TryDeserialize<RewritePayload>(text, out var payload) || payload is null)
            {
                logger.LogWarning("Bullet rewrite assistant AI response could not be parsed as JSON. Using heuristic fallback.");
                return fallback;
            }

            var rewritten = string.IsNullOrWhiteSpace(payload.RewrittenText) ? trimmed : payload.RewrittenText.Trim();
            var suggestions = NormalizeSuggestions(payload.Suggestions);
            if (suggestions.Count == 0)
            {
                suggestions = fallback.Suggestions.ToList();
            }

            if (!deepReview)
            {
                return new RewriteBulletResponse(rewritten, suggestions);
            }

            var refinement = await refinementPipeline.RefineAsync(
                new RefinementRequest(
                    ArtifactKind: "resume bullet",
                    OutputContract: "a single resume bullet as plain text - one sentence or clause, no bullet marker, no markdown, no surrounding quotes",
                    SourceMaterial: $"The bullet the candidate actually wrote: {trimmed}",
                    Draft: rewritten,
                    // Retrieve against what the candidate wrote, not the AI's rewrite, so the
                    // grounding is not steered by whatever the draft embellished.
                    GroundingQuery: trimmed),
                cancellationToken);

            // The recommended version becomes RewrittenText so callers that ignore the version
            // list - the MCP tool, older clients - still get the benefit of the pass.
            return new RewriteBulletResponse(
                refinement?.Recommended?.Text ?? rewritten,
                suggestions,
                refinement);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bullet rewrite assistant AI call failed. Using heuristic fallback.");
            return fallback;
        }
    }

    private static RewriteBulletResponse CreateFallback(string bulletText)
    {
        var suggestions = new List<string>();

        if (!ImpactPattern().IsMatch(bulletText))
        {
            suggestions.Add("Add a measurable result (percentage, time saved, cost reduction, or volume)." );
        }

        if (!ContainsOutcomeKeyword(bulletText))
        {
            suggestions.Add("Include business impact (customer value, reliability, delivery speed, or quality)." );
        }

        if (!ContainsTechnologyKeyword(bulletText))
        {
            suggestions.Add("Consider naming key tools/technologies used when appropriate.");
        }

        var rewritten = ToProfessionalTone(bulletText);
        return new RewriteBulletResponse(rewritten, suggestions);
    }

    private static IReadOnlyList<string> NormalizeSuggestions(IReadOnlyList<string>? suggestions)
    {
        if (suggestions is null)
        {
            return [];
        }

        return suggestions
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ContainsOutcomeKeyword(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.Contains("improv")
            || lower.Contains("increas")
            || lower.Contains("reduc")
            || lower.Contains("faster")
            || lower.Contains("quality")
            || lower.Contains("reliab")
            || lower.Contains("efficien");
    }

    private static bool ContainsTechnologyKeyword(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.Contains(".net")
            || lower.Contains("c#")
            || lower.Contains("api")
            || lower.Contains("sql")
            || lower.Contains("azure")
            || lower.Contains("blazor")
            || lower.Contains("docker")
            || lower.Contains("github");
    }

    private static string ToProfessionalTone(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        var rewritten = char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
        return rewritten.EndsWith('.') ? rewritten : rewritten + ".";
    }

    [GeneratedRegex("\\b(\\d+%|\\$?\\d+[\\d,]*(\\.\\d+)?|\\d+\\s*(x|hrs?|hours?|days?|weeks?|months?))\\b", RegexOptions.IgnoreCase)]
    private static partial Regex ImpactPattern();
}
