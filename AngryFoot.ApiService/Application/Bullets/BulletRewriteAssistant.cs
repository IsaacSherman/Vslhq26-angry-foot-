using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Application.Refinement;
using AngryFoot.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AngryFoot.ApiService.Application.Bullets;

public interface IBulletRewriteAssistant
{
    /// <param name="deepReview">
    /// Run the critique-and-revise pass over the AI rewrite, start to finish, with no chance for
    /// the user to weigh in. Ignored when the rewrite falls back to heuristics: there is no AI
    /// draft to critique.
    /// </param>
    Task<RewriteBulletResponse> RewriteAsync(string bulletText, bool deepReview, CancellationToken cancellationToken);

    /// <summary>
    /// Phase one of a guided deep review: draft the rewrite and have it reviewed, then stop.
    /// Null when there is no AI draft to critique, or the reviewer produced nothing usable - the
    /// caller should fall back to plain <see cref="RewriteAsync"/> behaviour.
    /// </summary>
    Task<BulletRewriteCritiqueResponse?> CritiqueAsync(string bulletText, CancellationToken cancellationToken);

    /// <summary>
    /// Phase two: run the revision and synthesis stages over a phase-one result, applying the
    /// user's guidance.
    /// </summary>
    Task<RewriteBulletResponse> CompleteAsync(CompleteBulletRewriteRequest request, CancellationToken cancellationToken);
}

internal sealed class BulletRewriteAssistant(
    IChatClient chatClient,
    IDraftRefinementPipeline refinementPipeline,
    ILogger<BulletRewriteAssistant> logger) : IBulletRewriteAssistant
{
    private sealed record RewritePayload(string RewrittenText, IReadOnlyList<string> Suggestions);

    /// <summary>The AI draft, or null when the assistant had to fall back to heuristics.</summary>
    private sealed record Draft(string Text, IReadOnlyList<string> Suggestions);

    public async Task<RewriteBulletResponse> RewriteAsync(string bulletText, bool deepReview, CancellationToken cancellationToken)
    {
        var trimmed = bulletText.Trim();
        var fallback = CreateFallback(trimmed);

        var draft = await DraftAsync(trimmed, fallback, cancellationToken);
        if (draft is null)
        {
            return fallback;
        }

        if (!deepReview)
        {
            return new RewriteBulletResponse(draft.Text, draft.Suggestions);
        }

        var refinement = await refinementPipeline.RefineAsync(
            BuildRefinementRequest(trimmed, draft.Text, guidance: null),
            cancellationToken);

        // The recommended version becomes RewrittenText so callers that ignore the version
        // list - the MCP tool, older clients - still get the benefit of the pass.
        return new RewriteBulletResponse(
            refinement?.Recommended?.Text ?? draft.Text,
            draft.Suggestions,
            refinement);
    }

    public async Task<BulletRewriteCritiqueResponse?> CritiqueAsync(string bulletText, CancellationToken cancellationToken)
    {
        var trimmed = bulletText.Trim();
        var draft = await DraftAsync(trimmed, CreateFallback(trimmed), cancellationToken);
        if (draft is null)
        {
            return null;
        }

        var critique = await refinementPipeline.CritiqueAsync(
            BuildRefinementRequest(trimmed, draft.Text, guidance: null),
            cancellationToken);

        return critique is null
            ? null
            : new BulletRewriteCritiqueResponse(trimmed, draft.Text, critique.Critique, critique.Alternative, draft.Suggestions);
    }

    public async Task<RewriteBulletResponse> CompleteAsync(CompleteBulletRewriteRequest request, CancellationToken cancellationToken)
    {
        var guidance = string.IsNullOrWhiteSpace(request.Guidance) ? null : request.Guidance.Trim();

        var refinement = await refinementPipeline.CompleteAsync(
            BuildRefinementRequest(request.OriginalText.Trim(), request.Draft, guidance),
            new RefinementCritique(request.Critique, request.Alternative),
            cancellationToken);

        return new RewriteBulletResponse(
            refinement?.Recommended?.Text ?? request.Draft,
            request.Suggestions,
            refinement);
    }

    private static RefinementRequest BuildRefinementRequest(string originalText, string draft, string? guidance) =>
        new(
            ArtifactKind: "resume bullet",
            OutputContract: "a single resume bullet as plain text - one sentence or clause, no bullet marker, no markdown, no surrounding quotes",
            SourceMaterial: $"The bullet the candidate actually wrote: {originalText}",
            Draft: draft,
            // Retrieve against what the candidate wrote, not the AI's rewrite, so the grounding
            // is not steered by whatever the draft embellished.
            GroundingQuery: originalText,
            UserGuidance: guidance);

    private async Task<Draft?> DraftAsync(string trimmed, RewriteBulletResponse fallback, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        var systemPrompt = "You improve resume bullets while preserving factual truth. Do not invent technologies, metrics, employers, timelines, scope, or outcomes. Return strict JSON object with fields: rewrittenText (string), suggestions (string[]). Suggestions should highlight missing impact/metrics/context if relevant.";
        var userPrompt = $"Bullet: {trimmed}";

        try
        {
            var response = await chatClient.GetJsonResponseAsync<RewritePayload>(systemPrompt, userPrompt, cancellationToken, logger);
            var text = response.RawText;
            if (response.Value is not { } payload)
            {
                logger.LogWarning(
                    "Bullet rewrite assistant AI response could not be parsed as JSON. Using heuristic fallback. Raw response: {RawResponse}",
                    AiJsonUtilities.ForLog(text));
                return null;
            }

            var rewritten = string.IsNullOrWhiteSpace(payload.RewrittenText) ? trimmed : payload.RewrittenText.Trim();
            var suggestions = NormalizeSuggestions(payload.Suggestions);

            return new Draft(rewritten, suggestions.Count == 0 ? fallback.Suggestions : suggestions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bullet rewrite assistant AI call failed. Using heuristic fallback.");
            return null;
        }
    }

    private static RewriteBulletResponse CreateFallback(string bulletText)
    {
        var suggestions = new List<string>();

        if (!BulletQualityHeuristics.HasMeasurableImpact(bulletText))
        {
            suggestions.Add("Add a measurable result (percentage, time saved, cost reduction, or volume)." );
        }

        if (!BulletQualityHeuristics.MentionsOutcome(bulletText))
        {
            suggestions.Add("Include business impact (customer value, reliability, delivery speed, or quality)." );
        }

        if (!BulletQualityHeuristics.NamesTechnology(bulletText))
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
}
