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

    // Institution and office lines ("North Central University, Castle Pines, NM") sit among the
    // achievements but describe where, not what. The state code keeps this from firing on a bullet
    // that happens to end in an acronym.
    private static readonly Regex TrailingLocation = new(@",[^,]{0,40}\b([A-Z]{2})$", RegexOptions.Compiled);

    private static readonly HashSet<string> StateCodes = new(StringComparer.Ordinal)
    {
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA", "HI", "ID", "IL", "IN", "IA",
        "KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ",
        "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT",
        "VA", "WA", "WV", "WI", "WY", "DC", "PR", "GU", "VI", "AE", "AP", "AA", "ZZ"
    };

    private const int MaximumSectionHeadingLength = 60;
    private const int MaximumSectionHeadingWords = 6;

    /// <summary>Sections whose contents are biographical or list-like rather than achievements.</summary>
    private static readonly HashSet<string> ExcludedSectionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "education", "academics", "academic", "degree", "degrees", "coursework", "course", "courses",
        "skills", "skill", "expertise", "competencies", "proficiencies", "languages", "technologies",
        "technology", "methodologies", "tools", "frameworks", "certifications", "certification",
        "certificates", "certificate", "licenses", "awards", "honors", "honours", "scholarships",
        "publications", "papers", "patents", "presentations", "interests", "hobbies", "references",
        "affiliations", "memberships", "summary", "objective", "profile", "contact"
    };

    /// <summary>Sections that introduce achievements, including sub-headings inside a job.</summary>
    private static readonly HashSet<string> AchievementSectionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "experience", "employment", "history", "work", "career", "positions", "roles", "projects",
        "project", "achievements", "accomplishments", "highlights", "military", "service"
    };

    private static readonly HashSet<string> PrepositionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "with", "in", "of", "for", "at", "on", "to", "from", "by", "using", "across", "into"
    };

    private static readonly Regex Parenthetical = new(@"\([^)]*\)", RegexOptions.Compiled);

    private enum SectionKind
    {
        /// <summary>Bullets found here are candidate achievements.</summary>
        Achievements,

        /// <summary>Degrees, skill lists, and the like — never candidate achievements.</summary>
        Excluded
    }

    public static IReadOnlyList<ParsedCandidate> Parse(string? resumeText)
    {
        if (string.IsNullOrWhiteSpace(resumeText))
        {
            return [];
        }

        var lines = resumeText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // Decide by whether the resume is written with markers, not by whether the marked pass found
        // anything: a marked resume whose bullets all sit in skipped sections must still be read as
        // marked, or the fallback would re-emit those lines with their glyphs attached. Two markers
        // are required so a lone hyphenated line can't reinterpret a marker-less resume.
        var markerLines = lines.Count(x => MarkerPattern.IsMatch(x.Trim()));

        return markerLines >= 2 ? ExtractMarkedBullets(lines) : ExtractUnmarkedBullets(lines);
    }

    private static List<ParsedCandidate> ExtractMarkedBullets(string[] lines)
    {
        var candidates = new List<ParsedCandidate>();
        var current = new System.Text.StringBuilder();
        var context = new ResumeContext();
        string? candidateEmployer = null;
        var candidateSection = SectionKind.Achievements;

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            var text = Normalize(current.ToString());
            if (candidateSection == SectionKind.Achievements && IsPlausibleBullet(text, MinimumBulletLength))
            {
                candidates.Add(new ParsedCandidate(text, candidateEmployer));
                context.StartNewBlock();
            }

            current.Clear();
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                Flush();
                context.StartNewBlock();
                continue;
            }

            var marker = MarkerPattern.Match(trimmed);
            if (marker.Success)
            {
                Flush();
                current.Append(trimmed[marker.Length..]);
                candidateEmployer = context.Employer;
                candidateSection = context.Section;
                continue;
            }

            if (current.Length > 0 && IsContinuation(trimmed, current.ToString()))
            {
                current.Append(' ').Append(trimmed);
                continue;
            }

            Flush();
            context.Observe(trimmed);
        }

        Flush();
        return candidates;
    }

    /// <summary>
    /// Tracks where in the resume we are: which section, and which employer the bullets belong to.
    ///
    /// Only the first heading-shaped line in a block sets the employer, so the job title or date
    /// line that usually follows it ("Staff Engineer, 2021 - Present") doesn't overwrite it. A blank
    /// line or an emitted bullet opens the next block, which is what lets back-to-back roles with no
    /// blank line between them ("Client Support Administrator (2006-2010)") still register.
    /// </summary>
    private sealed class ResumeContext
    {
        private bool _employerSetInBlock;

        public string? Employer { get; private set; }

        /// <summary>Assume achievements until a heading says otherwise, so a pasted fragment with no
        /// headings at all still yields candidates.</summary>
        public SectionKind Section { get; private set; } = SectionKind.Achievements;

        public void StartNewBlock() => _employerSetInBlock = false;

        public void Observe(string line)
        {
            if (TryClassifySection(line, out var kind))
            {
                // A sub-heading inside the experience section ("Significant Achievements:") keeps
                // the job it belongs to; anything else starts fresh.
                if (kind == SectionKind.Excluded || Section != SectionKind.Achievements)
                {
                    Employer = null;
                }

                Section = kind;
                _employerSetInBlock = false;
                return;
            }

            if (IsSectionHeading(line))
            {
                Employer = null;
                Section = SectionKind.Achievements;
                _employerSetInBlock = false;
                return;
            }

            if (_employerSetInBlock || !TryReadEmployer(line, out var employer))
            {
                return;
            }

            Employer = employer;
            _employerSetInBlock = true;
        }
    }

    /// <summary>
    /// Recognizes a section label by its vocabulary rather than its casing, since plenty of resumes
    /// write "Education" and "Experience" in ordinary title case. Kept to short, few-word lines so a
    /// long achievement that merely mentions one of these words isn't mistaken for a heading.
    /// </summary>
    private static bool TryClassifySection(string line, out SectionKind kind)
    {
        kind = SectionKind.Achievements;

        // "(In Progress)" and "(2009-2011)" qualify a heading without changing what it is.
        var text = Parenthetical.Replace(line, " ").TrimEnd(':').Trim();
        if (text.Length == 0 || text.Length > MaximumSectionHeadingLength || EndsSentence(text))
        {
            return false;
        }

        var words = text.Split(
            [' ', '\t', ',', '/', '&', '(', ')', '.', '-', '–', '—', '|', ':'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0 || words.Length > MaximumSectionHeadingWords)
        {
            return false;
        }

        // A preposition means it is a phrase about something ("Experience with Python",
        // "Master of Science in Computer Science"), not a label for the section itself.
        if (words.Any(PrepositionWords.Contains))
        {
            return false;
        }

        // Excluded wins ties so "Relevant Course Work" is coursework, not work history.
        if (words.Any(ExcludedSectionWords.Contains))
        {
            kind = SectionKind.Excluded;
            return true;
        }

        if (words.Any(AchievementSectionWords.Contains))
        {
            kind = SectionKind.Achievements;
            return true;
        }

        return false;
    }

    private static List<ParsedCandidate> ExtractUnmarkedBullets(string[] lines)
    {
        var candidates = new List<ParsedCandidate>();
        var current = new System.Text.StringBuilder();
        var context = new ResumeContext();
        string? candidateEmployer = null;
        var candidateSection = SectionKind.Achievements;

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            var text = Normalize(current.ToString());
            if (LooksLikeJobHeading(text) || LooksLikeLocation(text) || !IsPlausibleBullet(text, MinimumUnmarkedBulletLength))
            {
                context.Observe(text);
            }
            else if (candidateSection == SectionKind.Achievements)
            {
                candidates.Add(new ParsedCandidate(text, candidateEmployer));
                context.StartNewBlock();
            }

            current.Clear();
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                Flush();
                context.StartNewBlock();
                continue;
            }

            // A resume with a single marked line lands here; strip it so the glyph never survives
            // into a candidate.
            var marker = MarkerPattern.Match(trimmed);
            if (marker.Success)
            {
                trimmed = trimmed[marker.Length..];
            }

            if (IsSectionHeading(trimmed) || IsContactLine(trimmed) || IsDateLine(trimmed))
            {
                Flush();
                context.Observe(trimmed);
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
            candidateEmployer = context.Employer;
            candidateSection = context.Section;
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

    /// <summary>
    /// An institution or office line ("North Central University, Castle Pines, NM") — it sits among
    /// the achievements but says where the job was, not what was accomplished.
    /// </summary>
    private static bool LooksLikeLocation(string line)
    {
        var match = TrailingLocation.Match(line);
        return match.Success && StateCodes.Contains(match.Groups[1].Value);
    }

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
        // "Education" / "Languages, Technologies, and Methodologies" are headings by vocabulary
        // even though they are neither all-caps nor colon-terminated.
        if (TryClassifySection(line, out _))
        {
            return true;
        }

        var letters = line.Where(char.IsLetter).ToArray();
        if (letters.Length == 0)
        {
            return false;
        }

        // An all-caps line is a heading, but a bare acronym on its own line ("SQL", "AWS") is a
        // skills-list entry and must not be mistaken for the start of a new section.
        if (line.Length <= MaximumHeadingLength
            && letters.All(char.IsUpper)
            && (letters.Length >= 5 || line.Contains(' ', StringComparison.Ordinal)))
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
