using System.Text.RegularExpressions;

namespace AngryFoot.ApiService.Application.Bullets;

public sealed record ParsedCandidate(string Text, string? SuggestedEmployer);

/// <summary>
/// Splits pasted resume text into candidate achievement bullets. Deliberately lenient: a junk
/// candidate the user can discard in the review UI costs far less than a real achievement that
/// gets silently dropped, so the noise filters stay conservative.
/// </summary>
public static class ResumeBulletParser
{
    private const int MinimumBulletLength = 25;

    // Copy-pasting from a PDF often loses the bullet glyphs entirely, so a resume with no markers
    // anywhere falls back to treating substantial standalone lines as candidates.
    private const int MinimumUnmarkedBulletLength = 40;

    private const int MaximumHeadingLength = 80;

    // A wrapped line that starts capitalized is only treated as a continuation when it is long
    // enough that it can't plausibly be an employer or title heading.
    private const int MinimumCapitalizedContinuationLength = 40;

    private static readonly Regex MarkerPattern = new(
        @"^\s*(?:[•●▪◦‣·∙\*⁃]|[-–—]|o|\d{1,2}\s*[.)])\s+",
        RegexOptions.Compiled);

    private static readonly Regex PhonePattern = new(
        @"\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}",
        RegexOptions.Compiled);

    private static readonly Regex DateLinePattern = new(
        @"^(?:[A-Za-z]{3,9}\.?\s+)?\d{4}\s*(?:[-–—]|to)\s*(?:present|current|(?:[A-Za-z]{3,9}\.?\s+)?\d{4})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex YearOnlyPattern = new(@"^\d{4}$", RegexOptions.Compiled);

    private static readonly Regex TrailingDateRange = new(
        @"[,\s–—-]*\(?(?:[A-Za-z]{3,9}\.?\s+)?\d{4}\s*(?:[-–—]|to)\s*(?:present|current|(?:[A-Za-z]{3,9}\.?\s+)?\d{4})\)?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex AnyYear = new(@"\d{4}", RegexOptions.Compiled);

    private static readonly Regex TrailingTitleSegment = new(@"\s+-\s+.*$", RegexOptions.Compiled);

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    private static readonly char[] EmployerSeparators = ['—', '–', '|', ',', '/'];

    public static IReadOnlyList<ParsedCandidate> Parse(string? resumeText)
    {
        if (string.IsNullOrWhiteSpace(resumeText))
        {
            return [];
        }

        var lines = resumeText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var candidates = ExtractMarkedBullets(lines);

        return candidates.Count > 0 ? candidates : ExtractUnmarkedBullets(lines);
    }

    private static List<ParsedCandidate> ExtractMarkedBullets(string[] lines)
    {
        var candidates = new List<ParsedCandidate>();
        var current = new System.Text.StringBuilder();
        var employer = new EmployerTracker();
        string? candidateEmployer = null;

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            var text = Normalize(current.ToString());
            if (IsPlausibleBullet(text, MinimumBulletLength))
            {
                candidates.Add(new ParsedCandidate(text, candidateEmployer));
            }

            current.Clear();
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                Flush();
                employer.EndBlock();
                continue;
            }

            var marker = MarkerPattern.Match(trimmed);
            if (marker.Success)
            {
                Flush();
                current.Append(trimmed[marker.Length..]);
                candidateEmployer = employer.Current;
                continue;
            }

            if (current.Length > 0 && IsContinuation(trimmed, current.ToString()))
            {
                current.Append(' ').Append(trimmed);
                continue;
            }

            Flush();
            employer.Observe(trimmed);
        }

        Flush();
        return candidates;
    }

    /// <summary>
    /// Follows the employer heading above each block of bullets. Only the first heading-shaped line
    /// in a block wins, so the job title or date line that usually follows the employer
    /// ("Staff Engineer, 2021 - Present") doesn't overwrite it. A blank line or an emitted bullet
    /// opens the next block, which is what lets back-to-back roles with no blank line between them
    /// ("Client Support Administrator (2006-2010)") still register.
    /// </summary>
    private sealed class EmployerTracker
    {
        private bool _setInBlock;

        public string? Current { get; private set; }

        public void StartNewBlock() => _setInBlock = false;

        public void Observe(string line)
        {
            if (IsSectionHeading(line))
            {
                Current = null;
                _setInBlock = false;
                return;
            }

            if (_setInBlock || !TryReadEmployer(line, out var employer))
            {
                return;
            }

            Current = employer;
            _setInBlock = true;
        }
    }

    private static List<ParsedCandidate> ExtractUnmarkedBullets(string[] lines)
    {
        var candidates = new List<ParsedCandidate>();
        var current = new System.Text.StringBuilder();
        var employer = new EmployerTracker();
        string? candidateEmployer = null;

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            var text = Normalize(current.ToString());
            if (LooksLikeJobHeading(text) || !IsPlausibleBullet(text, MinimumUnmarkedBulletLength))
            {
                employer.Observe(text);
            }
            else
            {
                candidates.Add(new ParsedCandidate(text, candidateEmployer));
            }

            current.Clear();
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                Flush();
                employer.EndBlock();
                continue;
            }

            if (IsSectionHeading(trimmed) || IsContactLine(trimmed) || IsDateLine(trimmed))
            {
                Flush();
                employer.Observe(trimmed);
                continue;
            }

            // Unlike the marked path, a capitalized line is never treated as a wrap here: without
            // markers every achievement starts capitalized and few end in a period, so the
            // marked-mode wrap rule would glue an entire job block into one candidate.
            if (current.Length > 0 && IsWrappedContinuation(trimmed))
            {
                current.Append(' ').Append(trimmed);
                continue;
            }

            Flush();
            current.Append(trimmed);
            candidateEmployer = employer.Current;
        }

        Flush();
        return candidates;
    }

    private static bool IsContinuation(string line, string accumulated)
    {
        if (IsWrappedContinuation(line))
        {
            return true;
        }

        if (IsSectionHeading(line) || IsContactLine(line) || IsDateLine(line))
        {
            return false;
        }

        return !EndsSentence(accumulated) && line.Length > MinimumCapitalizedContinuationLength;
    }

    /// <summary>
    /// A line that can only be the tail of the previous one: text wrapped mid-sentence starts
    /// lowercase (or with a figure), where a new achievement almost always starts capitalized.
    /// </summary>
    private static bool IsWrappedContinuation(string line)
    {
        if (IsSectionHeading(line) || IsContactLine(line) || IsDateLine(line))
        {
            return false;
        }

        return char.IsLower(line[0]) || char.IsDigit(line[0]);
    }

    /// <summary>
    /// A job header rather than an achievement: an employer/title line ending in the dates worked,
    /// such as "Intern at Emerson Process Management  May 2014 - September 2016" or
    /// "Client Support Administrator (2006-2010)".
    /// </summary>
    private static bool LooksLikeJobHeading(string line) => TrailingDateRange.IsMatch(line);

    private static bool TryReadEmployer(string line, out string? employer)
    {
        employer = null;

        if (IsContactLine(line) || IsDateLine(line) || EndsSentence(line))
        {
            return false;
        }

        // Strip the dates worked before measuring: "Tutor at Math Department  August 2013 - August
        // 2015" is a heading, but the dates alone can push it past the length limit.
        var withoutDates = TrailingDateRange.Replace(line, string.Empty).Trim();

        // A leftover year means the line is something other than a plain employer heading; guessing
        // wrong is worse than leaving the field for the user to fill in.
        if (withoutDates.Length == 0 || withoutDates.Length > MaximumHeadingLength || AnyYear.IsMatch(withoutDates))
        {
            return false;
        }

        var segment = TrailingTitleSegment.Replace(withoutDates.Split(EmployerSeparators, 2)[0], string.Empty).Trim();
        segment = StripLeadingRole(segment);
        if (segment.Length == 0)
        {
            return false;
        }

        employer = segment;
        return true;
    }

    /// <summary>
    /// Turns "Intern at Emerson Process Management" into "Emerson Process Management" — the
    /// employer is what follows the role in this common heading shape.
    /// </summary>
    private static string StripLeadingRole(string heading)
    {
        var separator = heading.LastIndexOf(" at ", StringComparison.OrdinalIgnoreCase);
        return separator < 0 ? heading : heading[(separator + 4)..].Trim();
    }

    private static bool IsSectionHeading(string line)
    {
        var letters = line.Where(char.IsLetter).ToArray();
        if (letters.Length == 0)
        {
            return false;
        }

        if (line.Length <= MaximumHeadingLength && letters.All(char.IsUpper))
        {
            return true;
        }

        // A short label ending in a colon ("Experience:", "Programming Languages:") introduces a
        // section rather than naming an employer, so it clears the current one.
        return line.Length < MinimumUnmarkedBulletLength && line.EndsWith(':');
    }

    private static bool IsContactLine(string line)
    {
        return line.Contains('@', StringComparison.Ordinal)
            || line.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase)
            || line.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            || PhonePattern.IsMatch(line);
    }

    private static bool IsDateLine(string line)
    {
        return DateLinePattern.IsMatch(line) || YearOnlyPattern.IsMatch(line);
    }

    private static bool EndsSentence(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Length > 0 && trimmed[^1] is '.' or '!' or '?';
    }

    private static bool IsPlausibleBullet(string text, int minimumLength)
    {
        return text.Length >= minimumLength
            && text.Contains(' ', StringComparison.Ordinal)
            && !IsSectionHeading(text)
            && !IsContactLine(text)
            && !IsDateLine(text);
    }

    private static string Normalize(string text)
    {
        return WhitespaceRun.Replace(text, " ").Trim();
    }
}
