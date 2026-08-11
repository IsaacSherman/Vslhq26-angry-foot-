using System.Text.RegularExpressions;

namespace AngryFoot.Tests.Fixtures;

public sealed record ResumeCase(string Name, string ResumeText, IReadOnlyList<string> ExpectedBullets);

/// <summary>
/// Pairs each <c>ResumeN.txt</c> in Fixtures/Resumes with its expected <c>BulletsN.txt</c> output so
/// a resume that parses badly can be pinned as a regression by dropping in two files.
/// </summary>
public static class ResumeCorpus
{
    private static readonly Regex ResumeFileName = new(@"^Resume(\d+)\.txt$", RegexOptions.IgnoreCase);

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

    private static IEnumerable<ResumeCase> EnumerateCases()
    {
        var resumeFiles = System.IO.Directory
            .EnumerateFiles(Directory, "Resume*.txt")
            .Where(path => ResumeFileName.IsMatch(Path.GetFileName(path)))
            .OrderBy(path => int.Parse(ResumeFileName.Match(Path.GetFileName(path)).Groups[1].Value));

        foreach (var resumePath in resumeFiles)
        {
            var number = ResumeFileName.Match(Path.GetFileName(resumePath)).Groups[1].Value;
            var bulletsPath = Path.Combine(Directory, $"Bullets{number}.txt");

            // Loud rather than skipped: a half-added fixture must not look like a passing suite.
            if (!File.Exists(bulletsPath))
            {
                throw new InvalidOperationException(
                    $"Resume{number}.txt has no matching Bullets{number}.txt. Every corpus resume needs its expected bullets.");
            }

            var expected = File.ReadAllLines(bulletsPath)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();

            yield return new ResumeCase($"Resume{number}", File.ReadAllText(resumePath), expected);
        }
    }
}
