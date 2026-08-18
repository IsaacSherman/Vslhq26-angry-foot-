using AngryFoot.ApiService.Application.Bullets;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class ResumeBulletParserTests
{
    [Theory]
    [InlineData("• ")]
    [InlineData("- ")]
    [InlineData("* ")]
    [InlineData("▪ ")]
    [InlineData("o ")]
    [InlineData("1. ")]
    [InlineData("2) ")]
    public void StripsLeadingBulletMarkers(string marker)
    {
        var text = $"{marker}Shipped a caching layer that halved median API response times.";

        var result = ResumeBulletParser.Parse(text);

        result.Should().ContainSingle()
            .Which.Text.Should().Be("Shipped a caching layer that halved median API response times.");
    }

    [Fact]
    public void JoinsWrappedContinuationLines()
    {
        const string text = """
            • Rearchitected the notification service to use a durable queue, eliminating the
              message loss that had been reported by support roughly twice a month.
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Should().ContainSingle()
            .Which.Text.Should().Be(
                "Rearchitected the notification service to use a durable queue, eliminating the message loss that had been reported by support roughly twice a month.");
    }

    [Fact]
    public void SkipsHeadingsAndContactDetails()
    {
        const string text = """
            ALEX RIVERA
            alex@example.com | (555) 987-6543
            PROFESSIONAL EXPERIENCE
            • Consolidated three reporting tools into one, saving the team six hours per week.
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Should().ContainSingle()
            .Which.Text.Should().Be("Consolidated three reporting tools into one, saving the team six hours per week.");
    }

    [Fact]
    public void SuggestsEmployerFromTheHeadingAboveEachBlock()
    {
        const string text = """
            EXPERIENCE

            Contoso Ltd
            Senior Engineer, 2021 - Present
            • Rolled out feature flags across the platform, cutting rollback frequency in half.

            Fabrikam
            Engineer, 2019 - 2021
            • Replaced a nightly batch export with an incremental sync used by four teams.
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Should().HaveCount(2);
        result[0].SuggestedEmployer.Should().Be("Contoso Ltd");
        result[1].SuggestedEmployer.Should().Be("Fabrikam", "the job title line must not overwrite the employer");
    }

    [Fact]
    public void FallsBackToUnmarkedLinesWhenPastingLostTheBulletGlyphs()
    {
        const string text = """
            EXPERIENCE

            Contoso Ltd

            Migrated the payments service off a deprecated vendor SDK with no customer downtime.
            Introduced contract tests that caught three breaking API changes before release.
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Should().HaveCount(2);
        result[0].Text.Should().Be("Migrated the payments service off a deprecated vendor SDK with no customer downtime.");
        result[0].SuggestedEmployer.Should().Be("Contoso Ltd");
    }

    [Fact]
    public void TreatsRoleAtEmployerDateLinesAsHeadingsNotAchievements()
    {
        const string text = """
            Experience:
            Intern at Emerson Process Management 	May 2014 – September 2016
            Analyzed existing research in relevant problem domains to develop prototype solutions
            Client Support Administrator (2006-2010)
            Ensured the squadron's compliance with Air Force standards across 115 computers
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Select(x => x.Text).Should().Equal(
            "Analyzed existing research in relevant problem domains to develop prototype solutions",
            "Ensured the squadron's compliance with Air Force standards across 115 computers");
        result[0].SuggestedEmployer.Should().Be("Emerson Process Management", "the employer follows the role");
        result[1].SuggestedEmployer.Should().BeNull(
            "the only heading is a job title, and a title posing as a company is worse than a blank");
    }

    [Fact]
    public void DropsFragmentsTooShortToBeAchievements()
    {
        const string text = """
            EXPERIENCE
            • C#
            • SQL
            • Built a self-service reporting portal now used by every regional office.
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Should().ContainSingle()
            .Which.Text.Should().Be("Built a self-service reporting portal now used by every regional office.");
    }

    [Fact]
    public void SkipsBiographicalSections()
    {
        const string text = """
            Education
            North Central University of Questionable Science, Castle Pines NM
            Master of Science in Computational Folklore   GPA: 4.0
            Skills/Expertise
            Languages: Java, C#, JavaScript, TypeScript, T-SQL, and several regrettable dialects
            Experience
            Consolidated three reporting tools into one, saving the team six hours per week
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Select(x => x.Text).Should().Equal(
            "Consolidated three reporting tools into one, saving the team six hours per week");
    }

    [Fact]
    public void KeepsMarkedResumeInMarkedModeEvenWhenEverySectionIsSkipped()
    {
        const string text = """
            SKILLS
            • Proficient in C# and the surrounding .NET ecosystem across many years
            • Comfortable with SQL, schema design, and query tuning under load
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Should().BeEmpty("skills entries are not achievements, and the marker glyphs must "
            + "never leak into candidates via the marker-less fallback");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \n")]
    public void ReturnsNothingForEmptyInput(string? text)
    {
        ResumeBulletParser.Parse(text).Should().BeEmpty();
    }

    [Fact]
    public void ReadsMarkdownEmphasisAsPlainText()
    {
        // A converted DOCX marks employer and role lines with bold rather than capitals, so the
        // employer is only findable once the delimiters are gone.
        const string text = """
            **Experience**

            **Senior Engineer at Gigglebyte Controls July 2018 - Present**

            * Rebuilt the *deployment* pipeline and cut release time from six hours to forty minutes.
            """;

        var result = ResumeBulletParser.Parse(text);

        var candidate = result.Should().ContainSingle().Which;
        candidate.Text.Should().Be(
            "Rebuilt the deployment pipeline and cut release time from six hours to forty minutes.");
        candidate.SuggestedEmployer.Should().Be("Gigglebyte Controls");
    }

    [Fact]
    public void LeavesAnAsteriskSeparatorAlone()
    {
        // "8896 Streetroad Way * Boring Springs * 555-010-5151" is a contact line, not emphasis:
        // the asterisks never pair, and reading them as emphasis would swallow the separators.
        const string text = """
            8896 Streetroad Way * Boring Springs, ZZ 00042 * 555-010-5151

            * Consolidated four reporting jobs into one, saving eleven hours of manual work a month.
            * Rewrote the ingest validator and cut malformed-record incidents to zero for two quarters.
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Select(x => x.Text).Should().BeEquivalentTo(
            "Consolidated four reporting jobs into one, saving eleven hours of manual work a month.",
            "Rewrote the ingest validator and cut malformed-record incidents to zero for two quarters.");
    }

    [Fact]
    public void UnescapesMarkdownEscapesRatherThanReadingThemAsEmphasis()
    {
        const string text = """
            * Shipped the modernization plan without spawning any folders named final\_really\_final.
            * Retired the legacy exporter and moved its four consumers onto the shared API.
            """;

        var result = ResumeBulletParser.Parse(text);

        result[0].Text.Should().Be(
            "Shipped the modernization plan without spawning any folders named final_really_final.");
    }

    [Fact]
    public void StripsAtxHeadingMarkersWithoutTreatingThemAsSectionBreaks()
    {
        // A converter marks a job title with a heading as readily as a section, and the dated-title
        // nesting this parser already does is the thing that reads it correctly.
        const string text = """
            Interstate Compliance Patrol (2005-2011)

            # Client Systems Custodian (2005-2009)

            * Maintained workstation compliance across a busy unit with no missing baselines.
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Should().ContainSingle().Which.SuggestedEmployer.Should().Be("Interstate Compliance Patrol");
    }

    [Fact]
    public void ReadsAMarkdownLinkAsItsText()
    {
        const string text = """
            Reach me at [pat@totally-real-mail.invalid](mailto:pat@totally-real-mail.invalid)

            * Delivered the billing rewrite two weeks early and retired the nightly reconciliation job.
            * Introduced contract tests that caught nine breaking changes before they reached staging.
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Should().HaveCount(2, "the contact line is a contact line once the link syntax is gone");
    }

    [Fact]
    public void ReadsEachTableCellAsItsOwnLine()
    {
        // A skills table is laid out to be read cell by cell; joining the row would invent a
        // sentence that pairs unrelated entries.
        const string text = """
            EXPERIENCE

            * Ported the prototype scoring logic into production with careful bit handling throughout.

            SKILLS

            | C#/.NET, REST APIs | WPF, WinForms, UI triage |
            | --- | --- |
            | SQLite, SQL, data cleanup | C++, gRPC, native interop |
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Should().ContainSingle("table cells in a skills section are not achievements")
            .Which.Text.Should().StartWith("Ported the prototype");
    }

    [Fact]
    public void KeepsAnInlinePipeOnOneLine()
    {
        // "QA Lead / SDET | 2020 - 2021" separates fields inside a heading. Only a row fenced by
        // pipes at both ends is a table.
        const string text = """
            Paperclip Galaxy LLC - Mild Panic, OH

            QA Manager | 09/2019 - 03/2020

            - Coached a twenty-person engineering group on review habits and defect prevention.
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Should().ContainSingle().Which.SuggestedEmployer.Should().Be("Paperclip Galaxy LLC");
    }
}
