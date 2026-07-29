using AngryFoot.ApiService.Ai;
using AngryFoot.Contracts;
using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Application.Generation;

public sealed class HeuristicJobAnalyzer(IChatClient chatClient) : IJobAnalyzer
{
    private static readonly string[] KnownTechnologies =
    [
        ".net", "c#", "asp.net", "blazor", "azure", "sql", "sqlite", "postgresql", "mysql", "python", "java", "javascript", "typescript", "react", "angular", "docker", "kubernetes", "aws", "gcp", "github", "git"
    ];

    private static readonly HashSet<string> Stopwords =
    [
        "the", "and", "with", "that", "from", "this", "will", "have", "your", "you", "our", "for", "are", "has", "was", "were", "into", "their", "role", "team", "work", "experience"
    ];

    public async Task<JobAnalysisDto> AnalyzeAsync(string jobDescription, CancellationToken cancellationToken)
    {
        var fallback = AnalyzeHeuristically(jobDescription);

        var systemPrompt = "You analyze job descriptions. Return strict JSON with requiredSkills, preferredSkills, technologies, keywords, experienceThemes, inferredTitle, inferredSeniority. Do not include markdown.";
        var userPrompt = $"Job description:\n{jobDescription}";

        try
        {
            var text = await chatClient.GetTextResponseAsync(systemPrompt, userPrompt, cancellationToken);
            if (AiJsonUtilities.TryDeserialize<JobAnalysisDto>(text, out var aiResult) && aiResult is not null)
            {
                return Normalize(aiResult);
            }
        }
        catch
        {
        }

        return fallback;
    }

    private static JobAnalysisDto AnalyzeHeuristically(string jobDescription)
    {
        var lower = jobDescription.ToLowerInvariant();
        var lines = jobDescription
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var required = lines
            .Where(line => line.Contains("required", StringComparison.OrdinalIgnoreCase)
                || line.Contains("must", StringComparison.OrdinalIgnoreCase)
                || line.Contains("requirements", StringComparison.OrdinalIgnoreCase))
            .SelectMany(SplitTerms)
            .Take(12)
            .ToArray();

        var preferred = lines
            .Where(line => line.Contains("preferred", StringComparison.OrdinalIgnoreCase)
                || line.Contains("nice to have", StringComparison.OrdinalIgnoreCase)
                || line.Contains("plus", StringComparison.OrdinalIgnoreCase))
            .SelectMany(SplitTerms)
            .Take(12)
            .ToArray();

        var technologies = KnownTechnologies
            .Where(t => lower.Contains(t, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var keywords = jobDescription
            .Split([' ', '\r', '\n', ',', '.', ';', ':', '(', ')', '/', '\\', '-', '|', '"', '\''], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length >= 4)
            .Where(x => !Stopwords.Contains(x.ToLowerInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

        var themes = new List<string>();
        AddThemeIf(lower, themes, "lead", "Leadership");
        AddThemeIf(lower, themes, "architect", "Architecture");
        AddThemeIf(lower, themes, "deliver", "Delivery");
        AddThemeIf(lower, themes, "autom", "Automation");
        AddThemeIf(lower, themes, "perform", "Performance");
        AddThemeIf(lower, themes, "secur", "Security");
        AddThemeIf(lower, themes, "collabor", "Collaboration");

        var inferredTitle = lines.FirstOrDefault(x => x.Contains("engineer", StringComparison.OrdinalIgnoreCase)
            || x.Contains("developer", StringComparison.OrdinalIgnoreCase)
            || x.Contains("manager", StringComparison.OrdinalIgnoreCase));

        var inferredSeniority = lower.Contains("senior") ? "Senior" :
            lower.Contains("staff") ? "Staff" :
            lower.Contains("principal") ? "Principal" :
            lower.Contains("lead") ? "Lead" :
            null;

        return Normalize(new JobAnalysisDto(required, preferred, technologies, keywords, themes, inferredTitle, inferredSeniority));
    }

    private static IEnumerable<string> SplitTerms(string line)
    {
        return line
            .Split([',', ';', '.', ':', '/', '\\', '|', '-', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length >= 3)
            .Take(6);
    }

    private static void AddThemeIf(string source, ICollection<string> themes, string needle, string theme)
    {
        if (source.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            themes.Add(theme);
        }
    }

    private static JobAnalysisDto Normalize(JobAnalysisDto dto)
    {
        return new JobAnalysisDto(
            NormalizeValues(dto.RequiredSkills),
            NormalizeValues(dto.PreferredSkills),
            NormalizeValues(dto.Technologies),
            NormalizeValues(dto.Keywords),
            NormalizeValues(dto.ExperienceThemes),
            dto.InferredTitle?.Trim(),
            dto.InferredSeniority?.Trim());
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
}
