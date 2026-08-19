using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Application.Refinement;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Application.Generation;

/// <param name="Markdown">The recommended cover letter; the template fallback when AI is unavailable.</param>
/// <param name="Refinement">Deep-review versions, or null when the pass did not run.</param>
internal sealed record CoverLetterOutcome(string Markdown, RefinementDto? Refinement);

internal sealed class CoverLetterService(
    IChatClient chatClient,
    IDraftRefinementPipeline refinementPipeline,
    ILogger<CoverLetterService> logger)
{
    /// <param name="guidance">The candidate's clarification of their own material, if any.</param>
    public async Task<CoverLetterOutcome> BuildCoverLetterAsync(
        Domain.Profile profile,
        CoverLetterContext context,
        string? guidance,
        bool deepReview,
        CancellationToken cancellationToken)
    {
        var fallback = BuildFallbackCoverLetter(profile, context);

        var candidate = AiJsonUtilities.ToJson(new { profile.Name, profile.ProfessionalSummary });
        var selectedBullets = AiJsonUtilities.ToJson(context.Bullets.Select(x => x.Text));
        var guidanceLine = string.IsNullOrWhiteSpace(guidance)
            ? string.Empty
            : $"\nThe candidate has clarified what their experience means. Treat this as fact: {guidance.Trim()}";

        var systemPrompt = "You write concise professional cover letters in markdown. Use only provided facts. Do not fabricate experience. Keep under 350 words. Start with the salutation - no title, subject line, or heading above it.";
        var userPrompt = $"Candidate: {candidate}\nRole: {context.JobTitle}\nCompany: {context.Company}\nAnalysis: {AiJsonUtilities.ToJson(context.Analysis)}\nSelectedBullets: {selectedBullets}{guidanceLine}";

        try
        {
            var text = await chatClient.GetTextResponseAsync(systemPrompt, userPrompt, cancellationToken);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var draft = WithoutTitleHeading(text);
                if (!deepReview)
                {
                    return new CoverLetterOutcome(draft, null);
                }

                var refinement = await refinementPipeline.RefineAsync(
                    new RefinementRequest(
                        ArtifactKind: "cover letter",
                        OutputContract: "a complete cover letter in markdown, under 350 words, starting at the salutation, with no title or heading above it and no commentary around it",
                        SourceMaterial: $"Candidate: {candidate}\nRole: {context.JobTitle}\nCompany: {context.Company}\nThe candidate's selected achievements: {selectedBullets}",
                        Draft: draft,
                        // The letter's claims come from the selected bullets, so ground against
                        // those rather than against the letter's own prose.
                        GroundingQuery: string.Join(" ", context.Bullets.Select(x => x.Text)),
                        UserGuidance: guidance),
                    cancellationToken);

                return new CoverLetterOutcome(WithoutTitleHeading(refinement?.Recommended?.Text ?? draft), refinement);
            }

            logger.LogWarning("Cover letter AI response was empty. Using template fallback.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cover letter AI call failed. Using template fallback.");
        }

        return new CoverLetterOutcome(fallback, null);
    }

    /// <summary>
    /// Drops a markdown heading the model put above the salutation.
    /// <para>
    /// Measured against gpt-5.4, which opened the letter with a literal "# Cover Letter" in two runs
    /// out of three; gpt-5-mini never did. The prompt now says not to, and this is here because a
    /// prompt is a request rather than a guarantee - the letter is rendered straight into its own
    /// pane, where a title reads as part of the document the candidate is sending.
    /// </para>
    /// <para>
    /// Only a heading at the very top is removed, and only one: a letter has no reason to open with
    /// one, but a heading further down is the model structuring its own prose and is not ours to
    /// rewrite.
    /// </para>
    /// </summary>
    private static string WithoutTitleHeading(string markdown)
    {
        var text = markdown.Trim();
        if (!text.StartsWith('#'))
        {
            return text;
        }

        var lineBreak = text.IndexOf('\n');
        return lineBreak < 0 ? string.Empty : text[(lineBreak + 1)..].TrimStart();
    }

    private static string BuildFallbackCoverLetter(Domain.Profile profile, CoverLetterContext context)
    {
        var greeting = string.IsNullOrWhiteSpace(context.Company)
            ? "Dear Hiring Team,"
            : $"Dear {context.Company} Hiring Team,";

        var role = string.IsNullOrWhiteSpace(context.JobTitle) ? "this role" : context.JobTitle;
        var candidateName = string.IsNullOrWhiteSpace(profile.Name) ? "Candidate" : profile.Name.Trim();
        var summary = string.IsNullOrWhiteSpace(profile.ProfessionalSummary)
            ? "I bring hands-on experience delivering measurable outcomes across engineering initiatives."
            : profile.ProfessionalSummary.Trim();

        var highlights = context.Bullets.Take(3).Select(x => $"- {x.Text}").ToArray();
        var highlightText = highlights.Length == 0
            ? "- I align execution with business goals and measurable impact."
            : string.Join(Environment.NewLine, highlights);

        return string.Join(Environment.NewLine,
            greeting,
            string.Empty,
            $"I am excited to apply for {role}. {summary}",
            string.Empty,
            "Relevant highlights:",
            highlightText,
            string.Empty,
            "I would welcome the opportunity to discuss how my background aligns with your needs.",
            string.Empty,
            $"Sincerely,{Environment.NewLine}{candidateName}");
    }
}
