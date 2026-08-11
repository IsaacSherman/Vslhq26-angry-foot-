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

    /// <summary>
    /// Each bullet has to land on the employer it was written under — and on no employer at all when
    /// the resume only gives a position title, since a job title masquerading as a company is worse
    /// than a blank the user can fill in.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void AssociatesCorpusBulletsWithTheirEmployer(string caseName)
    {
        var resumeCase = ResumeCorpus.Load(caseName);

        var actual = ResumeBulletParser.Parse(resumeCase.ResumeText);

        // Bullet extraction is covered by the sibling test; assert pairing only for what we found,
        // so a parsing regression reports there rather than as a confusing employer mismatch.
        var comparable = Math.Min(actual.Count, resumeCase.Expected.Count);
        for (var i = 0; i < comparable; i++)
        {
            actual[i].SuggestedEmployer.Should().Be(
                resumeCase.Expected[i].Employer,
                "of the bullet \"{0}\"", Truncate(resumeCase.Expected[i].Text));
        }
    }

    private static string Truncate(string text) => text.Length <= 60 ? text : text[..60] + "...";

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
