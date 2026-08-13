using AngryFoot.ApiService.Ai;
using AngryFoot.Contracts;
using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Application.Refinement;

internal interface IDraftRefinementPipeline
{
    /// <summary>
    /// Runs the critique-and-revise pass over an AI draft. Returns null when the pass produced
    /// nothing worth showing, in which case the caller keeps its original draft.
    /// </summary>
    Task<RefinementDto?> RefineAsync(RefinementRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Three agents in sequence over one draft: a critic that reviews it and writes its own
/// alternative, the original author revising against the critique alone, and an arbiter that
/// merges the results. The author never sees the critic's alternative, so its revision is a
/// genuine second attempt rather than a copy.
/// </summary>
/// <remarks>
/// Costs three AI calls on top of the draft, which is why every caller gates this behind an
/// explicit opt-in. Each stage degrades independently: a failed stage drops its version and the
/// rest of the pass continues, and a failed critique abandons the pass entirely since the two
/// later stages have nothing to work from.
/// </remarks>
internal sealed class DraftRefinementPipeline(
    IChatClient chatClient,
    IRefinementGrounding grounding,
    ILogger<DraftRefinementPipeline> logger) : IDraftRefinementPipeline
{
    private sealed record CritiquePayload(string Critique, string Alternative);
    private sealed record RevisionPayload(string Revised);
    private sealed record SynthesisPayload(string Merged, string Rationale);

    public async Task<RefinementDto?> RefineAsync(RefinementRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Draft))
        {
            return null;
        }

        var context = await grounding.BuildContextAsync(request.GroundingQuery ?? request.Draft, cancellationToken);

        var critique = await CritiqueAsync(request, context, cancellationToken);
        if (critique is null)
        {
            return null;
        }

        var versions = new List<DraftVersionDto>
        {
            new(
                DraftVersionLabels.InitialDraft,
                "Initial draft",
                "The first draft, exactly what you would get without deep review.",
                request.Draft)
        };

        if (!string.IsNullOrWhiteSpace(critique.Alternative))
        {
            versions.Add(new DraftVersionDto(
                DraftVersionLabels.CriticAlternative,
                "Reviewer's alternative",
                "Written from scratch by the reviewing agent after critiquing the draft.",
                critique.Alternative.Trim()));
        }

        var revised = await ReviseAsync(request, critique.Critique, cancellationToken);
        if (revised is not null)
        {
            versions.Add(new DraftVersionDto(
                DraftVersionLabels.AuthorRevision,
                "Author's revision",
                "The original author's own revision after reading the critique - but not the reviewer's alternative.",
                revised));
        }

        var synthesis = await SynthesizeAsync(request, context, critique, revised, cancellationToken);
        if (synthesis is not null)
        {
            versions.Add(new DraftVersionDto(
                DraftVersionLabels.Synthesis,
                "Synthesis",
                synthesis.Value.Rationale,
                synthesis.Value.Text));
        }

        // Never recommend the reviewer's alternative on its own: it is the least-vetted version,
        // written by an agent that only saw the draft and never had its own work reviewed.
        var recommended = synthesis is not null
            ? DraftVersionLabels.Synthesis
            : revised is not null
                ? DraftVersionLabels.AuthorRevision
                : DraftVersionLabels.InitialDraft;

        return new RefinementDto(recommended, critique.Critique, versions);
    }

    private async Task<CritiquePayload?> CritiqueAsync(RefinementRequest request, string context, CancellationToken cancellationToken)
    {
        var systemPrompt = $$"""
            You are an exacting resume editor reviewing another writer's draft {{request.ArtifactKind}}. Your job is to find what is wrong with it, not to praise it.
            Look for: claims the source material does not support (invented technologies, metrics, employers, scope, or outcomes), vague or generic language, missed opportunities to quantify impact, tone problems, and filler a hiring manager would skim past.
            Then write your own alternative that fixes what you found. Your alternative must be {{request.OutputContract}}, and every claim in it must be supported by the source material or the candidate's library.
            Return strict JSON: {"critique": string, "alternative": string}. Keep the critique to specific, actionable points.
            """;

        var userPrompt = $"""
            Source material:
            {request.SourceMaterial}

            {FormatContext(context)}

            Draft to critique:
            {request.Draft}
            """;

        var payload = await CallAsync<CritiquePayload>(systemPrompt, userPrompt, "critique", cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Critique))
        {
            logger.LogWarning("Deep review produced no usable critique. Returning the original draft.");
            return null;
        }

        return payload with { Critique = payload.Critique.Trim() };
    }

    /// <summary>
    /// Deliberately shows the author the critique only. Handing over the reviewer's alternative
    /// here would collapse the two versions into one and waste the comparison.
    /// </summary>
    private async Task<string?> ReviseAsync(RefinementRequest request, string critique, CancellationToken cancellationToken)
    {
        var systemPrompt = $$"""
            You wrote the draft {{request.ArtifactKind}} below, and an editor has sent you their critique of it.
            Revise your draft to address every point you agree with, and leave alone anything the editor got wrong. Do not invent facts that are not in the source material.
            Your revision must be {{request.OutputContract}}.
            Return strict JSON: {"revised": string}.
            """;

        var userPrompt = $"""
            Source material:
            {request.SourceMaterial}

            Your draft:
            {request.Draft}

            Editor's critique:
            {critique}
            """;

        var payload = await CallAsync<RevisionPayload>(systemPrompt, userPrompt, "revision", cancellationToken);
        return string.IsNullOrWhiteSpace(payload?.Revised) ? null : payload.Revised.Trim();
    }

    private async Task<(string Text, string Rationale)?> SynthesizeAsync(
        RefinementRequest request,
        string context,
        CritiquePayload critique,
        string? revised,
        CancellationToken cancellationToken)
    {
        var systemPrompt = $$"""
            You are the final arbiter of resume language: a hiring-side judge who has read thousands of resumes and can tell substance from padding.
            You are given the same {{request.ArtifactKind}} written several ways, plus the critique that produced them. Weigh them against the source material and the candidate's library, then merge them into one version that takes the strongest element of each and keeps only claims the source supports.
            Prefer concrete, verifiable specifics over confident-sounding generalities. The merged version must be {{request.OutputContract}}.
            Return strict JSON: {"merged": string, "rationale": string}, where rationale is one sentence on what you took from where.
            """;

        var candidates = new List<string>
        {
            $"Version v1 (initial draft):\n{request.Draft}"
        };

        if (!string.IsNullOrWhiteSpace(critique.Alternative))
        {
            candidates.Add($"Version v2 (reviewer's alternative):\n{critique.Alternative.Trim()}");
        }

        if (revised is not null)
        {
            candidates.Add($"Version v1a (author's revision after the critique):\n{revised}");
        }

        var userPrompt = $"""
            Source material:
            {request.SourceMaterial}

            {FormatContext(context)}

            {string.Join(Environment.NewLine + Environment.NewLine, candidates)}

            Editor's critique of v1:
            {critique.Critique}
            """;

        var payload = await CallAsync<SynthesisPayload>(systemPrompt, userPrompt, "synthesis", cancellationToken);
        if (string.IsNullOrWhiteSpace(payload?.Merged))
        {
            return null;
        }

        var rationale = string.IsNullOrWhiteSpace(payload.Rationale)
            ? "Merged by the arbiter agent from the draft, the reviewer's alternative, and the author's revision."
            : payload.Rationale.Trim();

        return (payload.Merged.Trim(), rationale);
    }

    private async Task<T?> CallAsync<T>(string systemPrompt, string userPrompt, string stage, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var text = await chatClient.GetTextResponseAsync(systemPrompt, userPrompt, cancellationToken);
            if (AiJsonUtilities.TryDeserialize<T>(text, out var payload) && payload is not null)
            {
                return payload;
            }

            logger.LogWarning("Deep review {Stage} response could not be parsed as JSON. Skipping that version.", stage);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Deep review {Stage} call failed. Skipping that version.", stage);
            return null;
        }
    }

    private static string FormatContext(string context)
    {
        return string.IsNullOrWhiteSpace(context)
            ? "Candidate's bullet library: (unavailable)"
            : $"""
                Candidate's existing bullets, for grounding - treat these as the record of what the candidate has actually done:
                {context}
                """;
    }
}
