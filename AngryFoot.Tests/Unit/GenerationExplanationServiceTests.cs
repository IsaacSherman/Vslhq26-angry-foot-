using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class GenerationExplanationServiceTests
{
    private static Bullet Bullet(string text, string[]? technologies = null)
        => new()
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            Technologies = (technologies ?? []).ToList()
        };

    private static JobAnalysisDto Analysis(params string[] required)
        => new(required, [], [], [], [], null, null);

    private static GenerationExplanationDto Explain(
        JobAnalysisDto analysis,
        IReadOnlyList<RankedBullet> ranked,
        IReadOnlyList<RewrittenBullet> final)
        => GenerationExplanationService.Explain(analysis, ranked, final);

    private static RankedBullet Ranked(Bullet bullet, int score = 10) => new(bullet, score);

    private static RewrittenBullet Kept(Bullet bullet, string? text = null) => new(bullet, text ?? bullet.BulletText);

    [Fact]
    public void Explain_AccountsForEveryCandidateIncludingTheOnesLeftOff()
    {
        var kept = Bullet("Cut Azure spend by 30%.");
        var left = Bullet("Organised the team offsite.");

        var explanation = Explain(Analysis("azure"), [Ranked(kept), Ranked(left)], [Kept(kept)]);

        explanation.Decisions.Should().HaveCount(2);
        explanation.Decisions.Select(x => x.BulletId).Should().BeEquivalentTo([kept.Id, left.Id]);
    }

    [Fact]
    public void Explain_ABulletKeptInPlaceAndUnchangedIsSelected()
    {
        var bullet = Bullet("Cut Azure spend by 30%.");

        var decision = Explain(Analysis("azure"), [Ranked(bullet)], [Kept(bullet)]).Decisions.Single();

        decision.Kind.Should().Be(BulletDecisionKindDto.Selected);
        decision.RankerPosition.Should().Be(1);
        decision.ResumePosition.Should().Be(1);
        decision.FinalText.Should().Be(bullet.BulletText);
    }

    [Fact]
    public void Explain_ARewordedBulletIsRevisedAndKeepsBothWordings()
    {
        var bullet = Bullet("Cut Azure spend by 30%.");

        var decision = Explain(
            Analysis("azure"),
            [Ranked(bullet)],
            [Kept(bullet, "Reduced Azure spend 30% by rightsizing workloads.")]).Decisions.Single();

        decision.Kind.Should().Be(BulletDecisionKindDto.Selected | BulletDecisionKindDto.Revised);
        decision.OriginalText.Should().Be("Cut Azure spend by 30%.");
        decision.FinalText.Should().Be("Reduced Azure spend 30% by rightsizing workloads.");
        decision.Why.Reasoning.Should().Contain("tailored");
    }

    [Fact]
    public void Explain_AMovedBulletIsReorderedAndNamesBothPositions()
    {
        var first = Bullet("Cut Azure spend by 30%.");
        var second = Bullet("Migrated 40 services to Azure.");

        // Deep review promoted the ranker's second pick to the top.
        var decisions = Explain(
            Analysis("azure"),
            [Ranked(first), Ranked(second)],
            [Kept(second), Kept(first)]).Decisions;

        var moved = decisions.Single(x => x.BulletId == second.Id);
        moved.Kind.Should().Be(BulletDecisionKindDto.Selected | BulletDecisionKindDto.Reordered);
        moved.RankerPosition.Should().Be(2);
        moved.ResumePosition.Should().Be(1);
        moved.Why.Reasoning.Should().Contain("2").And.Contain("1");
    }

    [Fact]
    public void Explain_ABulletBothMovedAndRewordedRecordsBoth()
    {
        var first = Bullet("Cut Azure spend by 30%.");
        var second = Bullet("Migrated 40 services to Azure.");

        var moved = Explain(
            Analysis("azure"),
            [Ranked(first), Ranked(second)],
            [Kept(second, "Moved 40 services onto Azure, halving run cost."), Kept(first)])
            .Decisions.Single(x => x.BulletId == second.Id);

        moved.Kind.Should().HaveFlag(BulletDecisionKindDto.Reordered);
        moved.Kind.Should().HaveFlag(BulletDecisionKindDto.Revised);
        moved.Kind.Should().HaveFlag(BulletDecisionKindDto.Selected);
        moved.Why.Reasoning.Should().Contain("moved it").And.Contain("tailored",
            "both things that happened belong in the reasoning, not just the one that got the badge");
    }

    [Fact]
    public void Explain_SelectedAndOmittedAreNeverBothSet()
    {
        var kept = Bullet("Cut Azure spend by 30%.");
        var left = Bullet("Organised the team offsite.");

        var decisions = Explain(Analysis("azure"), [Ranked(kept), Ranked(left)], [Kept(kept)]).Decisions;

        decisions.Should().AllSatisfy(decision =>
            decision.Kind.HasFlag(BulletDecisionKindDto.Selected)
                .Should().Be(!decision.Kind.HasFlag(BulletDecisionKindDto.Omitted)));
    }

    [Fact]
    public void Explain_RevisedAndReorderedOnlyEverAccompanySelected()
    {
        var kept = Bullet("Cut Azure spend by 30%.");
        var left = Bullet("Organised the team offsite.");

        var omitted = Explain(Analysis("azure"), [Ranked(kept), Ranked(left)], [Kept(kept)])
            .Decisions.Single(x => x.BulletId == left.Id);

        omitted.Kind.Should().Be(BulletDecisionKindDto.Omitted, "an omitted bullet underwent nothing else");
    }

    [Fact]
    public void Explain_AnOmittedBulletHasNoResumePositionAndNoFinalText()
    {
        var kept = Bullet("Cut Azure spend by 30%.");
        var left = Bullet("Organised the team offsite.");

        var decision = Explain(Analysis("azure"), [Ranked(kept), Ranked(left)], [Kept(kept)])
            .Decisions.Single(x => x.BulletId == left.Id);

        decision.Kind.Should().Be(BulletDecisionKindDto.Omitted);
        decision.ResumePosition.Should().BeNull();
        decision.FinalText.Should().BeNull();
    }

    [Fact]
    public void Explain_AnOmittedBulletThatEvidencesNothingSaysSoRatherThanBlamingTheCap()
    {
        var kept = Bullet("Cut Azure spend by 30%.");
        var left = Bullet("Organised the team offsite.");

        var decision = Explain(Analysis("azure"), [Ranked(kept), Ranked(left)], [Kept(kept)])
            .Decisions.Single(x => x.BulletId == left.Id);

        decision.Why.Reasoning.Should().Contain("evidences none");
        decision.Why.MissingEvidence.Should().BeEmpty("nothing is lost by leaving it off");
    }

    [Fact]
    public void Explain_AnOmittedBulletCoveringSomethingTheResumeMissesSaysWhatItCost()
    {
        var kept = Bullet("Cut Azure spend by 30%.");
        var left = Bullet("Tuned Kubernetes autoscaling, halving cold starts.");

        var decision = Explain(
            Analysis("azure", "kubernetes"),
            [Ranked(kept), Ranked(left)],
            [Kept(kept)]).Decisions.Single(x => x.BulletId == left.Id);

        decision.Why.MissingEvidence.Should().ContainSingle()
            .Which.Should().Contain("kubernetes", Exactly.Once());
    }

    [Fact]
    public void Explain_AnOmittedBulletCoveringOnlyWhatTheResumeAlreadyEvidencesCostsNothing()
    {
        var kept = Bullet("Cut Azure spend by 30%.");
        var left = Bullet("Migrated 40 services to Azure.");

        var decision = Explain(Analysis("azure"), [Ranked(kept), Ranked(left)], [Kept(kept)])
            .Decisions.Single(x => x.BulletId == left.Id);

        decision.Why.MissingEvidence.Should().BeEmpty(
            "the resume already evidences the only thing this bullet speaks to");
    }

    [Fact]
    public void Explain_OrdersByResumePositionThenPutsTheOmittedOnesLast()
    {
        var first = Bullet("Cut Azure spend by 30%.");
        var second = Bullet("Migrated 40 services to Azure.");
        var left = Bullet("Organised the team offsite.");

        var decisions = Explain(
            Analysis("azure"),
            [Ranked(left), Ranked(first), Ranked(second)],
            [Kept(first), Kept(second)]).Decisions;

        decisions.Select(x => x.BulletId).Should().Equal(first.Id, second.Id, left.Id);
    }

    [Fact]
    public void Explain_EveryDecisionCarriesReasoningAndCitesItsOwnBullet()
    {
        var kept = Bullet("Cut Azure spend by 30%.");
        var left = Bullet("Organised the team offsite.");

        var decisions = Explain(Analysis("azure"), [Ranked(kept), Ranked(left)], [Kept(kept)]).Decisions;

        decisions.Should().AllSatisfy(decision =>
        {
            decision.Why.Reasoning.Should().NotBeNullOrWhiteSpace();
            decision.Why.SupportingEvidence.Should().ContainSingle()
                .Which.BulletId.Should().Be(decision.BulletId);
        });
    }

    [Fact]
    public void Explain_SummaryCountsWhatHappened()
    {
        var kept = Bullet("Cut Azure spend by 30%.");
        var reworded = Bullet("Migrated 40 services to Azure.");
        var left = Bullet("Organised the team offsite.");

        var explanation = Explain(
            Analysis("azure"),
            [Ranked(kept), Ranked(reworded), Ranked(left)],
            [Kept(kept), Kept(reworded, "Moved 40 services onto Azure.")]);

        explanation.Summary.Should().Contain("2 of the 3");
        explanation.Summary.Should().Contain("1 reworded");
        explanation.Summary.Should().Contain("1 left off");
    }

    [Fact]
    public void Explain_WithNoCandidates_ProducesAnEmptyButValidExplanation()
    {
        var explanation = Explain(Analysis("azure"), [], []);

        explanation.Decisions.Should().BeEmpty();
        explanation.Summary.Should().NotBeNullOrWhiteSpace();
    }
}
