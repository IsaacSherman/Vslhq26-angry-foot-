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
    // Spelled out, because the check is about the claim and not about how it is typed.
    [InlineData("Authored five acceptance tests across three products.")]
    [InlineData("Cut the release cycle from twelve weeks to two.")]
    [InlineData("Mentored twenty-five engineers.")]
    [InlineData("Processed millions of telemetry records.")]
    [InlineData("Ran a dozen migrations without an outage.")]
    public void HasMeasurableImpact_WithAFigure_IsTrue(string text)
    {
        BulletQualityHeuristics.HasMeasurableImpact(text).Should().BeTrue();
    }

    [Theory]
    [InlineData("Led the migration to a new platform.")]
    [InlineData("Responsible for the deployment pipeline.")]
    [InlineData("Improved reliability considerably.")]
    // "One" is a pronoun far more often than a count, and an ordinal is a claim to specificity
    // rather than to measurement.
    [InlineData("Rebuilt the pipeline, one of which needed a rewrite.")]
    [InlineData("Built the first CI/CD server the team had.")]
    [InlineData("Delivered several improvements to the platform.")]
    public void HasMeasurableImpact_WithoutAFigure_IsFalse(string text)
    {
        BulletQualityHeuristics.HasMeasurableImpact(text).Should().BeFalse();
    }

    /// <summary>
    /// The defect this pair was written for: two wordings of one accomplishment, differing only in
    /// whether the counts are spelled out. Scoring them differently made the thirty points for a
    /// measurable result winnable by find-and-replace, and docked the longer, more specific wording
    /// for being written in prose.
    /// </summary>
    [Fact]
    public void HasMeasurableImpact_DoesNotDependOnWhetherCountsAreSpelledOut()
    {
        const string spelledOut =
            "Developed five C# Factory Acceptance Tests (FATs) across three hardware products, providing "
            + "circuit- and component-level validation for manufacturing and RMA diagnostics; designed "
            + "three coordinated component tests for a complex multi-board product.";
        const string numerals =
            "Authored(C#) five FATs for three products, one of which necessitated 3 separate component tests";

        BulletQualityHeuristics.HasMeasurableImpact(numerals).Should().BeTrue();
        BulletQualityHeuristics.HasMeasurableImpact(spelledOut).Should().BeTrue(
            "the same facts stated in words are the same facts");
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

    [Theory]
    [InlineData("Rebuilt the release process.", "Rebuilt")]
    [InlineData("Led the migration.", "Led")]
    [InlineData("Mentored two interns.", "Mentored")]
    public void OpeningAction_WithAVerbFirst_NamesIt(string text, string expected)
    {
        BulletQualityHeuristics.OpeningAction(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("Responsible for the release process.")]
    [InlineData("The release process was rebuilt.")]
    public void OpeningAction_WithAnAssignmentOrAPassiveOpener_IsNull(string text)
    {
        BulletQualityHeuristics.OpeningAction(text).Should().BeNull();
    }

    /// <summary>
    /// A resume elides its subject, so any verb describing the work reads as the author's. These
    /// are the bullets an allowlist of "ownership verbs" used to reject, each of them plainly
    /// personal work.
    /// </summary>
    [Theory]
    [InlineData("Led the migration.")]
    [InlineData("Mentored two interns through weekly 1:1s and code reviews.")]
    [InlineData("Developed and maintained C# interoperability wrappers.")]
    [InlineData("Implemented scalable data models and a Roslyn-based analyzer.")]
    [InlineData("Provisioned and configured the software team's first CI/CD server.")]
    [InlineData("Attended the migration planning and rewrote the rollout order.")]
    public void SharedCreditMarker_WhenTheBulletDescribesItsAuthorsWork_IsNull(string text)
    {
        BulletQualityHeuristics.SharedCreditMarker(text).Should().BeNull();
    }

    [Theory]
    [InlineData("We led the migration.", "we")]
    [InlineData("Our team led the migration.", "our team")]
    [InlineData("Assisted with the migration.", "assisted with")]
    [InlineData("Contributed to the rollout plan.", "contributed to")]
    [InlineData("Participated in the design review.", "participated in")]
    public void SharedCreditMarker_WhenCreditIsGivenAway_NamesTheWording(string text, string expected)
    {
        BulletQualityHeuristics.SharedCreditMarker(text).Should().Be(expected);
    }

    [Fact]
    public void SharedCreditMarker_DoesNotReadAPossessiveAsSharedCredit()
    {
        BulletQualityHeuristics.SharedCreditMarker("Provisioned the software team's first CI/CD server.")
            .Should().BeNull("\"the team's server\" says whose server it was, not who built it");
    }

    [Fact]
    public void SharedCreditMarker_DoesNotMatchInsideALongerWord()
    {
        BulletQualityHeuristics.SharedCreditMarker("Weekly releases were automated.")
            .Should().BeNull("\"we\" inside \"weekly\" gives nothing away");
    }

    [Fact]
    public void ProperNoun_NamesTheParticularThingOrNothing()
    {
        BulletQualityHeuristics.ProperNoun("Rebuilt the Apollo release pipeline.").Should().Be("Apollo");
        BulletQualityHeuristics.ProperNoun("Rebuilt the release pipeline.").Should().BeNull();
    }

    [Fact]
    public void MeasurableImpact_QuotesTheFigureItFound()
    {
        BulletQualityHeuristics.MeasurableImpact("Cut build times by 40%.").Should().Be("40%");
        BulletQualityHeuristics.MeasurableImpact("Led the migration.").Should().BeNull();
        BulletQualityHeuristics.MeasurableImpact("Authored five acceptance tests.").Should().Be("five");
        BulletQualityHeuristics.MeasurableImpact("Mentored twenty-five engineers.").Should().Be("twenty-five",
            "a compound number is quoted whole rather than clipped to its tens");
    }

    [Fact]
    public void IsSpecific_IgnoresTheCapitalThatOnlyStartsTheSentence()
    {
        BulletQualityHeuristics.IsSpecific("Improved reliability.").Should().BeFalse();
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
