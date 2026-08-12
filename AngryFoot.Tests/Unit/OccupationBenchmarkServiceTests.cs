using AngryFoot.ApiService.Application.Benchmarks;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

public class OccupationBenchmarkServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private sealed record StubDataset(OccupationBenchmarkData Data) : IOccupationBenchmarkDataset;

    private static readonly BenchmarkOccupation SoftwareDevelopers = new(
        "15-1252.00",
        "Software Developers",
        ["Software Engineer"],
        [
            new BenchmarkItem("Programming", "Skill", 75, ["Programming", "develop", "cod"]),
            new BenchmarkItem("Troubleshooting", "Skill", 60, ["Troubleshooting", "root cause", "debug"]),
            new BenchmarkItem("Git", "Technology", 45, ["Git"]),
            new BenchmarkItem("Kubernetes", "Technology", 20, ["Kubernetes"])
        ]);

    private static OccupationBenchmarkData Dataset(params BenchmarkOccupation[] occupations)
        => new("O*NET 30.3 Database", "Attribution text.", occupations);

    private OccupationBenchmarkService CreateSut(OccupationBenchmarkData data)
        => new(_database.Context, new StubDataset(data), NullLogger<OccupationBenchmarkService>.Instance);

    private void SeedBullets(params Bullet[] bullets)
    {
        _database.Context.Bullets.AddRange(bullets);
        _database.Context.SaveChanges();
    }

    private static Bullet Bullet(string text, string[]? technologies = null)
        => new()
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            Technologies = (technologies ?? []).ToList()
        };

    private static JobAnalysisDto Analysis(string? inferredTitle = null)
        => new([], [], [], [], [], inferredTitle, null);

    [Fact]
    public async Task BuildAsync_WeightsCoverageByImportance()
    {
        // Programming (75) and Git (45) are evidenced; Troubleshooting (60) and Kubernetes (20) are not.
        SeedBullets(
            Bullet("Developed a payments service.", technologies: ["Git"]));
        var sut = CreateSut(Dataset(SoftwareDevelopers));

        var result = await sut.BuildAsync("Software Engineer", Analysis(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.CoverageScore.Should().Be(60, "120 of 200 importance points are covered");
        result.Covered.Select(i => i.Name).Should().BeEquivalentTo(["Programming", "Git"]);
        result.Missing.Select(i => i.Name).Should().BeEquivalentTo(["Troubleshooting", "Kubernetes"]);
        result.MatchConfidence.Should().Be("Exact");
        result.SocCode.Should().Be("15-1252.00");
        result.SourceAttribution.Should().Be("Attribution text.");
    }

    [Fact]
    public async Task BuildAsync_RanksListsByImportance()
    {
        SeedBullets(Bullet("Developed and debugged services.", technologies: ["Git", "Kubernetes"]));
        var sut = CreateSut(Dataset(SoftwareDevelopers));

        var result = await sut.BuildAsync("Software Engineer", Analysis(), CancellationToken.None);

        result!.CoverageScore.Should().Be(100);
        result.Covered.Select(i => i.Name).Should()
            .ContainInOrder("Programming", "Troubleshooting", "Git", "Kubernetes");
    }

    [Fact]
    public async Task BuildAsync_WithEmptyBulletLibrary_ReportsEverythingMissing()
    {
        var sut = CreateSut(Dataset(SoftwareDevelopers));

        var result = await sut.BuildAsync("Software Engineer", Analysis(), CancellationToken.None);

        result!.CoverageScore.Should().Be(0);
        result.Covered.Should().BeEmpty();
        result.Missing.Should().HaveCount(4);
        result.CoveredCount.Should().Be(0);
        result.RequirementCount.Should().Be(4);
    }

    [Fact]
    public async Task BuildAsync_ReportsTotalsSeparatelyFromTheTruncatedLists()
    {
        var manyItems = Enumerable.Range(0, 12)
            .Select(i => new BenchmarkItem($"Skill{i}", "Skill", 50, [$"skill{i}"]))
            .ToArray();
        var occupation = new BenchmarkOccupation("15-1252.00", "Software Developers", ["Software Engineer"], manyItems);
        SeedBullets(Bullet(string.Join(' ', manyItems.Select(i => i.Name))));
        var sut = CreateSut(Dataset(occupation));

        var result = await sut.BuildAsync("Software Engineer", Analysis(), CancellationToken.None);

        result!.Covered.Should().HaveCount(8, "the panel lists only the highest-importance items");
        result.CoveredCount.Should().Be(12, "the totals let the UI say how many were held back");
        result.RequirementCount.Should().Be(12);
    }

    [Fact]
    public async Task BuildAsync_MatchesBulletSkillsAndTechnologiesNotJustText()
    {
        SeedBullets(Bullet("Shipped a release.", technologies: ["Git"]));
        var sut = CreateSut(Dataset(SoftwareDevelopers));

        var result = await sut.BuildAsync("Software Engineer", Analysis(), CancellationToken.None);

        result!.Covered.Select(i => i.Name).Should().Contain("Git",
            "the benchmark reads the same bullet fields as the fit heuristic");
    }

    [Fact]
    public async Task BuildAsync_PrefersExplicitJobTitleOverInferredTitle()
    {
        var nurses = new BenchmarkOccupation("29-1141.00", "Registered Nurses", [],
            [new BenchmarkItem("Service Orientation", "Skill", 80, ["patient"])]);
        var sut = CreateSut(Dataset(SoftwareDevelopers, nurses));

        var result = await sut.BuildAsync("Software Engineer", Analysis(inferredTitle: "Registered Nurse"), CancellationToken.None);

        result!.SocCode.Should().Be("15-1252.00", "the title the user typed is more reliable than the one inferred from the posting");
    }

    [Fact]
    public async Task BuildAsync_WithoutJobTitle_FallsBackToInferredTitle()
    {
        var sut = CreateSut(Dataset(SoftwareDevelopers));

        var result = await sut.BuildAsync(null, Analysis(inferredTitle: "Software Engineer"), CancellationToken.None);

        result!.SocCode.Should().Be("15-1252.00");
    }

    [Fact]
    public async Task BuildAsync_WithUnmappableTitle_ReturnsNoneWithoutClaimingAComparison()
    {
        SeedBullets(Bullet("Developed a payments service."));
        var sut = CreateSut(Dataset(SoftwareDevelopers));

        var result = await sut.BuildAsync("Chief Vibes Officer", Analysis(), CancellationToken.None);

        result.Should().NotBeNull("the user is told why there is no benchmark rather than shown nothing");
        result!.MatchConfidence.Should().Be("None");
        result.SocCode.Should().BeNull();
        result.CoverageScore.Should().Be(0);
        result.Covered.Should().BeEmpty();
        result.Missing.Should().BeEmpty();
        result.Summary.Should().Contain("Chief Vibes Officer");
    }

    [Fact]
    public async Task BuildAsync_WithNoTitleAtAll_ExplainsThatATitleIsNeeded()
    {
        var sut = CreateSut(Dataset(SoftwareDevelopers));

        var result = await sut.BuildAsync(null, Analysis(), CancellationToken.None);

        result!.MatchConfidence.Should().Be("None");
        result.Summary.Should().Contain("job title");
    }

    [Fact]
    public async Task BuildAsync_WithNoDataset_ReturnsNull()
    {
        var sut = CreateSut(OccupationBenchmarkData.Empty);

        var result = await sut.BuildAsync("Software Engineer", Analysis(), CancellationToken.None);

        result.Should().BeNull("a missing dataset disables the panel rather than showing an empty one");
    }

    [Fact]
    public async Task BuildAsync_NeverFramesTheComparisonAsBeingAgainstPeople()
    {
        SeedBullets(Bullet("Developed a payments service."));
        var sut = CreateSut(Dataset(SoftwareDevelopers));

        var result = await sut.BuildAsync("Software Engineer", Analysis(), CancellationToken.None);

        // Issue #5: the benchmark is aggregate occupational data and the copy must never imply otherwise.
        result!.Summary.Should().NotContainAny("employees", "other candidates", "peers", "colleagues");
        result.Summary.Should().Contain("not a comparison");
    }
}
