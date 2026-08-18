using System.Text.RegularExpressions;

namespace AngryFoot.Tests.Fixtures;

/// <summary>One expected bullet and the employer it belongs to; null when the resume names only a
/// position and no employer for it.</summary>
public sealed record ExpectedBullet(string Text, string? Employer);

public sealed record ResumeCase(string Name, string ResumeText, IReadOnlyList<ExpectedBullet> Expected)
{
    public IReadOnlyList<string> ExpectedBullets => Expected.Select(x => x.Text).ToArray();
}

/// <summary>
/// Pairs each <c>ResumeN.txt</c> in Fixtures/Resumes with its expected <c>BulletsN.txt</c> output so
/// a resume that parses badly can be pinned as a regression by dropping in two files.
/// <para>
/// A number may also carry a <c>ResumeN.md</c>: the same resume as converted from PDF or DOCX,
/// checked against the same expectations. That pairing is the assertion issue #14 is really about -
/// uploading a document has to yield what pasting its text yields - and it is expressed as a shared
/// expectation file rather than a second copy so the two can never be updated apart.
/// </para>
/// </summary>
public static class ResumeCorpus
{
    private static readonly Regex ResumeFileName = new(@"^Resume(\d+)\.(?<format>txt|md)$", RegexOptions.IgnoreCase);

    /// <summary>Marks a bullet the resume gives no employer for, only a position title.</summary>
    public const string NoEmployer = "(none)";

    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Resumes");

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in EnumerateCases().Select(x => x.Name))
        {
            data.Add(name);
        }

        return data;
    }

    public static ResumeCase Load(string name)
    {
        return EnumerateCases().Single(x => x.Name == name);
    }

    /// <summary>Loud rather than skipped: a half-added fixture must not look like a passing suite.</summary>
    private static IReadOnlyList<string> ReadLines(string number, string prefix, string describes)
    {
        var path = Path.Combine(Directory, $"{prefix}{number}.txt");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Resume{number} has no matching {prefix}{number}.txt. Every corpus resume needs {describes}.");
        }

        return File.ReadAllLines(path)
            .Select(line => line.TrimEnd())
            .Where(line => line.Trim().Length > 0)
            .ToArray();
    }

    private static IEnumerable<ResumeCase> EnumerateCases()
    {
        var resumeFiles = System.IO.Directory
            .EnumerateFiles(Directory, "Resume*")
            .Where(path => ResumeFileName.IsMatch(Path.GetFileName(path)))
            .OrderBy(path => int.Parse(ResumeFileName.Match(Path.GetFileName(path)).Groups[1].Value))
            .ThenBy(path => Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

        foreach (var resumePath in resumeFiles)
        {
            var match = ResumeFileName.Match(Path.GetFileName(resumePath));
            var number = match.Groups[1].Value;
            var isMarkdown = string.Equals(match.Groups["format"].Value, "md", StringComparison.OrdinalIgnoreCase);

            var bullets = ReadLines(number, "Bullets", "its expected bullets");
            var pairs = ReadLines(number, "Employers", "the employer each bullet belongs to");

            var expected = new List<ExpectedBullet>(pairs.Count);
            for (var i = 0; i < pairs.Count; i++)
            {
                var columns = pairs[i].Split('\t', 2);
                if (columns.Length != 2)
                {
                    throw new InvalidOperationException(
                        $"Employers{number}.txt line {i + 1} is not a tab-separated 'employer<TAB>bullet' pair: {pairs[i]}");
                }

                var employer = columns[0].Trim();
                expected.Add(new ExpectedBullet(
                    columns[1].Trim(),
                    employer == NoEmployer ? null : employer));
            }

            // The two files restate the same bullets, so drift between them has to be loud or the
            // employer expectations would quietly stop lining up with the bullet expectations.
            var restated = expected.Select(x => x.Text).ToArray();
            if (!restated.SequenceEqual(bullets.Select(x => x.Trim())))
            {
                throw new InvalidOperationException(
                    $"Employers{number}.txt and Bullets{number}.txt disagree. Their bullets must match exactly, in order.");
            }

            var name = isMarkdown ? $"Resume{number} (converted)" : $"Resume{number}";
            yield return new ResumeCase(name, File.ReadAllText(resumePath), expected);
        }
    }
}
