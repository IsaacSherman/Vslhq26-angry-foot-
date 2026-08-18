using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Application.Review;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class ResumeDocumentHeuristicsTests
{
    private static IReadOnlyList<CoverageDiagnosticDto> Check(string resumeText)
    {
        var bullets = ResumeBulletParser.Parse(resumeText)
            .Select(candidate => new Bullet { Id = Guid.NewGuid(), BulletText = candidate.Text })
            .ToArray();

        return ResumeDocumentHeuristics.Check(resumeText, bullets);
    }

    private static IEnumerable<string> Codes(string resumeText) => Check(resumeText).Select(x => x.Code);

    private const string Bullets = """
        - Cut warehouse costs by $280,000 a year by moving cold partitions to object storage.
        - Automated the nightly reconciliation job, removing about 12 hours of manual work a week.
        """;

    [Fact]
    public void RaisesMissingContactInfoWhenTheHeaderHasNoWayToReply()
    {
        Codes($"MILDRED WAFFLE\nPlatform Engineer\n\nEXPERIENCE\n\n{Bullets}")
            .Should().Contain(CoverageDiagnosticCodes.MissingContactInfo);
    }

    [Theory]
    [InlineData("mildred@totally-real-mail.invalid")]
    [InlineData("555-010-3030")]
    [InlineData("linkedin.com/in/mildred")]
    public void StaysQuietWhenTheHeaderHasAnyContactChannel(string contact)
    {
        Codes($"MILDRED WAFFLE\n{contact}\n\nEXPERIENCE\n\n{Bullets}")
            .Should().NotContain(CoverageDiagnosticCodes.MissingContactInfo);
    }

    [Fact]
    public void RaisesInconsistentDatesWhenNumericAndNamedMonthsAreMixed()
    {
        var resume = $"""
            MILDRED WAFFLE
            mildred@totally-real-mail.invalid

            EXPERIENCE

            Marmot Signal Works
            Staff Engineer, May 2016 - Present

            {Bullets}

            Paperclip Galaxy
            QA Manager, 09/2019 - 03/2020
            """;

        var diagnostic = Check(resume).Single(x => x.Code == CoverageDiagnosticCodes.InconsistentDates);
        diagnostic.Message.Should().Contain("numeric months").And.Contain("named months");
        diagnostic.Severity.Should().Be(DiagnosticSeverityDto.Suggestion, "a mixed format is untidy, not broken");
    }

    [Fact]
    public void StaysQuietWhenEveryDateUsesOneFormat()
    {
        var resume = $"""
            MILDRED WAFFLE
            mildred@totally-real-mail.invalid

            EXPERIENCE

            Marmot Signal Works
            Staff Engineer, May 2016 - August 2019

            {Bullets}

            Paperclip Galaxy
            QA Manager, September 2019 - March 2020
            """;

        Codes(resume).Should().NotContain(CoverageDiagnosticCodes.InconsistentDates);
    }

    [Fact]
    public void RaisesMissingSectionWhenNoHeadingNamesTheWorkHistory()
    {
        Codes($"MILDRED WAFFLE\nmildred@totally-real-mail.invalid\n\nMarmot Signal Works\n\n{Bullets}")
            .Should().Contain(CoverageDiagnosticCodes.MissingSection);
    }

    [Theory]
    [InlineData("EXPERIENCE")]
    [InlineData("Work History")]
    [InlineData("PROFESSIONAL HISTORY")]
    public void StaysQuietWhenAnyWorkHistoryHeadingIsPresent(string heading)
    {
        Codes($"MILDRED WAFFLE\nmildred@totally-real-mail.invalid\n\n{heading}\n\n{Bullets}")
            .Should().NotContain(CoverageDiagnosticCodes.MissingSection);
    }

    [Fact]
    public void RaisesNoBulletsFoundRatherThanReportingAnEmptyReview()
    {
        var diagnostics = Check("MILDRED WAFFLE\nmildred@totally-real-mail.invalid\n\nI have done many things.");

        diagnostics.Should().Contain(x => x.Code == CoverageDiagnosticCodes.NoBulletsFound);
        diagnostics.Should().NotContain(x => x.Code == CoverageDiagnosticCodes.MissingSection,
            "a document with no bullets has no work-history section to have missed a heading for");
    }

    [Fact]
    public void RaisesLongBulletOnlyPastTheThreshold()
    {
        var longBullet = "Rebuilt " + string.Join(" ", Enumerable.Repeat("the notification delivery subsystem", 12));
        var resume = $"MILDRED WAFFLE\nmildred@totally-real-mail.invalid\n\nEXPERIENCE\n\n- {longBullet}.\n{Bullets}";

        var diagnostics = Check(resume).Where(x => x.Code == CoverageDiagnosticCodes.LongBullet).ToArray();

        diagnostics.Should().ContainSingle("only the long bullet is long; the other two are not");
        diagnostics[0].Why.SupportingEvidence.Should().ContainSingle()
            .Which.BulletText.Should().StartWith("Rebuilt the notification");
    }
}
