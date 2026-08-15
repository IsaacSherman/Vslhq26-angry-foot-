using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class BulletQualityScorerTests
{
    private static Bullet Bullet(string text, string[]? technologies = null, string[]? jobCategories = null)
        => new()
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            Technologies = (technologies ?? []).ToList(),
            JobCategories = (jobCategories ?? []).ToList()
        };

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
    public void Score_WithNothingEarned_IsZeroAndSaysWhyForEachMiss()
    {
        var quality = BulletQualityScorer.Score(Bullet("responsible for various things"));

        quality.Score.Should().Be(0);
        quality.Signals.Should().OnlyContain(x => !x.Earned);
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
        quality.Diagnostics.Should().Contain(x => x.Message.Contains("runs to"));
    }

    [Fact]
    public void Score_WeakOpenerIsNamedInTheDiagnostic()
    {
        var quality = BulletQualityScorer.Score(Bullet("Responsible for the deployment pipeline."));

        Earned(quality, BulletQualitySignals.OpensWithAction).Should().BeFalse();
        quality.Diagnostics.Should().Contain(x => x.Message.Contains("responsible for"));
    }

    [Fact]
    public void Score_CollectiveWordingLosesTheOwnershipSignal()
    {
        Earned(BulletQualityScorer.Score(Bullet("We led the migration to Azure.")), BulletQualitySignals.Ownership)
            .Should().BeFalse("a reader cannot tell which part was the candidate's");

        Earned(BulletQualityScorer.Score(Bullet("Led the migration to Azure.")), BulletQualitySignals.Ownership)
            .Should().BeTrue();
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
