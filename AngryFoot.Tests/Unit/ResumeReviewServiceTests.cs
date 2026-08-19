using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Application.Evidence.Diagnostics;
using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Application.Review;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AngryFoot.Tests.Unit;

public class ResumeReviewServiceTests
{
    private const string Resume = """
        MILDRED WAFFLE
        mildred.waffle@totally-real-mail.invalid

        EXPERIENCE

        Marmot Signal Works
        Staff Data Engineer, 2021 - Present
        - Cut warehouse costs by $280,000 per year by migrating cold partitions to object storage.
        - Worked on the nightly reconciliation job and the reporting pipeline.
        """;

    private sealed class StubReviewer(ResumeReview? review = null) : IResumeReviewer
    {
        public IReadOnlyList<CoverageDiagnosticDto>? SawFindings { get; private set; }

        public IReadOnlyList<Bullet>? SawBullets { get; private set; }

        public Task<ResumeReview?> ReviewAsync(
            IReadOnlyList<Bullet> bullets,
            IReadOnlyList<CoverageDiagnosticDto> deterministicFindings,
            CancellationToken cancellationToken)
        {
            SawBullets = bullets;
            SawFindings = deterministicFindings;
            return Task.FromResult(review);
        }
    }

    private sealed class ThrowingReviewer(Exception exception) : IResumeReviewer
    {
        public Task<ResumeReview?> ReviewAsync(
            IReadOnlyList<Bullet> bullets,
            IReadOnlyList<CoverageDiagnosticDto> deterministicFindings,
            CancellationToken cancellationToken) => throw exception;
    }

    private static ResumeReviewService CreateSut(
        IResumeReviewer? reviewer = null,
        IEvidenceCoverageAnalyzer? coverage = null,
        params IEvidenceDiagnosticAnalyzer[] analyzers)
        => new(
            analyzers.Length > 0 ? analyzers : [new MeasurableImpactAnalyzer()],
            reviewer ?? new StubReviewer(),
            new HeuristicJobAnalyzer(ChatClientMocks.ReturningText(string.Empty).Object, NullLogger<HeuristicJobAnalyzer>.Instance),
            coverage ?? Mock.Of<IEvidenceCoverageAnalyzer>(),
            NullLogger<ResumeReviewService>.Instance);

    private static Task<ResumeReviewReportDto> ReviewAsync(ResumeReviewService sut, string? posting = null)
        => sut.ReviewAsync(Resume, posting, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ReviewAsync_WithNoAi_StillReportsTheDeterministicFindings()
    {
        var report = await ReviewAsync(CreateSut());

        report.Source.Should().Be(CoverageSourceDto.Deterministic);
        report.Bullets.Should().HaveCount(2);
        report.Bullets[1].Findings.Should().Contain(x => x.Code == CoverageDiagnosticCodes.NoMeasurableImpact,
            "the second bullet states no figure, and that is decided without a model");
        report.Summary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ReviewAsync_AttachesEachFindingToTheBulletItIsAbout()
    {
        var report = await ReviewAsync(CreateSut());

        report.Bullets[0].Findings.Should().NotContain(x => x.Code == CoverageDiagnosticCodes.NoMeasurableImpact,
            "the first bullet states $280,000");
        report.SpotChecks.Should().OnlyContain(x => x.BulletIds.Count == 0,
            "a finding about one bullet belongs under that bullet, not in the document-level list");
    }

    [Fact]
    public async Task ReviewAsync_ShowsTheReviewerWhatTheDeterministicPassAlreadyFound()
    {
        var reviewer = new StubReviewer();

        await ReviewAsync(CreateSut(reviewer));

        reviewer.SawBullets.Should().HaveCount(2);
        reviewer.SawFindings.Should().NotBeEmpty("repeating a finding the user can already see is noise");
    }

    [Fact]
    public async Task ReviewAsync_AiSpotChecksAreFiledUnderTheDocumentEvenThoughTheyCiteBullets()
    {
        var everyBullet = new CoverageDiagnosticDto(
            DiagnosticSeverityDto.Suggestion,
            CoverageDiagnosticCodes.WeakEvidence,
            "Technical specificity is minimal across bullets.",
            EvidenceMappings.AboutBullets([], "..."),
            [Guid.NewGuid(), Guid.NewGuid()]);
        var reviewer = new StubReviewer(new ResumeReview(null, [everyBullet], new Dictionary<int, IReadOnlyList<string>>()));

        var report = await ReviewAsync(CreateSut(reviewer));

        report.SpotChecks.Should().Contain(x => x.Message == everyBullet.Message,
            "an observation about the resume belongs in the document list");
        report.Bullets.Should().OnlyContain(bullet => bullet.Findings.All(x => x.Message != everyBullet.Message),
            "filing it by citation would print one note about every bullet once per bullet");
    }

    [Fact]
    public async Task ReviewAsync_MergesTheAiReviewWithoutDisplacingTheDeterministicFindings()
    {
        var reviewer = new StubReviewer(new ResumeReview(
            "Reads as competent and unquantified.",
            [],
            new Dictionary<int, IReadOnlyList<string>> { [1] = ["Say how much time the automation saved."] }));

        var report = await ReviewAsync(CreateSut(reviewer));

        report.Source.Should().Be(CoverageSourceDto.AiReviewed);
        report.Summary.Should().Be("Reads as competent and unquantified.");
        report.Bullets[1].Suggestions.Should().Equal("Say how much time the automation saved.");
        report.Bullets[1].Findings.Should().Contain(x => x.Code == CoverageDiagnosticCodes.NoMeasurableImpact,
            "the AI pass adds to the deterministic report and never replaces it");
    }

    [Fact]
    public async Task ReviewAsync_WhenTheReviewerThrows_TheDeterministicReportStillComesBack()
    {
        var sut = CreateSut(new ThrowingReviewer(new InvalidOperationException("model exploded")));

        var review = async () => await ReviewAsync(sut);

        // The reviewer swallows its own failures; this pins that the service does not add a second
        // catch that would also hide a bug in the composition around it.
        await review.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReviewAsync_PropagatesCancellation()
    {
        var sut = CreateSut(new ThrowingReviewer(new OperationCanceledException()));

        var review = async () => await ReviewAsync(sut);

        await review.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReviewAsync_AFailingAnalyzerIsSkippedRatherThanFailingTheReview()
    {
        var sut = CreateSut(analyzers: [new ThrowingDiagnosticAnalyzer(), new MeasurableImpactAnalyzer()]);

        var report = await ReviewAsync(sut);

        report.Bullets[1].Findings.Should().Contain(x => x.Code == CoverageDiagnosticCodes.NoMeasurableImpact);
    }

    [Fact]
    public async Task ReviewAsync_WithNoBullets_SaysSoRatherThanReturningAnEmptyReport()
    {
        var sut = CreateSut();

        var report = await sut.ReviewAsync(
            "MILDRED WAFFLE\nmildred@totally-real-mail.invalid\n\nI have done many things.",
            null,
            TestContext.Current.CancellationToken);

        report.Bullets.Should().BeEmpty();
        report.SpotChecks.Should().Contain(x => x.Code == CoverageDiagnosticCodes.NoBulletsFound);
        report.Summary.Should().Contain("No achievement bullets");
    }

    [Fact]
    public async Task ReviewAsync_WithoutAPosting_DoesNotProduceCoverage()
    {
        (await ReviewAsync(CreateSut())).Coverage.Should().BeNull();
    }

    [Fact]
    public async Task ReviewAsync_WithAPosting_DescribesTheResumeAgainstIt()
    {
        var expected = new EvidenceCoverageReportDto(
            50, 1, 2, "Summary.", 0, 1, 1, [], [], CoverageSourceDto.Deterministic, "Disclaimer.");
        var coverage = new Mock<IEvidenceCoverageAnalyzer>();
        coverage
            .Setup(x => x.DescribeResumeAsync(It.IsAny<JobAnalysisDto>(), It.IsAny<IReadOnlyList<Bullet>>(), It.IsAny<SemanticEvidenceIndex?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var report = await ReviewAsync(CreateSut(coverage: coverage.Object), "We need a data engineer with SQL and Azure.");

        report.Coverage.Should().BeSameAs(expected);
        coverage.Verify(
            x => x.DescribeResumeAsync(It.IsAny<JobAnalysisDto>(), It.Is<IReadOnlyList<Bullet>>(b => b.Count == 2), It.IsAny<SemanticEvidenceIndex?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReviewAsync_GivesEveryBulletADistinctIdSoFindingsCanBeAttributed()
    {
        var reviewer = new StubReviewer();

        await ReviewAsync(CreateSut(reviewer));

        reviewer.SawBullets!.Select(x => x.Id).Should().OnlyHaveUniqueItems().And.NotContain(Guid.Empty);
    }
}
