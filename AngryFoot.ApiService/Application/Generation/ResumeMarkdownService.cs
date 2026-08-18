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

            // Under the employer rather than inside the heading: a reader scans headings for
            // company names, and a heading carrying a title and a date range as well is the line
            // they have to read twice.
            if (BuildRoleLine(work) is { } roleLine)
            {
                lines.Add(roleLine);
                lines.Add(string.Empty);
            }

            var mapped = remaining
                .Where(x => !string.IsNullOrWhiteSpace(x.Bullet.SourceEmployer)
                    && string.Equals(x.Bullet.SourceEmployer, work.Employer, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            // An employer with no selected bullets keeps its heading and dates - the timeline is
            // worth printing on its own - but gets no placeholder line. Selection weights recent
            // work, so older roles routinely end up with nothing, and "Experience details pending."
            // under half a career reads as an unfinished document rather than an edited one.
            foreach (var item in mapped)
            {
                lines.Add($"- {item.Text}");
                remaining.Remove(item);
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

    /// <summary>
    /// The role and when it was held, or null when the profile records neither. Emphasised rather
    /// than made a heading so it reads as a subtitle to the employer above it.
    /// </summary>
    private static string? BuildRoleLine(WorkHistory work)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(work.Title))
        {
            parts.Add(work.Title.Trim());
        }

        if (BuildDateRange(work) is { } dates)
        {
            parts.Add(dates);
        }

        return parts.Count == 0 ? null : $"*{string.Join(" | ", parts)}*";
    }

    /// <summary>
    /// A start with no end reads as the role the candidate still holds, which is the resume
    /// convention. An end with no start is printed alone rather than guessed at.
    /// </summary>
    private static string? BuildDateRange(WorkHistory work)
    {
        var start = work.StartDate?.Trim();
        var end = work.EndDate?.Trim();

        return (string.IsNullOrWhiteSpace(start), string.IsNullOrWhiteSpace(end)) switch
        {
            (false, false) => $"{start} - {end}",
            (false, true) => $"{start} - Present",
            (true, false) => end,
            _ => null
        };
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
