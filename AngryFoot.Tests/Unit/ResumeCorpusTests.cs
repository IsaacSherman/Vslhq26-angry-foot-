using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.Tests.Fixtures;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// Golden-file regression suite over Fixtures/Resumes. See the README there for how to add a case.
/// </summary>
public class ResumeCorpusTests
{
    public static TheoryData<string> Cases => ResumeCorpus.CaseNames();

    [Theory]
    [MemberData(nameof(Cases))]
    public void ParsesCorpusResumeIntoExpectedBullets(string caseName)
    {
        var resumeCase = ResumeCorpus.Load(caseName);

        var actual = ResumeBulletParser.Parse(resumeCase.ResumeText).Select(x => x.Text).ToArray();

        actual.Should().Equal(resumeCase.ExpectedBullets, Describe(resumeCase.ExpectedBullets, actual));
    }

    [Fact]
    public void CorpusIsNotEmpty()
    {
        ResumeCorpus.CaseNames().Should().NotBeEmpty("the parser's regression coverage lives in Fixtures/Resumes");
    }

    private static string Describe(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var missing = expected.Except(actual).ToArray();
        var unexpected = actual.Except(expected).ToArray();

        if (missing.Length == 0 && unexpected.Length == 0)
        {
            return "the bullets are right but came out in a different order";
        }

        var parts = new List<string>();
        if (missing.Length > 0)
        {
            parts.Add($"missing: {string.Join(" | ", missing)}");
        }

        if (unexpected.Length > 0)
        {
            parts.Add($"unexpected: {string.Join(" | ", unexpected)}");
        }

        return string.Join("; ", parts);
    }
}
