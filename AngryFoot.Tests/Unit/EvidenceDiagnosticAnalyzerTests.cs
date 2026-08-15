using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Application.Evidence.Diagnostics;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AwesomeAssertions;
using Moq;

namespace AngryFoot.Tests.Unit;

public class EvidenceDiagnosticAnalyzerTests
{
    private static Bullet Bullet(string text, string[]? skills = null, string[]? technologies = null)
        => new()
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            Skills = (skills ?? []).ToList(),
            Technologies = (technologies ?? []).ToList()
        };

    private static JobAnalysisDto Analysis(string[]? required = null, string[]? preferred = null)
        => new(required ?? [], preferred ?? [], [], [], [], null, null);

    private static DiagnosticContext Context(JobAnalysisDto analysis, DiagnosticScope scope)
        => new(analysis, EvidenceCoverageEngine.Evaluate(RequirementSet.From(analysis), scope.Bullets), scope);

    private static Task<IReadOnlyList<CoverageDiagnosticDto>> RunAsync(
        IEvidenceDiagnosticAnalyzer analyzer,
        DiagnosticContext context)
        => analyzer.AnalyzeAsync(context, TestContext.Current.CancellationToken);

    public class MissingSkill
    {
        [Fact]
        public async Task RequiredIsAWarningAndPreferredIsASuggestion()
        {
            var context = Context(
                Analysis(required: ["kubernetes"], preferred: ["terraform"]),
                DiagnosticScope.Library([Bullet("Built C# services.")]));

            var diagnostics = await RunAsync(new MissingSkillAnalyzer(), context);

            diagnostics.Single(x => x.Message.Contains("kubernetes")).Severity
                .Should().Be(DiagnosticSeverityDto.Warning);
            diagnostics.Single(x => x.Message.Contains("terraform")).Severity
                .Should().Be(DiagnosticSeverityDto.Suggestion);
        }

        [Fact]
        public async Task EveryDiagnosticSaysWhatToWrite()
        {
            var context = Context(
                Analysis(required: ["kubernetes"]),
                DiagnosticScope.Library([Bullet("Built C# services.")]));

            var diagnostic = (await RunAsync(new MissingSkillAnalyzer(), context)).Single();

            diagnostic.Why.Requirement.Should().Be("kubernetes");
            diagnostic.Why.MissingEvidence.Should().NotBeEmpty();
            diagnostic.Why.Reasoning.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task HoldsBackTheTailAndSaysHowMuchItHeldBack()
        {
            var context = Context(
                Analysis(required: ["a1", "b2", "c3", "d4", "e5", "f6", "g7"]),
                DiagnosticScope.Library([Bullet("Unrelated work.")]));

            var diagnostics = await RunAsync(new MissingSkillAnalyzer(), context);

            diagnostics.Should().HaveCount(DiagnosticBudget.MaxPerCode + 1);
            diagnostics.Last().Should().Match<CoverageDiagnosticDto>(x =>
                x.Severity == DiagnosticSeverityDto.Info && x.Message.StartsWith("2 more"));
        }

        [Fact]
        public async Task SaysNothingWhenEveryRequirementIsEvidenced()
        {
            var context = Context(
                Analysis(required: ["c#"]),
                DiagnosticScope.Library([Bullet("Built C# services.", skills: ["C#"])]));

            (await RunAsync(new MissingSkillAnalyzer(), context)).Should().BeEmpty();
        }
    }

    public class WeakEvidence
    {
        [Fact]
        public async Task FiresOnlyForMentionedRequirementsAndCitesTheBullet()
        {
            var mentioned = Bullet("Worked with Kubernetes on several teams.");
            var context = Context(
                Analysis(required: ["kubernetes", "c#", "terraform"]),
                DiagnosticScope.Library([mentioned, Bullet("Cut C# build times by 40%.")]));

            var diagnostics = await RunAsync(new WeakEvidenceAnalyzer(), context);

            var diagnostic = diagnostics.Should().ContainSingle().Subject;
            diagnostic.Message.Should().Contain("kubernetes");
            diagnostic.Severity.Should().Be(DiagnosticSeverityDto.Suggestion);
            diagnostic.BulletIds.Should().ContainSingle().Which.Should().Be(mentioned.Id);
        }
    }

    public class MeasurableImpact
    {
        [Fact]
        public async Task FlagsBulletsWithoutAFigureAndLeavesQuantifiedOnesAlone()
        {
            var vague = Bullet("Led the platform migration.");
            var context = Context(
                Analysis(),
                DiagnosticScope.Library([vague, Bullet("Cut deploy time by 80%.")]));

            var diagnostics = await RunAsync(new MeasurableImpactAnalyzer(), context);

            diagnostics.Should().ContainSingle().Which.BulletIds.Should().ContainSingle()
                .Which.Should().Be(vague.Id);
        }

        [Fact]
        public async Task NamesTheBulletSoARunOfThemStaysReadable()
        {
            var context = Context(
                Analysis(),
                DiagnosticScope.Library([
                    Bullet("Led the platform migration."),
                    Bullet("Organised the team offsite.")
                ]));

            var messages = (await RunAsync(new MeasurableImpactAnalyzer(), context))
                .Select(x => x.Message)
                .ToArray();

            messages.Should().OnlyHaveUniqueItems(
                "a list of identical messages is a list the user has to open one by one");
            messages.Should().Contain(x => x.Contains("Led the platform migration"));
        }

        [Fact]
        public async Task ShortensALongBulletOnAWordBoundary()
        {
            const string text = "Led the platform migration across every team in the organisation over eighteen months.";
            var context = Context(Analysis(), DiagnosticScope.Library([Bullet(text)]));

            var message = (await RunAsync(new MeasurableImpactAnalyzer(), context)).Single().Message;

            message.Should().Contain("...").And.NotContain("eighteen");

            // Whatever was kept must be a whole-word prefix of the bullet: the original continues
            // with a space at exactly that point, so no word was cut in half.
            var kept = message[(message.IndexOf('"') + 1)..message.IndexOf("...", StringComparison.Ordinal)];
            text.Should().StartWith(kept);
            text[kept.Length].Should().Be(' ');
        }

        [Fact]
        public async Task PutsBulletsAlreadyCarryingEvidenceFirst()
        {
            var cited = Bullet("Worked with Azure daily.");
            var uncited = Bullet("Organised the team offsite.");
            var context = Context(
                Analysis(required: ["azure"]),
                DiagnosticScope.Library([uncited, cited]));

            var diagnostics = await RunAsync(new MeasurableImpactAnalyzer(), context);

            diagnostics[0].BulletIds.Should().Contain(cited.Id,
                "a number on a bullet that already carries evidence pays off twice");
            diagnostics[0].Why.Reasoning.Should().Contain("half credit");
        }
    }

    public class OverusedWording
    {
        [Fact]
        public async Task FlagsAWordUsedByThreeBullets()
        {
            var context = Context(
                Analysis(),
                DiagnosticScope.Library([
                    Bullet("Managed the release calendar."),
                    Bullet("Managed the vendor relationship."),
                    Bullet("Managed the migration plan.")
                ]));

            var diagnostics = await RunAsync(new OverusedWordingAnalyzer(), context);

            diagnostics.Should().Contain(x => x.Message.Contains("\"managed\"") && x.Message.Contains("3 bullets"));
        }

        [Fact]
        public async Task DoesNotFlagATechnologyTheJobActuallyAsksFor()
        {
            var context = Context(
                Analysis(required: ["azure"]),
                DiagnosticScope.Library([
                    Bullet("Migrated billing to Azure."),
                    Bullet("Tuned Azure networking."),
                    Bullet("Cut Azure spend by 30%.")
                ]));

            var diagnostics = await RunAsync(new OverusedWordingAnalyzer(), context);

            diagnostics.Should().NotContain(x => x.Message.Contains("\"azure\""),
                "three bullets evidencing Azure is the point, not filler");
        }

        [Fact]
        public async Task DoesNotFlagATaggedTechnologyEvenWhenTheJobIsSilentAboutIt()
        {
            var context = Context(
                Analysis(),
                DiagnosticScope.Library([
                    Bullet("Shipped the Docker build.", technologies: ["Docker"]),
                    Bullet("Tuned Docker layers.", technologies: ["Docker"]),
                    Bullet("Documented Docker usage.", technologies: ["Docker"])
                ]));

            (await RunAsync(new OverusedWordingAnalyzer(), context))
                .Should().NotContain(x => x.Message.Contains("\"docker\""));
        }

        [Fact]
        public async Task FlagsAWeakOpener()
        {
            var weak = Bullet("Responsible for the deployment pipeline.");
            var context = Context(Analysis(), DiagnosticScope.Library([weak]));

            var diagnostic = (await RunAsync(new OverusedWordingAnalyzer(), context)).Should().ContainSingle().Subject;

            diagnostic.BulletIds.Should().ContainSingle().Which.Should().Be(weak.Id);
            diagnostic.Message.Should().Contain("Responsible for the deployment pipeline",
                "the headline has to say which bullet it means");
            diagnostic.Why.Reasoning.Should().Contain("responsible for",
                "the offending phrase belongs in the explanation");
        }
    }

    public class BulletOrdering
    {
        [Fact]
        public async Task SaysNothingAboutTheLibrary()
        {
            var context = Context(
                Analysis(required: ["azure"]),
                DiagnosticScope.Library([Bullet("Unrelated."), Bullet("Migrated 40 services to Azure.")]));

            (await RunAsync(new BulletOrderingAnalyzer(), context)).Should().BeEmpty(
                "library order is a modified date, not a decision the user made");
        }

        [Fact]
        public async Task FlagsAStrongerBulletPrintedBelowAWeakerOne()
        {
            var weak = Bullet("Organised the team offsite.");
            var strong = Bullet("Migrated 40 services to Azure.");
            var context = Context(
                Analysis(required: ["azure"]),
                DiagnosticScope.Resume([weak, strong]));

            var diagnostic = (await RunAsync(new BulletOrderingAnalyzer(), context)).Should().ContainSingle().Subject;

            diagnostic.Severity.Should().Be(DiagnosticSeverityDto.Suggestion);
            diagnostic.BulletIds.Should().Contain([strong.Id, weak.Id]);
        }

        [Fact]
        public async Task SaysNothingWhenTheStrongestBulletIsAlreadyFirst()
        {
            var context = Context(
                Analysis(required: ["azure"]),
                DiagnosticScope.Resume([
                    Bullet("Migrated 40 services to Azure."),
                    Bullet("Organised the team offsite.")
                ]));

            (await RunAsync(new BulletOrderingAnalyzer(), context)).Should().BeEmpty();
        }
    }

    public class DuplicateBullets
    {
        private static Mock<IBulletDuplicateDetector> Detector(
            DuplicateScanResult result,
            out List<DuplicateSubject> captured)
        {
            var subjects = new List<DuplicateSubject>();
            captured = subjects;

            var mock = new Mock<IBulletDuplicateDetector>();
            mock.Setup(x => x.DetectAsync(It.IsAny<IReadOnlyList<DuplicateSubject>>(), It.IsAny<CancellationToken>()))
                .Callback<IReadOnlyList<DuplicateSubject>, CancellationToken>((s, _) => subjects.AddRange(s))
                .ReturnsAsync(result);
            return mock;
        }

        [Fact]
        public async Task ReportsAPairOnceEvenThoughTheDetectorReportsItFromBothSides()
        {
            var left = Bullet("Cut deploy time by 80%.");
            var right = Bullet("Reduced deployment time 80%.");

            var scan = new DuplicateScanResult(
                new Dictionary<int, IReadOnlyList<DuplicateWarningDto>>
                {
                    [0] = [new DuplicateWarningDto(DuplicateWarningKindDto.ExistingBullet, right.Id, null, right.BulletText, 0.93)],
                    [1] = [new DuplicateWarningDto(DuplicateWarningKindDto.ExistingBullet, left.Id, null, left.BulletText, 0.93)]
                },
                DuplicateDetectionModeDto.Semantic,
                null);

            var analyzer = new DuplicateBulletAnalyzer(Detector(scan, out _).Object);
            var context = Context(Analysis(), DiagnosticScope.Library([left, right]));

            var diagnostics = await RunAsync(analyzer, context);

            diagnostics.Should().ContainSingle()
                .Which.BulletIds.Should().BeEquivalentTo([left.Id, right.Id]);
        }

        [Fact]
        public async Task SurfacesLexicalFallbackAsAnInfoDiagnostic()
        {
            var scan = new DuplicateScanResult(
                new Dictionary<int, IReadOnlyList<DuplicateWarningDto>>(),
                DuplicateDetectionModeDto.Lexical,
                "Semantic duplicate detection is unavailable, so duplicates were compared by text only.");

            var analyzer = new DuplicateBulletAnalyzer(Detector(scan, out _).Object);
            var context = Context(Analysis(), DiagnosticScope.Library([Bullet("One."), Bullet("Two.")]));

            var diagnostics = await RunAsync(analyzer, context);

            diagnostics.Should().ContainSingle()
                .Which.Severity.Should().Be(DiagnosticSeverityDto.Info,
                    "an empty duplicate list means less when only text was compared, and the report should say so");
        }

        [Fact]
        public async Task PassesRealBulletIdsSoTheDetectorCanSuppressSelfAndIgnoredPairs()
        {
            var bullets = new[] { Bullet("One."), Bullet("Two.") };
            var scan = new DuplicateScanResult(
                new Dictionary<int, IReadOnlyList<DuplicateWarningDto>>(),
                DuplicateDetectionModeDto.Semantic,
                null);

            var analyzer = new DuplicateBulletAnalyzer(Detector(scan, out var captured).Object);

            await RunAsync(analyzer, Context(Analysis(), DiagnosticScope.Library(bullets)));

            captured.Select(x => x.BulletId).Should().BeEquivalentTo(bullets.Select(x => (Guid?)x.Id));
        }

        [Fact]
        public async Task SaysNothingAboutASingleBullet()
        {
            var analyzer = new DuplicateBulletAnalyzer(Mock.Of<IBulletDuplicateDetector>());

            (await RunAsync(analyzer, Context(Analysis(), DiagnosticScope.Library([Bullet("Only one.")]))))
                .Should().BeEmpty();
        }
    }
}
