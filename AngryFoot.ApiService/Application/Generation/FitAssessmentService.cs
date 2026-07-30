using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Data;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Application.Generation;

public interface IFitAssessor
{
    Task<FitAssessmentDto> AssessAsync(string jobDescription, JobAnalysisDto analysis, CancellationToken cancellationToken);
}

internal sealed class FitAssessmentService(
    AngryFootDbContext dbContext,
    IChatClient chatClient,
    ILogger<FitAssessmentService> logger) : IFitAssessor
{
    private const int MaxListItems = 8;
    private const int MaxBulletsSentToAi = 60;

    private sealed record FitPayload(
        int? FitScore,
        string? Verdict,
        IReadOnlyList<string>? Strengths,
        IReadOnlyList<string>? Gaps,
        IReadOnlyList<string>? BulletSuggestions);

    public async Task<FitAssessmentDto> AssessAsync(string jobDescription, JobAnalysisDto analysis, CancellationToken cancellationToken)
    {
        var bullets = await dbContext.Bullets.AsNoTracking().ToListAsync(cancellationToken);
        var profile = await dbContext.Profiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

        var fallback = AssessHeuristically(analysis, bullets);

        if (bullets.Count == 0)
        {
            // With no evidence to ground the AI, the heuristic's "empty library" answer is more honest.
            return fallback;
        }

        var systemPrompt =
            "You are a blunt but fair career coach assessing how qualified a candidate is for a specific job. " +
            "Base your assessment ONLY on the candidate's achievement bullets and profile summary provided - never assume unstated experience. " +
            "Do not restate the job description; analyze the candidate's chances. " +
            "Return strict JSON with fields: " +
            "fitScore (integer 0-100 for how qualified the candidate is), " +
            "verdict (2-3 direct sentences on the candidate's realistic chances for this role), " +
            "strengths (string[], each naming a job requirement the candidate demonstrably meets, citing the supporting evidence), " +
            "gaps (string[], each naming a job requirement where the candidate's evidence is weak or missing), " +
            "bulletSuggestions (string[], the specific new achievement bullets the candidate should write to close the highest-value gaps - describe what each bullet needs to demonstrate).";

        var userPrompt =
            $"Job description:\n{Truncate(jobDescription, 6000)}\n\n" +
            $"Extracted job requirements: {AiJsonUtilities.ToJson(analysis)}\n\n" +
            $"Candidate profile summary: {profile?.ProfessionalSummary ?? "(none provided)"}\n\n" +
            $"Candidate achievement bullets: {AiJsonUtilities.ToJson(bullets.Take(MaxBulletsSentToAi).Select(b => new { text = b.BulletText, skills = b.Skills, technologies = b.Technologies }))}";

        try
        {
            var text = await chatClient.GetTextResponseAsync(systemPrompt, userPrompt, cancellationToken);
            if (AiJsonUtilities.TryDeserialize<FitPayload>(text, out var payload) && payload is not null)
            {
                var result = Normalize(payload, fallback);
                if (result is not null)
                {
                    return result;
                }
            }

            logger.LogWarning("Fit assessment AI response could not be parsed. Using heuristic fallback.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Fit assessment AI call failed. Using heuristic fallback.");
        }

        return fallback;
    }

    private static FitAssessmentDto? Normalize(FitPayload payload, FitAssessmentDto fallback)
    {
        var strengths = NormalizeValues(payload.Strengths);
        var gaps = NormalizeValues(payload.Gaps);
        var suggestions = NormalizeValues(payload.BulletSuggestions);
        var verdict = payload.Verdict?.Trim();

        // A payload with no verdict and no content is a failed response, not an assessment.
        if (string.IsNullOrWhiteSpace(verdict) && strengths.Count == 0 && gaps.Count == 0 && suggestions.Count == 0)
        {
            return null;
        }

        return new FitAssessmentDto(
            Math.Clamp(payload.FitScore ?? fallback.FitScore, 0, 100),
            string.IsNullOrWhiteSpace(verdict) ? fallback.Verdict : verdict,
            strengths,
            gaps,
            suggestions.Count > 0 ? suggestions : fallback.BulletSuggestions);
    }

    private static FitAssessmentDto AssessHeuristically(JobAnalysisDto analysis, IReadOnlyList<Bullet> bullets)
    {
        // Required skills and technologies weigh double; preferred skills weigh single.
        var requirements = analysis.RequiredSkills.Concat(analysis.Technologies)
            .Select(term => (Term: term, Weight: 2))
            .Concat(analysis.PreferredSkills.Select(term => (Term: term, Weight: 1)))
            .GroupBy(x => x.Term, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Term: g.Key, Weight: g.Max(x => x.Weight)))
            .ToList();

        var topRequirements = requirements
            .OrderByDescending(x => x.Weight)
            .Select(x => x.Term)
            .Take(5)
            .ToList();

        if (bullets.Count == 0)
        {
            var target = topRequirements.Count > 0 ? string.Join(", ", topRequirements) : "the role's core requirements";
            return new FitAssessmentDto(
                0,
                $"Your bullet library is empty, so there is no evidence to assess against this role. Start by writing bullets that demonstrate: {target}.",
                [],
                topRequirements.Select(t => $"{t} - no supporting bullets in your library").ToArray(),
                topRequirements.Select(t => $"Write a bullet demonstrating hands-on {t} work with a measurable outcome.").ToArray());
        }

        if (requirements.Count == 0)
        {
            return new FitAssessmentDto(
                50,
                "No clear requirements could be extracted from this job description, so your fit cannot be scored. Review the posting manually and check that the description text is complete.",
                [],
                [],
                []);
        }

        var covered = new List<(string Term, int Weight, int SupportCount)>();
        var uncovered = new List<(string Term, int Weight)>();

        foreach (var (term, weight) in requirements)
        {
            var supportCount = bullets.Count(b => BulletSupports(b, term));
            if (supportCount > 0)
            {
                covered.Add((term, weight, supportCount));
            }
            else
            {
                uncovered.Add((term, weight));
            }
        }

        var totalWeight = requirements.Sum(x => x.Weight);
        var score = (int)Math.Round(100.0 * covered.Sum(x => x.Weight) / totalWeight);

        var strengths = covered
            .OrderByDescending(x => x.Weight)
            .ThenByDescending(x => x.SupportCount)
            .Take(MaxListItems)
            .Select(x => $"{x.Term} - backed by {x.SupportCount} bullet{(x.SupportCount == 1 ? "" : "s")}")
            .ToArray();

        var gaps = uncovered
            .OrderByDescending(x => x.Weight)
            .Take(MaxListItems)
            .Select(x => $"{x.Term} - no supporting bullets in your library")
            .ToArray();

        var suggestions = uncovered
            .OrderByDescending(x => x.Weight)
            .Take(5)
            .Select(x => $"Write a bullet demonstrating hands-on {x.Term} work with a measurable outcome.")
            .ToArray();

        var verdict = score switch
        {
            >= 75 => $"Strong match: your library covers {covered.Count} of {requirements.Count} extracted requirements. Focus on tailoring existing bullets rather than writing new material.",
            >= 50 => $"Competitive with gaps: you cover {covered.Count} of {requirements.Count} extracted requirements. Closing the top gaps below would meaningfully improve your odds.",
            >= 25 => $"Stretch role: you cover only {covered.Count} of {requirements.Count} extracted requirements. Expect your application to hinge on the gaps listed below.",
            _ => $"Weak match today: you cover {covered.Count} of {requirements.Count} extracted requirements. Consider building experience in the gap areas before prioritizing this role."
        };

        return new FitAssessmentDto(score, verdict, strengths, gaps, suggestions);
    }

    private static bool BulletSupports(Bullet bullet, string term)
    {
        return bullet.BulletText.Contains(term, StringComparison.OrdinalIgnoreCase)
            || bullet.Skills.Any(s => s.Contains(term, StringComparison.OrdinalIgnoreCase))
            || bullet.Technologies.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> NormalizeValues(IReadOnlyList<string>? values)
    {
        return (values ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
            .ToArray();
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
