using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Generation;

internal sealed class ResumeMarkdownService
{
    public string BuildResume(Domain.Profile profile, JobAnalysisDto analysis, IReadOnlyList<RewrittenBullet> bullets)
    {
        var lines = new List<string>
        {
            $"# {ValueOrPlaceholder(profile.Name, "Candidate Name")}",
            string.Empty,
            BuildContactLine(profile),
            string.Empty,
            "## Professional Summary",
            string.Empty,
            ValueOrPlaceholder(profile.ProfessionalSummary, "Professional summary to be provided."),
            string.Empty,
            "## Skills",
            string.Empty,
            BuildSkillsLine(analysis, bullets),
            string.Empty,
            "## Professional Experience",
            string.Empty
        };

        var remaining = bullets.ToList();
        foreach (var work in profile.WorkHistory.OrderBy(x => x.SortOrder))
        {
            lines.Add($"### {ValueOrPlaceholder(work.Employer, "Employer")}");
            var mapped = remaining
                .Where(x => !string.IsNullOrWhiteSpace(x.Bullet.SourceEmployer)
                    && string.Equals(x.Bullet.SourceEmployer, work.Employer, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (mapped.Length == 0)
            {
                lines.Add("- Experience details pending.");
            }
            else
            {
                foreach (var item in mapped)
                {
                    lines.Add($"- {item.Text}");
                    remaining.Remove(item);
                }
            }

            lines.Add(string.Empty);
        }

        if (remaining.Count > 0)
        {
            lines.Add("### Selected Experience");
            foreach (var item in remaining)
            {
                lines.Add($"- {item.Text}");
            }
            lines.Add(string.Empty);
        }

        lines.Add("## Education");
        lines.Add(string.Empty);
        if (profile.Education.Count == 0)
        {
            lines.Add("- Education details pending.");
        }
        else
        {
            foreach (var education in profile.Education.OrderBy(x => x.SortOrder))
            {
                var credential = string.IsNullOrWhiteSpace(education.Credential) ? string.Empty : $", {education.Credential}";
                var field = string.IsNullOrWhiteSpace(education.Field) ? string.Empty : $" ({education.Field})";
                var gradDate = string.IsNullOrWhiteSpace(education.GraduationDate) ? string.Empty : $" - {education.GraduationDate}";
                lines.Add($"- {education.Institution}{credential}{field}{gradDate}");
            }
        }

        lines.Add(string.Empty);
        lines.Add("## Certifications");
        lines.Add(string.Empty);
        if (profile.Certifications.Count == 0)
        {
            lines.Add("- Certifications pending.");
        }
        else
        {
            foreach (var certification in profile.Certifications.OrderBy(x => x.SortOrder))
            {
                var issuer = string.IsNullOrWhiteSpace(certification.Issuer) ? string.Empty : $" ({certification.Issuer})";
                var date = string.IsNullOrWhiteSpace(certification.IssueDate) ? string.Empty : $" - {certification.IssueDate}";
                lines.Add($"- {certification.Name}{issuer}{date}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildContactLine(Domain.Profile profile)
    {
        var values = new[] { profile.Email, profile.Phone, profile.LinkedIn, profile.GitHub }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToArray();

        return values.Length == 0 ? "Contact details pending." : string.Join(" | ", values);
    }

    private static string BuildSkillsLine(JobAnalysisDto analysis, IReadOnlyList<RewrittenBullet> bullets)
    {
        var skills = bullets
            .SelectMany(x => x.Bullet.Skills)
            .Concat(bullets.SelectMany(x => x.Bullet.Technologies))
            .Concat(analysis.RequiredSkills)
            .Concat(analysis.Technologies)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();

        return skills.Length == 0 ? "Skills pending." : string.Join(", ", skills);
    }

    private static string ValueOrPlaceholder(string? value, string placeholder)
    {
        return string.IsNullOrWhiteSpace(value) ? placeholder : value.Trim();
    }
}
