using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class BulletEvidenceTests
{
    private static Bullet Bullet(string text, string[]? skills = null, string[]? technologies = null)
        => new()
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            Skills = (skills ?? []).ToList(),
            Technologies = (technologies ?? []).ToList()
        };

    [Theory]
    [InlineData("Migrated billing to AWS last quarter.", "AWS")]
    [InlineData("Migrated billing to aws last quarter.", "AWS")]
    [InlineData("Ran everything on AWS.", "aws")]
    [InlineData("Deployed to AWS, then to Azure.", "AWS")]
    [InlineData("(AWS)", "AWS")]
    public void Supports_WholeWord_MatchesTheTermAsItsOwnWord(string text, string term)
    {
        BulletEvidence.Supports(Bullet(text), term).Should().BeTrue();
    }

    [Theory]
    [InlineData("Reviewed contracts against federal laws.", "AWS")]
    [InlineData("Wrote a jigsaw solver.", "saw")]
    [InlineData("Shipped the latest release.", "test")]
    [InlineData("Refactored the awstats parser.", "AWS")]
    public void Supports_WholeWord_IgnoresTermsBuriedInsideOtherWords(string text, string term)
    {
        BulletEvidence.Supports(Bullet(text), term)
            .Should().BeFalse("a term inside an unrelated word is not evidence of anything");
    }

    [Theory]
    [InlineData("Built C# services.", "C#")]
    [InlineData("Wrote C++ firmware.", "C++")]
    [InlineData("Ported the app to ASP.NET Core.", ".NET")]
    [InlineData("Used Node.js on the edge.", "Node.js")]
    public void Supports_WholeWord_HandlesTermsWithPunctuation(string text, string term)
    {
        BulletEvidence.Supports(Bullet(text), term)
            .Should().BeTrue("technology names carry punctuation that already forms a boundary");
    }

    [Theory]
    [InlineData("Analyzed query plans.", "analyz")]
    [InlineData("Owned the analysis of the outage.", "analys")]
    [InlineData("Developed and shipped the service.", "develop")]
    [InlineData("Led development of the platform.", "develop")]
    public void Supports_WordStart_ReachesInflectedFormsOfAClippedStem(string text, string term)
    {
        BulletEvidence.Supports(Bullet(text), term, EvidenceMatch.WordStart).Should().BeTrue();
    }

    [Theory]
    [InlineData("Shipped the latest release.", "test")]
    [InlineData("Reduced misdevelopment of features.", "develop")]
    public void Supports_WordStart_StillRequiresTheTermToStartAWord(string text, string term)
    {
        BulletEvidence.Supports(Bullet(text), term, EvidenceMatch.WordStart).Should().BeFalse();
    }

    [Fact]
    public void Supports_ReadsSkillsAndTechnologiesAsWellAsBulletText()
    {
        var bullet = Bullet("Shipped a release.", skills: ["Kubernetes"], technologies: ["AWS"]);

        BulletEvidence.Supports(bullet, "Kubernetes").Should().BeTrue();
        BulletEvidence.Supports(bullet, "AWS").Should().BeTrue();
        BulletEvidence.Supports(bullet, "Terraform").Should().BeFalse();
    }

    [Fact]
    public void SupportsAny_IsTrueWhenAnySingleTermMatches()
    {
        var bullet = Bullet("Diagnosed a production outage.");

        BulletEvidence.SupportsAny(bullet, ["hotfix", "outage"]).Should().BeTrue();
        BulletEvidence.SupportsAny(bullet, ["hotfix", "rollback"]).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Supports_WithNoTerm_IsFalse(string? term)
    {
        BulletEvidence.Supports(Bullet("Anything."), term!).Should().BeFalse();
    }
}
