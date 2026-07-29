using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Application.Generation;

internal sealed class CoverLetterService(IChatClient chatClient)
{
    public async Task<string> BuildCoverLetterAsync(Domain.Profile profile, CoverLetterContext context, CancellationToken cancellationToken)
    {
        var fallback = BuildFallbackCoverLetter(profile, context);

        var systemPrompt = "You write concise professional cover letters in markdown. Use only provided facts. Do not fabricate experience. Keep under 350 words.";
        var userPrompt = $"Candidate: {AiJsonUtilities.ToJson(new { profile.Name, profile.ProfessionalSummary })}\nRole: {context.JobTitle}\nCompany: {context.Company}\nAnalysis: {AiJsonUtilities.ToJson(context.Analysis)}\nSelectedBullets: {AiJsonUtilities.ToJson(context.Bullets.Select(x => x.Text))}";

        try
        {
            var text = await chatClient.GetTextResponseAsync(systemPrompt, userPrompt, cancellationToken);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }
        catch
        {
        }

        return fallback;
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
