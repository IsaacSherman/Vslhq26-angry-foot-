using AngryFoot.ApiService.Application.Bullets;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// These heuristics moved out of <c>BulletRewriteAssistant</c> so the evidence engine and the
/// diagnostics could share one definition of a well-written bullet. The impact cases below are the
/// ones the rewrite assistant's fallback suggestions depended on before the move.
/// </summary>
public class BulletQualityHeuristicsTests
{
    [Theory]
    [InlineData("Cut build times by 40%.")]
    [InlineData("Saved $12,000 annually.")]
    [InlineData("Improved throughput 3x.")]
    [InlineData("Reduced deploys from 6 hours to 40 minutes.")]
    [InlineData("Shipped in 3 weeks.")]
    [InlineData("Handled 1,250 requests per second.")]
    public void HasMeasurableImpact_WithAFigure_IsTrue(string text)
    {
        BulletQualityHeuristics.HasMeasurableImpact(text).Should().BeTrue();
    }

    [Theory]
    [InlineData("Led the migration to a new platform.")]
    [InlineData("Responsible for the deployment pipeline.")]
    [InlineData("Improved reliability considerably.")]
    public void HasMeasurableImpact_WithoutAFigure_IsFalse(string text)
    {
        BulletQualityHeuristics.HasMeasurableImpact(text).Should().BeFalse();
    }

    [Theory]
    [InlineData("Responsible for the release process.", "responsible for")]
    [InlineData("  Worked on the billing service.", "worked on")]
    [InlineData("Helped with onboarding.", "helped with")]
    public void WeakOpener_WhenTheBulletDescribesAnAssignment_NamesIt(string text, string expected)
    {
        BulletQualityHeuristics.WeakOpener(text).Should().Be(expected);
    }

    [Fact]
    public void WeakOpener_WhenTheBulletOpensWithAnAction_IsNull()
    {
        BulletQualityHeuristics.WeakOpener("Rebuilt the release process, cutting deploys by 40%.")
            .Should().BeNull();
    }

    [Fact]
    public void WeakOpener_WhenTheWeakPhraseIsNotAtTheStart_IsNull()
    {
        BulletQualityHeuristics.WeakOpener("Rebuilt the pipeline I was previously responsible for.")
            .Should().BeNull("the opening words are what the diagnostic is about");
    }

    [Fact]
    public void ContentTokens_DropsStopWordsAndShortWords()
    {
        var tokens = BulletQualityHeuristics.ContentTokens("Led the migration of a team to Azure");

        tokens.Should().Contain(["led", "migration", "azure"]);
        tokens.Should().NotContain(["the", "for", "team", "to", "of", "a"]);
    }

    [Fact]
    public void ContentTokens_CountsARepeatedWordOnce()
    {
        BulletQualityHeuristics.ContentTokens("Migrated services, then migrated the rest")
            .Count(token => token == "migrated")
            .Should().Be(1, "the callers ask how many bullets use a word, not how often it appears");
    }

    [Fact]
    public void ContentTokens_KeepsPunctuationInsideAName()
    {
        BulletQualityHeuristics.ContentTokens("Built services on ASP.NET Core")
            .Should().Contain("asp.net", "a dotted name is one word, not two");
    }

    [Fact]
    public void ContentTokens_DropsVeryShortNamesAlongWithEverythingElseShort()
    {
        BulletQualityHeuristics.ContentTokens("Built C# services")
            .Should().NotContain("c#",
                "the length floor is what keeps two-letter filler out of repetition counts, and it "
                + "cannot make an exception for short technology names without letting the filler back in");
    }

    [Fact]
    public void NamesTechnology_AndMentionsOutcome_ReadTheirKeywordLists()
    {
        BulletQualityHeuristics.NamesTechnology("Built the Azure pipeline").Should().BeTrue();
        BulletQualityHeuristics.NamesTechnology("Ran the weekly planning meeting").Should().BeFalse();

        BulletQualityHeuristics.MentionsOutcome("Improved reliability").Should().BeTrue();
        BulletQualityHeuristics.MentionsOutcome("Attended the design review").Should().BeFalse();
    }
}
