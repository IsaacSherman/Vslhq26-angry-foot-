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
        result[1].SuggestedEmployer.Should().Be("Client Support Administrator", "a bare title is the only heading available");
    }

    [Fact]
    public void DropsFragmentsTooShortToBeAchievements()
    {
        const string text = """
            SKILLS
            • C#
            • SQL
            • Built a self-service reporting portal now used by every regional office.
            """;

        var result = ResumeBulletParser.Parse(text);

        result.Should().ContainSingle()
            .Which.Text.Should().Be("Built a self-service reporting portal now used by every regional office.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \n")]
    public void ReturnsNothingForEmptyInput(string? text)
    {
        ResumeBulletParser.Parse(text).Should().BeEmpty();
    }
}
