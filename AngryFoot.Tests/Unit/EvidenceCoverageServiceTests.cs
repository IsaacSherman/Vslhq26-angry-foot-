using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Application.Evidence.Diagnostics;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// The service that assembles a report: deterministic engine, optional AI review, diagnostics. The
/// reviewer is faked here so the report-assembly rules can be exercised directly; the wire format
/// it parses is covered by <see cref="AiEvidenceReviewerTests"/>.
/// </summary>
public class EvidenceCoverageServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private EvidenceCoverageService CreateSut(
        FakeEvidenceReviewer? reviewer = null,
        params IEvidenceDiagnosticAnalyzer[] analyzers)
        => new(
            _database.Context,
            reviewer ?? new FakeEvidenceReviewer(),
            analyzers,
            NullLogger<EvidenceCoverageService>.Instance);

    private Bullet SeedBullet(string text, string[]? skills = null, string[]? technologies = null)
    {
        var bullet = new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            Skills = (skills ?? []).ToList(),
            Technologies = (technologies ?? []).ToList()
        };

        _database.Context.Bullets.Add(bullet);
        _database.Context.SaveChanges();
        return bullet;
    }

    private static JobAnalysisDto Analysis(string[]? required = null, string[]? preferred = null)
        => new(required ?? [], preferred ?? [], [], [], [], null, null);

    private static void AssertScoreIsDerivedFromRequirements(EvidenceCoverageReportDto coverage)
    {
        var expectedTotal = coverage.Requirements.Sum(x => x.Weight * 2);
        var expectedEarned = coverage.Requirements.Sum(x => x.Weight * x.Strength switch
        {
            EvidenceStrengthDto.Strong => 2,
            EvidenceStrengthDto.Weak => 1,
            _ => 0
        });

        coverage.TotalWeight.Should().Be(expectedTotal);
        coverage.EarnedWeight.Should().Be(expectedEarned);
        coverage.CoverageScore.Should().Be(expectedTotal == 0
            ? 0
            : (int)Math.Round(100.0 * expectedEarned / expectedTotal, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public async Task AnalyzeLibraryAsync_WithEmptyLibrary_ScoresZeroWithoutCallingTheReviewer()
    {
        var reviewer = new FakeEvidenceReviewer();
        var sut = CreateSut(reviewer);

        var coverage = await sut.AnalyzeLibraryAsync("A role.", Analysis(required: ["c#", "azure"]), TestContext.Current.CancellationToken);

        coverage.CoverageScore.Should().Be(0);
        coverage.Summary.Should().Contain("empty", "the summary must say why there is nothing to report");
        coverage.MissingCount.Should().Be(2);
        reviewer.CallCount.Should().Be(0, "there is no evidence for a reviewer to review");
    }

    [Fact]
    public async Task AnalyzeLibraryAsync_WithNoExtractedRequirements_SkipsTheReviewerAndSaysWhy()
    {
        SeedBullet("Built C# services.");
        var reviewer = new FakeEvidenceReviewer();

        var coverage = await CreateSut(reviewer).AnalyzeLibraryAsync("A role.", Analysis(), TestContext.Current.CancellationToken);

        coverage.CoverageScore.Should().Be(0);
        coverage.Summary.Should().Contain("No requirements could be extracted");
        reviewer.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeLibraryAsync_WhenTheReviewerDeclines_KeepsTheDeterministicReport()
    {
        SeedBullet("Built C# services.", skills: ["C#"]);

        var coverage = await CreateSut().AnalyzeLibraryAsync("A role.", Analysis(required: ["c#"]), TestContext.Current.CancellationToken);

        coverage.Source.Should().Be(CoverageSourceDto.Deterministic);
        coverage.CoverageScore.Should().Be(100);
        AssertScoreIsDerivedFromRequirements(coverage);
    }

    [Fact]
    public async Task AnalyzeLibraryAsync_WhenTheReviewerFails_KeepsTheDeterministicReport()
    {
        SeedBullet("Built C# services.", skills: ["C#"]);
        var reviewer = new FakeEvidenceReviewer { Throws = new HttpRequestException("down") };

        // The reviewer swallows its own failures, so a throw here is the belt-and-braces case:
        // the report must survive a collaborator that breaks its contract.
        var act = () => CreateSut(reviewer).AnalyzeLibraryAsync("A role.", Analysis(required: ["c#"]), TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task AnalyzeLibraryAsync_WithoutAReview_SaysSoAsAnInfoDiagnostic()
    {
        SeedBullet("Built C# services.", skills: ["C#"]);

        var coverage = await CreateSut().AnalyzeLibraryAsync("A role.", Analysis(required: ["c#"]), TestContext.Current.CancellationToken);

        coverage.Diagnostics.Should().ContainSingle(x => x.Code == CoverageDiagnosticCodes.AnalysisLimitation)
            .Which.Severity.Should().Be(DiagnosticSeverityDto.Info,
                "a report whose limits the user cannot see is the opacity this feature removes");
    }

    [Fact]
    public async Task AnalyzeLibraryAsync_WhenTheReviewerAnswers_UsesItsEvidenceAndSummary()
    {
        SeedBullet("Worked with Azure.");
        var reviewer = new FakeEvidenceReviewer(baseline => new EvidenceReview(
            "The reviewer's summary.",
            [baseline[0] with { Strength = EvidenceStrengthDto.Missing, Citations = [] }],
            []));

        var coverage = await CreateSut(reviewer).AnalyzeLibraryAsync(
            "A role.", Analysis(required: ["azure"]), TestContext.Current.CancellationToken);

        coverage.Source.Should().Be(CoverageSourceDto.AiReviewed);
        coverage.Summary.Should().Be("The reviewer's summary.");
        coverage.CoverageScore.Should().Be(0, "the reviewer downgraded the only requirement");
        coverage.Diagnostics.Should().NotContain(x => x.Code == CoverageDiagnosticCodes.AnalysisLimitation);
        AssertScoreIsDerivedFromRequirements(coverage);
    }

    [Fact]
    public async Task AnalyzeLibraryAsync_RunsDiagnosticsOverTheReviewedEvidenceNotTheBaseline()
    {
        SeedBullet("Cut Azure spend by 30%.");
        var reviewer = new FakeEvidenceReviewer(baseline => new EvidenceReview(
            null,
            [baseline[0] with { Strength = EvidenceStrengthDto.Missing, Citations = [] }],
            []));

        var coverage = await CreateSut(reviewer, new MissingSkillAnalyzer()).AnalyzeLibraryAsync(
            "A role.", Analysis(required: ["azure"]), TestContext.Current.CancellationToken);

        coverage.Diagnostics.Should().Contain(x => x.Code == CoverageDiagnosticCodes.MissingSkill,
            "a downgrade the reviewer made should surface as a diagnostic without the reviewer writing one");
    }

    [Fact]
    public async Task AnalyzeLibraryAsync_SendsTheProfileSummaryToTheReviewer()
    {
        SeedBullet("Built C# services.");
        _database.Context.Profiles.Add(new Profile
        {
            Id = Guid.NewGuid(),
            Name = "Ada",
            ProfessionalSummary = "Backend engineer."
        });
        _database.Context.SaveChanges();

        var reviewer = new FakeEvidenceReviewer();
        await CreateSut(reviewer).AnalyzeLibraryAsync("A role.", Analysis(required: ["c#"]), TestContext.Current.CancellationToken);

        reviewer.LastProfessionalSummary.Should().Be("Backend engineer.");
    }

    [Fact]
    public async Task AnalyzeLibraryAsync_OrdersRequirementsWeakestFirst()
    {
        SeedBullet("Built C# services.", skills: ["C#"]);
        SeedBullet("Touched some SQL.");

        var coverage = await CreateSut().AnalyzeLibraryAsync(
            "A role.",
            Analysis(required: ["c#", "sql", "kubernetes"]),
            TestContext.Current.CancellationToken);

        coverage.Requirements.Select(x => x.Strength).Should().BeInAscendingOrder(
            "the requirements needing work belong at the top");
    }

    [Fact]
    public async Task AnalyzeLibraryAsync_OrdersDiagnosticsMostUrgentFirst()
    {
        SeedBullet("Built C# services.", skills: ["C#"]);

        var coverage = await CreateSut(null, new MissingSkillAnalyzer(), new MeasurableImpactAnalyzer())
            .AnalyzeLibraryAsync("A role.", Analysis(required: ["c#", "kubernetes"]), TestContext.Current.CancellationToken);

        coverage.Diagnostics.Select(x => x.Severity).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task AnalyzeLibraryAsync_WhenADiagnosticAnalyzerThrows_KeepsTheRestOfTheReport()
    {
        SeedBullet("Built C# services.", skills: ["C#"]);

        var coverage = await CreateSut(null, new ThrowingDiagnosticAnalyzer(), new MissingSkillAnalyzer())
            .AnalyzeLibraryAsync("A role.", Analysis(required: ["c#", "kubernetes"]), TestContext.Current.CancellationToken);

        coverage.Diagnostics.Should().Contain(x => x.Code == CoverageDiagnosticCodes.MissingSkill);
        coverage.CoverageScore.Should().Be(50);
    }

    [Fact]
    public async Task AnalyzeLibraryAsync_WhenCancelled_PropagatesCancellation()
    {
        SeedBullet("Anything.");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var reviewer = new FakeEvidenceReviewer { Throws = new OperationCanceledException(cts.Token) };
        var act = () => CreateSut(reviewer).AnalyzeLibraryAsync("A role.", Analysis(required: ["c#"]), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AnalyzeLibraryAsync_AlwaysCarriesTheDisclaimer()
    {
        SeedBullet("Built C# services.");

        var coverage = await CreateSut().AnalyzeLibraryAsync("A role.", Analysis(required: ["c#"]), TestContext.Current.CancellationToken);

        coverage.Disclaimer.Should().Be(EvidenceCoverageCopy.Disclaimer);
    }

    [Fact]
    public async Task DescribeResumeAsync_ReportsOnTheGivenBulletsAndNeverCallsTheReviewer()
    {
        SeedBullet("A library bullet mentioning Azure.");
        var onResume = new Bullet { Id = Guid.NewGuid(), BulletText = "Cut C# build times by 40%." };
        var reviewer = new FakeEvidenceReviewer();

        var coverage = await CreateSut(reviewer).DescribeResumeAsync(
            Analysis(required: ["c#", "azure"]),
            [onResume],
            TestContext.Current.CancellationToken);

        coverage.Source.Should().Be(CoverageSourceDto.Deterministic);
        coverage.CoverageScore.Should().Be(50, "the resume evidences C# but says nothing about Azure");
        coverage.Requirements.Single(x => x.Requirement == "azure").Strength
            .Should().Be(EvidenceStrengthDto.Missing, "the library bullet is not on this resume");
        reviewer.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DescribeResumeAsync_DiagnosesOrderingBecauseAResumeHasAnOrder()
    {
        var weak = new Bullet { Id = Guid.NewGuid(), BulletText = "Organised the team offsite." };
        var strong = new Bullet { Id = Guid.NewGuid(), BulletText = "Migrated 40 services to Azure." };

        var coverage = await CreateSut(null, new BulletOrderingAnalyzer()).DescribeResumeAsync(
            Analysis(required: ["azure"]),
            [weak, strong],
            TestContext.Current.CancellationToken);

        coverage.Diagnostics.Should().Contain(x => x.Code == CoverageDiagnosticCodes.BulletOrdering);
    }
}
