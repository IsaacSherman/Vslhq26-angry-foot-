using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class BulletQualityScorerTests
{
    private static Bullet Bullet(
        string text,
        string[]? technologies = null,
        string[]? jobCategories = null,
        string[]? acknowledged = null)
        => new()
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            Technologies = (technologies ?? []).ToList(),
            JobCategories = (jobCategories ?? []).ToList(),
            AcknowledgedQualitySignals = (acknowledged ?? []).ToList()
        };

    private static BulletQualitySignalDto Signal(BulletQualityDto quality, string name)
        => quality.Signals.Single(x => x.Name == name);

    private static bool Earned(BulletQualityDto quality, string name)
        => quality.Signals.Single(x => x.Name == name).Earned;

    [Fact]
    public void Score_IsTheSumOfTheEarnedSignalWeights()
    {
        var quality = BulletQualityScorer.Score(
            Bullet("Led the Apollo migration, cutting deploy time by 40%.", technologies: ["Azure"], jobCategories: ["Engineering"]));

        quality.Score.Should().Be(quality.Signals.Where(x => x.Earned).Sum(x => x.Weight));
    }

    [Fact]
    public void Score_WithEverySignalEarned_IsOneHundred()
    {
        var quality = BulletQualityScorer.Score(
            Bullet("Led the Apollo migration, cutting deploy time by 40%.", technologies: ["Azure"], jobCategories: ["Engineering"]));

        quality.Score.Should().Be(100);
        quality.Signals.Should().OnlyContain(x => x.Earned);
    }

    [Fact]
    public void Score_WithOnlyThePresumedSignalEarned_ScoresJustThat()
    {
        var quality = BulletQualityScorer.Score(Bullet("responsible for various things"));

        quality.Score.Should().Be(5, "ownership is presumed; nothing else about this bullet earns anything");
        quality.Signals.Where(x => x.Earned).Select(x => x.Name).Should().Equal(BulletQualitySignals.Ownership);
        quality.Diagnostics.Should().NotBeEmpty();
        quality.Diagnostics.Should().AllSatisfy(x => x.Why.Reasoning.Should().NotBeNullOrWhiteSpace());
    }

    [Fact]
    public void Score_EverySignalCarriesAUserFacingLabel()
    {
        var quality = BulletQualityScorer.Score(Bullet("Anything."));

        quality.Signals.Should().AllSatisfy(x => x.Label.Should().NotBeNullOrWhiteSpace());
        quality.Signals.Select(x => x.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Score_TechnologySignalReadsEnrichmentAsWellAsWording()
    {
        var tagged = BulletQualityScorer.Score(Bullet("Rebuilt the ingest pipeline.", technologies: ["Kafka"]));
        var untagged = BulletQualityScorer.Score(Bullet("Rebuilt the ingest pipeline."));

        Earned(tagged, BulletQualitySignals.Technology).Should().BeTrue(
            "the tag records a technology the wording happens not to name");
        Earned(untagged, BulletQualitySignals.Technology).Should().BeFalse();
    }

    [Fact]
    public void Score_RoleRelevanceComesFromEnrichmentOnly()
    {
        Earned(BulletQualityScorer.Score(Bullet("Rebuilt the pipeline.", jobCategories: ["Data Engineering"])),
            BulletQualitySignals.RoleRelevance).Should().BeTrue();

        Earned(BulletQualityScorer.Score(Bullet("Rebuilt the pipeline.")),
            BulletQualitySignals.RoleRelevance).Should().BeFalse();
    }

    [Fact]
    public void Score_ScoresAlternativeWordingAgainstTheBulletsOwnEnrichment()
    {
        var bullet = Bullet("Rebuilt the ingest pipeline.", technologies: ["Kafka"], jobCategories: ["Data Engineering"]);

        var quality = BulletQualityScorer.Score(bullet, "Rebuilt the Kafka ingest pipeline, cutting lag by 90%.");

        quality.Score.Should().Be(100, "a revision restates the same work, so its enrichment still applies");
        quality.WordCount.Should().Be(9, "a figure is a word to the reader counting them");
    }

    [Fact]
    public void Score_FlagsALongBulletWithoutCostingItPoints()
    {
        var longText = "Led the Apollo migration, cutting deploy time by 40% "
            + string.Join(" ", Enumerable.Repeat("across many different teams and systems", 8));

        var quality = BulletQualityScorer.Score(Bullet(longText, technologies: ["Azure"], jobCategories: ["Engineering"]));

        quality.Score.Should().Be(100, "length is advice, not a penalty");
        quality.Diagnostics.Should().Contain(x => x.Message.Contains("stops being skimmed"));
    }

    [Fact]
    public void Score_WeakOpenerIsNamedInTheDiagnostic()
    {
        var quality = BulletQualityScorer.Score(Bullet("Responsible for the deployment pipeline."));

        Earned(quality, BulletQualitySignals.OpensWithAction).Should().BeFalse();
        quality.Diagnostics.Should().Contain(x => x.Message.Contains("responsible for"));
    }

    /// <summary>
    /// The bullets an ownership allowlist used to reject. A resume elides its subject, so each of
    /// these reads as the author's own work and scores as such.
    /// </summary>
    [Theory]
    [InlineData("Mentored two interns through weekly 1:1s and code reviews.")]
    [InlineData("Developed and maintained C# interoperability wrappers.")]
    [InlineData("Provisioned and configured the software team's first CI/CD server.")]
    public void Score_PresumesOwnershipOfWorkTheBulletDescribes(string text)
    {
        Earned(BulletQualityScorer.Score(Bullet(text)), BulletQualitySignals.Ownership).Should().BeTrue();
    }

    [Fact]
    public void Score_OnlyLosesOwnershipWhenCreditIsGivenAway()
    {
        var shared = BulletQualityScorer.Score(Bullet("We led the migration to Azure."));

        Earned(shared, BulletQualitySignals.Ownership).Should().BeFalse();
        Signal(shared, BulletQualitySignals.Ownership).Detail.Should().Contain("\"we\"",
            "the check has to name the wording it objected to");
    }

    [Fact]
    public void Score_OwnershipIsWorthFivePointsBecauseTheTextCannotSettleIt()
    {
        Signal(BulletQualityScorer.Score(Bullet("Anything.")), BulletQualitySignals.Ownership)
            .Weight.Should().Be(5);
    }

    [Fact]
    public void Score_OwnershipIsTheOnlyContestableSignal()
    {
        var quality = BulletQualityScorer.Score(Bullet("Anything."));

        quality.Signals.Where(x => x.IsContestable).Select(x => x.Name)
            .Should().Equal(BulletQualitySignals.Ownership);
    }

    [Fact]
    public void Score_ADisputedSignalScoresAndIsReportedAsTheAuthorsCall()
    {
        var quality = BulletQualityScorer.Score(
            Bullet("We led the migration to Azure.", acknowledged: [BulletQualitySignals.Ownership]));

        var ownership = Signal(quality, BulletQualitySignals.Ownership);
        ownership.Earned.Should().BeTrue();
        ownership.IsDeclared.Should().BeTrue("the author settled it; the wording did not");
        ownership.Detail.Should().Contain("Disputed by the author.");
    }

    [Fact]
    public void Score_ADisputedSignalStopsBeingRaised()
    {
        var quality = BulletQualityScorer.Score(
            Bullet("We led the migration to Azure.", acknowledged: [BulletQualitySignals.Ownership]));

        quality.Diagnostics.Should().NotContain(x => x.Message.Contains("Credit reads as shared"),
            "being told twice that a check disagrees is what turns an assessment into an argument");
    }

    [Fact]
    public void Score_DisputingASignalTheTextCanDecideDoesNothing()
    {
        var quality = BulletQualityScorer.Score(
            Bullet("Led the migration.", acknowledged: [BulletQualitySignals.MeasurableImpact]));

        Earned(quality, BulletQualitySignals.MeasurableImpact).Should().BeFalse(
            "whether a figure is present is not open to opinion");
    }

    [Fact]
    public void Score_EverySignalReportsWhatTheCheckSaw()
    {
        var quality = BulletQualityScorer.Score(
            Bullet("Led the Apollo migration, cutting deploy time by 40%.", technologies: ["Azure"]));

        quality.Signals.Should().AllSatisfy(x => x.Detail.Should().NotBeNullOrWhiteSpace());
        Signal(quality, BulletQualitySignals.MeasurableImpact).Detail.Should().Contain("40%");
        Signal(quality, BulletQualitySignals.Specificity).Detail.Should().Contain("Apollo");
        Signal(quality, BulletQualitySignals.OpensWithAction).Detail.Should().Contain("Led");
        Signal(quality, BulletQualitySignals.Technology).Detail.Should().Contain("Azure");
    }

    [Fact]
    public void Score_DistinguishesNotEnrichedFromNothingFound()
    {
        var pending = Bullet("Led the migration.");
        pending.EnrichmentState = EnrichmentState.Pending;

        var enriched = Bullet("Led the migration.");
        enriched.EnrichmentState = EnrichmentState.Enriched;

        Signal(BulletQualityScorer.Score(pending), BulletQualitySignals.RoleRelevance)
            .Detail.Should().Contain("Not enriched yet");
        Signal(BulletQualityScorer.Score(enriched), BulletQualitySignals.RoleRelevance)
            .Detail.Should().Contain("no job family");
    }

    [Fact]
    public void Score_EveryDiagnosticPointsAtTheBulletItIsAbout()
    {
        var bullet = Bullet("responsible for various things");

        var quality = BulletQualityScorer.Score(bullet);

        quality.Diagnostics.Should().AllSatisfy(diagnostic =>
        {
            diagnostic.BulletIds.Should().ContainSingle().Which.Should().Be(bullet.Id);
            diagnostic.Why.SupportingEvidence.Should().ContainSingle()
                .Which.BulletId.Should().Be(bullet.Id);
        });
    }
}
