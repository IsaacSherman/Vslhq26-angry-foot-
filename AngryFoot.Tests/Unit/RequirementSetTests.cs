using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// Which extracted terms are one requirement and which are two. Getting this wrong is expensive in
/// both directions: splitting one requirement across two rows costs coverage points for a
/// distinction nobody drew, and merging two costs the user a requirement they were never told about.
/// </summary>
public class RequirementSetTests
{
    private static JobAnalysisDto Analysis(
        string[]? required = null,
        string[]? preferred = null,
        string[]? technologies = null)
        => new(required ?? [], preferred ?? [], technologies ?? [], [], [], null, null);

    private static IReadOnlyList<Requirement> From(
        string[]? required = null,
        string[]? preferred = null,
        string[]? technologies = null)
        => RequirementSet.From(Analysis(required, preferred, technologies));

    public class Merging
    {
        [Fact]
        public void AVendorPrefixedNameIsTheSameRequirementAsThePlainOne()
        {
            var requirements = From(required: ["Microsoft Azure"], technologies: ["Azure"]);

            requirements.Should().ContainSingle();
            requirements[0].Term.Should().Be("Azure", "the shorter wording is the one a bullet is likelier to have used");
            requirements[0].MergedFrom.Should().Contain("Microsoft Azure");
        }

        [Fact]
        public void AMergedRequirementIsCountedOnceRatherThanTwice()
        {
            var split = From(required: ["Microsoft Azure"], technologies: ["Azure"]);
            var single = From(technologies: ["Azure"]);

            CoverageScore.From(EvidenceCoverageEngine.Evaluate(split, []))
                .TotalWeight
                .Should().Be(CoverageScore.From(EvidenceCoverageEngine.Evaluate(single, [])).TotalWeight);
        }

        [Fact]
        public void AMergedRequirementKeepsTheHighestWeight()
        {
            var requirements = From(required: ["Microsoft Azure"], preferred: ["Azure"]);

            requirements.Should().ContainSingle();
            requirements[0].Weight.Should().Be(2, "a posting that requires it elsewhere requires it");
        }

        [Fact]
        public void PhrasingAroundARequirementIsNotPartOfIt()
        {
            var requirements = From(required: ["Experience with Kubernetes"], technologies: ["Kubernetes"]);

            requirements.Should().ContainSingle();
            requirements[0].Term.Should().Be("Kubernetes");
        }

        [Fact]
        public void AYearsOfPrefixIsNotPartOfTheRequirement()
        {
            var requirements = From(required: ["5+ years of Python"], technologies: ["Python"]);

            requirements.Should().ContainSingle();
            requirements[0].Term.Should().Be("Python");
        }

        [Fact]
        public void MergingIsNotSensitiveToTheOrderThePostingListedTerms()
        {
            var oneWay = From(required: ["Azure"], technologies: ["Microsoft Azure"]);
            var theOther = From(required: ["Microsoft Azure"], technologies: ["Azure"]);

            oneWay.Single().Term.Should().Be(theOther.Single().Term);
        }
    }

    public class NotMerging
    {
        [Fact]
        public void ATermInsideALongerWordIsADifferentRequirement()
        {
            var requirements = From(technologies: ["Java", "JavaScript"]);

            requirements.Should().HaveCount(2, "\"Java\" is not a word inside \"JavaScript\"");
        }

        [Fact]
        public void AProductBuiltOnAnotherIsADifferentRequirement()
        {
            var requirements = From(technologies: ["Azure", "Azure DevOps"]);

            requirements.Should().HaveCount(2, "knowing Azure is not knowing Azure DevOps");
        }

        [Fact]
        public void ANarrowerRequirementIsLeftAloneRatherThanFoldedIntoTheBroaderOne()
        {
            var requirements = From(required: ["Kubernetes administration"], technologies: ["Kubernetes"]);

            // Deliberately conservative, and the same rule that keeps "Azure DevOps" out of "Azure":
            // nothing syntactic tells a longer phrasing of one ask from a genuinely narrower one, and
            // dropping a requirement the user pasted in is worse than scoring a narrow one twice.
            requirements.Should().HaveCount(2);
        }

        [Fact]
        public void UnrelatedRequirementsStayApart()
        {
            var requirements = From(required: ["c#"], preferred: ["docker"], technologies: ["azure"]);

            requirements.Should().HaveCount(3);
        }

        [Fact]
        public void APunctuatedTechnologyKeepsTheCharactersThatIdentifyIt()
        {
            var requirements = From(technologies: ["C", "C#", "C++", ".NET"]);

            requirements.Should().HaveCount(4);
        }
    }

    public class MatchingAfterAMerge
    {
        private static Bullet Bullet(string text, string[]? technologies = null)
            => new()
            {
                Id = Guid.NewGuid(),
                BulletText = text,
                Technologies = (technologies ?? []).ToList()
            };

        [Fact]
        public void ABulletUsingEitherWordingCounts()
        {
            var requirements = From(required: ["Microsoft Azure"], technologies: ["Azure"]);
            var spelledOut = Bullet("Migrated 40 services to Microsoft Azure.");
            var shortForm = Bullet("Migrated 40 services to Azure.");

            EvidenceCoverageEngine.Evaluate(requirements, [spelledOut]).Single()
                .Strength.Should().Be(EvidenceStrengthDto.Strong);
            EvidenceCoverageEngine.Evaluate(requirements, [shortForm]).Single()
                .Strength.Should().Be(EvidenceStrengthDto.Strong);
        }

        [Fact]
        public void ABulletMatchingOnlyTheMergedWordingStillCounts()
        {
            // "CI/CD" and "CI CD" are one requirement, and the row shows one of them - but a bullet
            // that wrote the other is whole-word matched by neither unless every merged wording is
            // tried. This is what stops merging from costing a match.
            var requirements = From(required: ["CI CD"], technologies: ["CI/CD"]);
            var bullet = Bullet("Built the CI/CD pipeline that cut releases to 10 minutes.");

            var evidence = EvidenceCoverageEngine.Evaluate(requirements, [bullet]).Single();

            evidence.Strength.Should().Be(EvidenceStrengthDto.Strong);
            evidence.Citations.Single().MatchedTerm.Should().Be("CI/CD");
        }

        [Fact]
        public void ABulletTaggedWithTheMergedWordingStillCounts()
        {
            var requirements = From(required: ["Experience with Kubernetes"], technologies: ["Kubernetes"]);
            var bullet = Bullet("Ran the cluster.", technologies: ["Kubernetes"]);

            EvidenceCoverageEngine.Evaluate(requirements, [bullet]).Single()
                .Strength.Should().Be(EvidenceStrengthDto.Strong);
        }

        [Fact]
        public void TheRowSaysWhatWasMergedIntoIt()
        {
            var requirements = From(required: ["Microsoft Azure"], technologies: ["Azure"]);

            var row = EvidenceCoverageEngine.Evaluate(requirements, []).Single().ToDto();

            row.MergedFrom.Should().Contain("Microsoft Azure");
            row.Why.Reasoning.Should().Contain("counted here as the same requirement rather than twice");
        }

        [Fact]
        public void AnOrdinaryRequirementReportsNoMerge()
        {
            var row = EvidenceCoverageEngine.Evaluate(From(technologies: ["Azure"]), []).Single().ToDto();

            row.MergedFrom.Should().BeNull();
            row.Why.Reasoning.Should().NotContain("same requirement");
        }
    }
}
