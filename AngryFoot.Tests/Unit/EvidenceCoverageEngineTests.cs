using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// The deterministic half of evidence coverage: requirement weighting, strength, and the score
/// derived from them. Everything here runs with no database and no AI, which is the same path the
/// product takes when neither is configured.
/// </summary>
public class EvidenceCoverageEngineTests
{
    private static Bullet Bullet(string text, string[]? skills = null, string[]? technologies = null)
        => new()
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            Skills = (skills ?? []).ToList(),
            Technologies = (technologies ?? []).ToList()
        };

    private static JobAnalysisDto Analysis(
        string[]? required = null,
        string[]? preferred = null,
        string[]? technologies = null)
        => new(required ?? [], preferred ?? [], technologies ?? [], [], [], null, null);

    private static IReadOnlyList<RequirementEvidence> Evaluate(JobAnalysisDto analysis, params Bullet[] bullets)
        => EvidenceCoverageEngine.Evaluate(RequirementSet.From(analysis), bullets);

    /// <summary>An index that scores every requirement against every bullet at <paramref name="confidence"/>.</summary>
    private static SemanticEvidenceIndex Semantic(double confidence, JobAnalysisDto analysis, params Bullet[] bullets)
        => new(RequirementSet.From(analysis)
            .SelectMany(requirement => bullets.Select(bullet => (requirement.Term, bullet.Id)))
            .ToDictionary(key => key, _ => confidence));

    public class Weighting
    {
        [Fact]
        public void RequiredAndTechnologyWeighDoublePreferred()
        {
            var requirements = RequirementSet.From(Analysis(
                required: ["c#"], preferred: ["docker"], technologies: ["azure"]));

            requirements.Single(x => x.Term == "c#").Weight.Should().Be(2);
            requirements.Single(x => x.Term == "azure").Weight.Should().Be(2);
            requirements.Single(x => x.Term == "docker").Weight.Should().Be(1);
        }

        [Fact]
        public void ATermListedTwiceBecomesOneRequirementAtItsHighestWeight()
        {
            var requirements = RequirementSet.From(Analysis(required: ["Azure"], preferred: ["azure"]));

            requirements.Should().ContainSingle()
                .Which.Should().Match<Requirement>(x => x.Weight == 2 && x.Kind == RequirementKindDto.Required);
        }

        [Fact]
        public void RequiredOutranksTechnologyWhenBothClaimTheSameTerm()
        {
            RequirementSet.From(Analysis(required: ["Azure"], technologies: ["Azure"]))
                .Should().ContainSingle()
                .Which.Kind.Should().Be(RequirementKindDto.Required);
        }

        [Fact]
        public void BlankTermsAreDropped()
        {
            RequirementSet.From(Analysis(required: ["c#", "   ", ""])).Should().ContainSingle();
        }
    }

    public class Strength
    {
        [Fact]
        public void ATaggedSkillIsStrongEvenWithoutAMetric()
        {
            var evidence = Evaluate(
                Analysis(required: ["c#"]),
                Bullet("Optimized the data layer.", skills: ["C#"]));

            evidence.Single().Strength.Should().Be(EvidenceStrengthDto.Strong,
                "enrichment read the bullet as being about C#, not merely mentioning it");
        }

        [Fact]
        public void ProseWithAMetricIsStrong()
        {
            var evidence = Evaluate(
                Analysis(required: ["kubernetes"]),
                Bullet("Cut Kubernetes rollout time by 60%."));

            evidence.Single().Strength.Should().Be(EvidenceStrengthDto.Strong);
        }

        [Fact]
        public void ProseWithoutAMetricIsWeak()
        {
            var evidence = Evaluate(
                Analysis(required: ["kubernetes"]),
                Bullet("Worked with Kubernetes across several services."));

            var single = evidence.Single();
            single.Strength.Should().Be(EvidenceStrengthDto.Weak);
            single.Citations.Should().ContainSingle()
                .Which.Because.Should().Contain("does not quantify");
        }

        [Fact]
        public void ATermInsideALongerWordIsNotEvidence()
        {
            var evidence = Evaluate(
                Analysis(required: ["aws"]),
                Bullet("Reviewed the relevant laws and drafted 3 policies."));

            evidence.Single().Strength.Should().Be(EvidenceStrengthDto.Missing,
                "whole-word matching is BulletEvidence's rule and this layer does not loosen it");
        }

        [Fact]
        public void TheStrongestSupportingBulletSetsTheStrength()
        {
            var evidence = Evaluate(
                Analysis(required: ["azure"]),
                Bullet("Explored Azure options."),
                Bullet("Migrated 40 services to Azure."));

            evidence.Single().Strength.Should().Be(EvidenceStrengthDto.Strong);
        }

        [Fact]
        public void PreferredSkillsAreMatchedTheSameWayAsRequiredOnes()
        {
            var evidence = Evaluate(
                Analysis(preferred: ["docker"]),
                Bullet("Used Docker daily.", technologies: ["Docker"]));

            evidence.Single().Strength.Should().Be(EvidenceStrengthDto.Strong);
        }

        [Fact]
        public void EveryRequirementCarriesReasoningWhetherOrNotItIsEvidenced()
        {
            var evidence = Evaluate(
                Analysis(required: ["c#", "kubernetes"]),
                Bullet("Built C# services.", skills: ["C#"]));

            evidence.Should().AllSatisfy(x => x.Reasoning.Should().NotBeNullOrWhiteSpace());
        }

        [Fact]
        public void CitationsAreCappedSoOneRequirementCannotFloodTheReport()
        {
            var bullets = Enumerable.Range(0, 6)
                .Select(i => Bullet($"Shipped Azure workload {i}.", technologies: ["Azure"]))
                .ToArray();

            Evaluate(Analysis(technologies: ["azure"]), bullets)
                .Single().Citations.Should().HaveCount(3);
        }
    }

    public class Score
    {
        private static CoverageTotals ScoreOf(JobAnalysisDto analysis, params Bullet[] bullets)
            => CoverageScore.From(Evaluate(analysis, bullets));

        [Fact]
        public void WeakEvidenceEarnsHalfOfStrong()
        {
            var totals = ScoreOf(
                Analysis(required: ["azure"]),
                Bullet("Worked with Azure."));

            totals.EarnedWeight.Should().Be(2);
            totals.TotalWeight.Should().Be(4);
            totals.Score.Should().Be(50);
        }

        [Fact]
        public void NoRequirementsScoresZeroRatherThanDividingByZero()
        {
            var totals = ScoreOf(Analysis(), Bullet("A bullet."));

            totals.Should().Be(new CoverageTotals(0, 0, 0));
        }

        /// <summary>
        /// The score has to be reproducible from the table beneath it, which is only true while
        /// earned and total stay whole numbers. This is the invariant the DTO advertises.
        /// </summary>
        [Theory]
        [InlineData(new[] { "c#", "kubernetes" }, new string[0])]
        [InlineData(new[] { "kubernetes" }, new[] { "docker" })]
        [InlineData(new string[0], new[] { "docker", "azure", "sql" })]
        public void ScoreIsAlwaysRoundOfEarnedOverTotal(string[] required, string[] preferred)
        {
            var totals = ScoreOf(
                Analysis(required: required, preferred: preferred),
                Bullet("Used Docker daily.", technologies: ["Docker"]),
                Bullet("Wrote C# services, cutting latency 20%."));

            var expected = totals.TotalWeight == 0
                ? 0
                : (int)Math.Round(100.0 * totals.EarnedWeight / totals.TotalWeight, MidpointRounding.AwayFromZero);

            totals.Score.Should().Be(expected);
        }

        /// <summary>
        /// The scores the retired fit heuristic produced for these inputs, pinned so replacing an
        /// opaque number with an explainable one did not quietly move everyone's number as well.
        /// </summary>
        [Fact]
        public void MatchesTheRetiredFitHeuristicWhenEveryCoveredRequirementIsStrong()
        {
            ScoreOf(
                Analysis(required: ["c#", "kubernetes"]),
                Bullet("Optimized the data layer.", skills: ["C#"]),
                Bullet("Improved deployment speed by 40% using c# tooling."))
                .Score.Should().Be(50, "one of two equally weighted requirements is covered");

            ScoreOf(
                Analysis(required: ["kubernetes"], preferred: ["docker"]),
                Bullet("Used Docker daily.", technologies: ["Docker"]))
                .Score.Should().Be(33, "preferred skills weigh half of required skills");

            ScoreOf(Analysis(technologies: ["azure"]), Bullet("Shipped Azure workloads.", technologies: ["Azure"]))
                .Score.Should().Be(100);

            ScoreOf(Analysis(required: ["c#", "azure"]))
                .Score.Should().Be(0, "an empty library evidences nothing");
        }
    }

    /// <summary>
    /// What an embedding match may and may not do. The bar is the same one the AI reviewer clears:
    /// it may offer a bullet as related, and it may never make the resume count as having said
    /// something it does not say.
    /// </summary>
    public class SemanticMatches
    {
        private static readonly JobAnalysisDto Leadership =
            Analysis(required: ["technical leadership and mentoring"]);

        private static Bullet Mentoring()
            => Bullet("Mentored two interns through weekly 1:1s, pair programming, and code reviews.");

        [Fact]
        public void ABulletThatNeverNamesTheRequirementIsCitedWhenAnEmbeddingMatchesIt()
        {
            var bullet = Mentoring();

            var evidence = EvidenceCoverageEngine
                .Evaluate(RequirementSet.From(Leadership), [bullet], Semantic(0.87, Leadership, bullet))
                .Single();

            evidence.Strength.Should().Be(EvidenceStrengthDto.Weak);
            var citation = evidence.Citations.Single();
            citation.MatchKind.Should().Be(EvidenceMatchKindDto.Semantic);
            citation.Confidence.Should().Be(0.87);
            citation.Because.Should().Contain("0.87");
        }

        [Fact]
        public void ASemanticMatchIsNeverStrongHoweverConfident()
        {
            var bullet = Mentoring();

            var evidence = EvidenceCoverageEngine
                .Evaluate(RequirementSet.From(Leadership), [bullet], Semantic(0.999, Leadership, bullet))
                .Single();

            evidence.Strength.Should().Be(
                EvidenceStrengthDto.Weak,
                "full credit means the resume states the requirement, and a vector cannot make it say so");
        }

        [Fact]
        public void WithNoIndexTheReportIsExactlyTheLexicalOne()
        {
            var bullet = Mentoring();

            var withoutIndex = EvidenceCoverageEngine.Evaluate(RequirementSet.From(Leadership), [bullet]);
            var withEmptyIndex = EvidenceCoverageEngine.Evaluate(
                RequirementSet.From(Leadership), [bullet], SemanticEvidenceIndex.Empty);

            withoutIndex.Single().Strength.Should().Be(EvidenceStrengthDto.Missing);
            withEmptyIndex.Single().Strength.Should().Be(EvidenceStrengthDto.Missing);
        }

        [Fact]
        public void AWordMatchOutranksAnEmbeddingMatchOnTheSameBullet()
        {
            var analysis = Analysis(technologies: ["azure"]);
            var bullet = Bullet("Migrated 40 services to Azure.");

            var citation = EvidenceCoverageEngine
                .Evaluate(RequirementSet.From(analysis), [bullet], Semantic(0.99, analysis, bullet))
                .Single()
                .Citations
                .Single();

            citation.MatchKind.Should().Be(EvidenceMatchKindDto.ExactTerm);
            citation.Confidence.Should().BeNull();
        }

        [Fact]
        public void TheReasoningDoesNotClaimABulletMentionsAWordItNeverUses()
        {
            var bullet = Mentoring();

            var evidence = EvidenceCoverageEngine
                .Evaluate(RequirementSet.From(Leadership), [bullet], Semantic(0.87, Leadership, bullet))
                .Single();

            evidence.Reasoning.Should().Contain("No bullet uses that wording");
            evidence.ToDto().Why.MissingEvidence.Should().Contain(x => x.Contains("keyword screen"));
        }

        [Fact]
        public void ASemanticMatchStillEarnsOnlyHalfTheRequirementsWeight()
        {
            var bullet = Mentoring();

            var totals = CoverageScore.From(EvidenceCoverageEngine
                .Evaluate(RequirementSet.From(Leadership), [bullet], Semantic(0.87, Leadership, bullet)));

            totals.TotalWeight.Should().Be(4);
            totals.EarnedWeight.Should().Be(2);
            totals.Score.Should().Be(50);
        }
    }
}
