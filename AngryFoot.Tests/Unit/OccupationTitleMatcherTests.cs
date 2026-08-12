using AngryFoot.ApiService.Application.Benchmarks;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class OccupationTitleMatcherTests
{
    private static readonly BenchmarkOccupation SoftwareDevelopers = Occupation(
        "15-1252.00", "Software Developers", ["Software Engineer", "Application Developer", "DevOps Engineer"]);

    private static readonly BenchmarkOccupation DataScientists = Occupation(
        "15-2051.00", "Data Scientists", []);

    private static readonly BenchmarkOccupation SecurityAnalysts = Occupation(
        "15-1212.00", "Information Security Analysts", ["Security Analyst", "Network Security Analyst"]);

    private static readonly BenchmarkOccupation[] Dataset = [SoftwareDevelopers, DataScientists, SecurityAnalysts];

    private static BenchmarkOccupation Occupation(string soc, string title, string[] alternates)
        => new(soc, title, alternates, [new BenchmarkItem("Programming", "Skill", 75, ["program"])]);

    [Fact]
    public void Match_WithOccupationTitle_IsExact()
    {
        var match = OccupationTitleMatcher.Match("Software Developers", Dataset);

        match.Should().NotBeNull();
        match!.Occupation.SocCode.Should().Be("15-1252.00");
        match.Confidence.Should().Be(OccupationMatchConfidence.Exact);
    }

    [Fact]
    public void Match_WithAlternateTitle_IsExact()
    {
        var match = OccupationTitleMatcher.Match("Application Developer", Dataset);

        match.Should().NotBeNull();
        match!.Occupation.SocCode.Should().Be("15-1252.00");
        match.Confidence.Should().Be(OccupationMatchConfidence.Exact, "reported job titles are matched as literally as the occupation title");
        match.MatchedTitle.Should().Be("Application Developer");
    }

    [Theory]
    [InlineData("Senior Software Engineer II")]
    [InlineData("Staff Software Engineer")]
    [InlineData("Jr. Software Engineer")]
    [InlineData("software engineer iii")]
    [InlineData("Principal Software Engineer, Level 3")]
    public void Match_IgnoresSeniorityAndLevelMarkers(string title)
    {
        var match = OccupationTitleMatcher.Match(title, Dataset);

        match.Should().NotBeNull();
        match!.Occupation.SocCode.Should().Be("15-1252.00");
        match.Confidence.Should().Be(OccupationMatchConfidence.Exact,
            "seniority and ladder level say where in a career a role sits, not which occupation it is");
    }

    [Fact]
    public void Match_SingularizesTitles()
    {
        var match = OccupationTitleMatcher.Match("Data Scientist", Dataset);

        match.Should().NotBeNull();
        match!.Occupation.SocCode.Should().Be("15-2051.00", "O*NET names occupations in the plural");
        match.Confidence.Should().Be(OccupationMatchConfidence.Exact);
    }

    [Fact]
    public void Match_WithPartialOverlap_IsFuzzy()
    {
        var match = OccupationTitleMatcher.Match("Information Security Analyst and Engineer", Dataset);

        match.Should().NotBeNull();
        match!.Occupation.SocCode.Should().Be("15-1212.00");
        match.Confidence.Should().Be(OccupationMatchConfidence.Fuzzy);
    }

    [Theory]
    [InlineData("Chief Vibes Officer")]
    [InlineData("Registered Nurse")]
    [InlineData("Line Cook")]
    public void Match_WithUnrelatedTitle_ReturnsNull(string title)
    {
        OccupationTitleMatcher.Match(title, Dataset)
            .Should().BeNull("a bogus occupational comparison is worse than none at all");
    }

    [Fact]
    public void Match_WithGenericSingleWord_ReturnsNull()
    {
        OccupationTitleMatcher.Match("Engineer", Dataset)
            .Should().BeNull("one generic word shared with a title is not enough to pick an occupation");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Senior II")]
    public void Match_WithNoUsableTitle_ReturnsNull(string? title)
    {
        OccupationTitleMatcher.Match(title, Dataset).Should().BeNull();
    }

    [Fact]
    public void Match_WithEmptyDataset_ReturnsNull()
    {
        OccupationTitleMatcher.Match("Software Engineer", []).Should().BeNull();
    }
}
