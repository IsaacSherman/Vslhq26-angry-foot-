using System.Text.RegularExpressions;
using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Application.Evidence.Diagnostics;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Review;

/// <summary>
/// What can be checked about a resume as a document rather than about its bullets one at a time.
/// Pure: no database, no AI, no clock.
/// </summary>
/// <remarks>
/// Everything here is a fact about the text that the reader can confirm by looking. Nothing judges
/// the work described, and nothing guesses at intent - a resume with no education section may have
/// left it out on purpose, so that is a note and never a warning.
/// </remarks>
internal static partial class ResumeDocumentHeuristics
{
    /// <summary>Long enough that a reader's attention runs out before the bullet does. The same
    /// figure the bullet quality panel treats as advisory, so the two never disagree.</summary>
    private const int LongBulletWordCount = 45;

    /// <summary>Contact details sit in the header. Looking further finds a former employer's
    /// address and calls it the candidate's.</summary>
    private const int HeaderLineCount = 15;

    [GeneratedRegex(@"\b\d{1,2}/\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex NumericMonthYear();

    [GeneratedRegex(@"\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+\d{4}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex NamedMonthYear();

    /// <summary>Any four-digit year, however it is written. Deliberately not "a year with no month
    /// attached": 09/2019 has no bare year, and skipping those lines would hide exactly the mixture
    /// this check exists to find.</summary>
    [GeneratedRegex(@"\b\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex AnyYear();

    private static readonly string[] ExperienceHeadings =
        ["experience", "employment", "work history", "professional history", "career"];

    public static IReadOnlyList<CoverageDiagnosticDto> Check(string resumeText, IReadOnlyList<Bullet> bullets)
    {
        var lines = resumeText
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        List<CoverageDiagnosticDto> diagnostics = [];
        diagnostics.AddRange(CheckContactDetails(lines));
        diagnostics.AddRange(CheckDateFormats(lines));
        diagnostics.AddRange(CheckExperienceSection(lines, bullets));
        diagnostics.AddRange(CheckBulletLength(bullets));
        return diagnostics;
    }

    private static IEnumerable<CoverageDiagnosticDto> CheckContactDetails(IReadOnlyList<string> lines)
    {
        if (lines.Take(HeaderLineCount).Any(ResumeBulletParser.IsContactLine))
        {
            yield break;
        }

        yield return Document(
            DiagnosticSeverityDto.Warning,
            CoverageDiagnosticCodes.MissingContactInfo,
            "The top of this resume has no email address, phone number, or profile link.",
            "A reader who wants to reply has nothing to reply to. This checks the first "
                + $"{HeaderLineCount} non-empty lines, so contact details further down would not be found here either - "
                + "and a reader skimming the header would not find them either.",
            ["An email address, phone number, or profile URL in the header."]);
    }

    private static IEnumerable<CoverageDiagnosticDto> CheckDateFormats(IReadOnlyList<string> lines)
    {
        var dated = lines.Where(line => AnyYear().IsMatch(line)).ToArray();
        if (dated.Length < 2)
        {
            yield break;
        }

        // Counted over the whole document rather than per line: one line reading "May 2016" and
        // another "09/2019" is the inconsistency, and neither line is wrong by itself. Only the two
        // month styles count - "2021 - Present" alongside "May 2016" is normal on real resumes, and
        // a check that fires on almost every document teaches the reader to skip it.
        List<string> styles = [];
        if (dated.Any(line => NumericMonthYear().IsMatch(line)))
        {
            styles.Add("numeric months (09/2019)");
        }

        if (dated.Any(line => NamedMonthYear().IsMatch(line)))
        {
            styles.Add("named months (September 2019)");
        }

        if (styles.Count < 2)
        {
            yield break;
        }

        yield return Document(
            DiagnosticSeverityDto.Suggestion,
            CoverageDiagnosticCodes.InconsistentDates,
            $"Dates are written two ways in this resume: {string.Join(" and ", styles)}.",
            "Mixed date formats read as carelessness to a human and can confuse a parser that expects one shape.",
            ["One date format used throughout."]);
    }

    private static IEnumerable<CoverageDiagnosticDto> CheckExperienceSection(
        IReadOnlyList<string> lines,
        IReadOnlyList<Bullet> bullets)
    {
        if (bullets.Count == 0)
        {
            yield return Document(
                DiagnosticSeverityDto.Warning,
                CoverageDiagnosticCodes.NoBulletsFound,
                "No achievement bullets could be read out of this document.",
                "Either the resume describes the work in paragraphs rather than bullets, or its layout hid them. "
                    + "Everything below is about the document, because there were no bullets to look at.",
                ["Achievement lines, one per accomplishment."]);
            yield break;
        }

        if (lines.Any(line => ExperienceHeadings.Any(heading =>
                line.Contains(heading, StringComparison.OrdinalIgnoreCase))))
        {
            yield break;
        }

        yield return Document(
            DiagnosticSeverityDto.Suggestion,
            CoverageDiagnosticCodes.MissingSection,
            "No heading names the work-history section.",
            "The bullets were found, so the section is there. A reader scanning for it - and a parser "
                + "splitting the document into sections - is looking for the word.",
            ["A heading such as \"Experience\" or \"Work History\" above the roles."]);
    }

    private static IEnumerable<CoverageDiagnosticDto> CheckBulletLength(IReadOnlyList<Bullet> bullets)
    {
        var wordy = bullets
            .Where(bullet => BulletQualityHeuristics.WordCount(bullet.BulletText) > LongBulletWordCount)
            .OrderByDescending(bullet => BulletQualityHeuristics.WordCount(bullet.BulletText))
            .Select(bullet => new CoverageDiagnosticDto(
                DiagnosticSeverityDto.Suggestion,
                CoverageDiagnosticCodes.LongBullet,
                $"\"{DiagnosticBudget.Excerpt(bullet)}\" runs to {BulletQualityHeuristics.WordCount(bullet.BulletText)} words.",
                EvidenceMappings.AboutBullets(
                    [bullet],
                    $"Past about {LongBulletWordCount} words a bullet stops being scanned and starts being skipped. "
                        + "Length costs nothing when it is carrying evidence, which is why this is a note rather than a rule.",
                    ["The same accomplishment in fewer words, or split into two bullets."]),
                [bullet.Id]))
            .ToArray();

        return DiagnosticBudget.Cap(wordy, remaining => $"{remaining} more bullets run past {LongBulletWordCount} words.");
    }

    /// <summary>A finding about the document, which has no bullet to cite.</summary>
    private static CoverageDiagnosticDto Document(
        DiagnosticSeverityDto severity,
        string code,
        string message,
        string reasoning,
        IReadOnlyList<string> missingEvidence)
        => new(severity, code, message, EvidenceMappings.AboutBullets([], reasoning, missingEvidence), []);
}
