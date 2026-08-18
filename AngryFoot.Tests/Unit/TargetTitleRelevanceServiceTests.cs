using AngryFoot.ApiService.Application.Benchmarks;
using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.ApiService.Domain;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// Runs against the real bundled O*NET dataset rather than a fake one: the value of this feature is
/// entirely in whether the shipped data actually maps titles to bullets, and a stubbed dataset
/// would prove only that the arithmetic works.
/// </summary>
public class TargetTitleRelevanceServiceTests
{
    private readonly FakeBulletVectorStore _vectorStore = new() { IsAvailable = false };

    private static Bullet CreateBullet(string text, string[]? technologies = null, string[]? skills = null)
        => new()
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            Technologies = (technologies ?? []).ToList(),
            Skills = (skills ?? []).ToList(),
            ModifiedDate = DateTime.UtcNow
        };

    /// <summary>
    /// The dataset as it ships, loaded from the output directory the ApiService project copies it
    /// to - the same route <see cref="OccupationBenchmarkDatasetTests"/> takes.
    /// </summary>
    private static readonly OccupationBenchmarkData ShippedData = OccupationBenchmarkDataset.Load(
        Path.Combine(AppContext.BaseDirectory, "Application", "Benchmarks", "Data", OccupationBenchmarkDataset.DataFileName),
        NullLogger.Instance);

    private sealed class ShippedDataset : IOccupationBenchmarkDataset
    {
        public OccupationBenchmarkData Data => ShippedData;
    }

    private TargetTitleRelevanceService CreateSut()
        => new(new ShippedDataset(), _vectorStore, NullLogger<TargetTitleRelevanceService>.Instance);

    [Fact]
    public async Task BuildAsync_WithNoTitle_ScoresNothing()
    {
        var sut = CreateSut();

        var relevance = await sut.BuildAsync(
            null, [CreateBullet("Cut Azure spend 30%.")], TestContext.Current.CancellationToken);

        relevance.IsActive.Should().BeFalse();
        relevance.Summary.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_WithATitleTheDatasetDoesNotKnow_SaysItSteeredNothing()
    {
        var sut = CreateSut();

        var relevance = await sut.BuildAsync(
            "Assistant Regional Manager of Beets",
            [CreateBullet("Cut Azure spend 30%.")],
            TestContext.Current.CancellationToken);

        relevance.IsActive.Should().BeFalse();
        relevance.Summary.Should().Contain("did not steer this selection",
            "a title that changed nothing has to say so, or the user cannot tell it was ignored");
    }

    /// <summary>
    /// The behaviour the feature was asked for, and the case the occupational dataset cannot serve
    /// on its own: it carries no machine-learning occupation at all, so this is the title's own
    /// words doing the work.
    /// </summary>
    [Fact]
    public async Task BuildAsync_RanksDifferentBulletsForDifferentTitles()
    {
        var sut = CreateSut();
        var machineLearning = CreateBullet(
            "Trained and deployed machine learning models over 2M rows of customer data.",
            technologies: ["Python"],
            skills: ["machine learning"]);
        var backend = CreateBullet(
            "Built and maintained C# web services and their SQL database schemas.",
            technologies: ["C#", "SQL"],
            skills: ["software development"]);
        var bullets = new[] { machineLearning, backend };

        var forMachineLearning = await sut.BuildAsync(
            "Machine Learning Specialist", bullets, TestContext.Current.CancellationToken);
        var forDeveloper = await sut.BuildAsync(
            "Software Engineer", bullets, TestContext.Current.CancellationToken);

        forMachineLearning.For(machineLearning.Id).Should().BeGreaterThan(forMachineLearning.For(backend.Id));
        forDeveloper.For(backend.Id).Should().BeGreaterThan(forDeveloper.For(machineLearning.Id));
    }

    [Fact]
    public async Task BuildAsync_ExplainsAWordMatchByNamingTheSubjectItFound()
    {
        var sut = CreateSut();
        var bullet = CreateBullet(
            "Trained machine learning models over 2M rows.", skills: ["machine learning"]);

        var relevance = await sut.BuildAsync(
            "Senior Machine Learning Engineer", [bullet], TestContext.Current.CancellationToken);

        relevance.Describe(bullet.Id).Should().Contain("machine learning");
        relevance.Summary.Should().Contain("Selected for \"Senior Machine Learning Engineer\"");
        relevance.Summary.Should().Contain("machine learning", "seniority is stripped, the subject is not");
    }

    /// <summary>
    /// A title made only of ladder words has no subject to search for, so the word signal must
    /// contribute nothing rather than matching every bullet that happens to use the word.
    /// </summary>
    /// <remarks>
    /// "Coordinator" rather than "Engineer" deliberately: O*NET lists "Engineer" as an alternate
    /// title of Computer Hardware Engineers, so that title does map to an occupation and would score
    /// through the occupational signal - correctly, but through the path this test is not about.
    /// </remarks>
    [Fact]
    public async Task BuildAsync_WithATitleThatIsOnlyARoleNoun_DoesNotMatchOnTheRoleNoun()
    {
        var sut = CreateSut();
        var bullet = CreateBullet("Worked as a coordinator on the platform team.");

        var relevance = await sut.BuildAsync("Senior Coordinator", [bullet], TestContext.Current.CancellationToken);

        relevance.For(bullet.Id).Should().Be(0);
        relevance.Summary.Should().Contain("did not steer this selection");
    }

    [Fact]
    public async Task BuildAsync_ScoresAWholePhraseAboveASingleWordFromIt()
    {
        var sut = CreateSut();
        var whole = CreateBullet("Trained machine learning models over 2M rows.");
        var partial = CreateBullet("Repaired the machine shop's CNC controller.");

        var relevance = await sut.BuildAsync(
            "Machine Learning Specialist", [whole, partial], TestContext.Current.CancellationToken);

        relevance.For(whole.Id).Should().BeGreaterThan(relevance.For(partial.Id));
    }

    /// <summary>
    /// When every bullet scores the same there is no spread to read, so they are all equally
    /// relevant - and the summary has to report that as the null result it is rather than as a
    /// library full of matches.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenEveryBulletScoresTheSame_SaysTheTitleNarrowedNothing()
    {
        var sut = CreateSut();
        var bullets = new[]
        {
            CreateBullet("Organised the annual team offsite."),
            CreateBullet("Chaired the weekly planning meeting."),
            CreateBullet("Maintained the shared calendar.")
        };

        _vectorStore.IsAvailable = true;
        _vectorStore.SearchResults = bullets.Select(x => new BulletSimilarityMatch(x.Id, 0.62f)).ToArray();

        var relevance = await sut.BuildAsync(
            "Assistant Regional Manager of Beets", bullets, TestContext.Current.CancellationToken);

        relevance.Summary.Should().Contain("narrowed the field very little");
    }

    /// <summary>
    /// The defect this normalisation exists to prevent: a three-word title is close to most of one
    /// person's technical writing, so scaling against the maximum alone left every bullet between
    /// 0.76 and 1.0 and the relevance term stopped separating anything.
    /// </summary>
    [Fact]
    public async Task BuildAsync_SpreadsCloselyBunchedSemanticScoresAcrossTheWholeRange()
    {
        var sut = CreateSut();
        var best = CreateBullet("Trained a churn model.");
        var middle = CreateBullet("Built the reporting service.");
        var worst = CreateBullet("Organised the team offsite.");

        _vectorStore.IsAvailable = true;
        _vectorStore.SearchResults =
        [
            new BulletSimilarityMatch(best.Id, 0.72f),
            new BulletSimilarityMatch(middle.Id, 0.63f),
            new BulletSimilarityMatch(worst.Id, 0.55f)
        ];

        var relevance = await sut.BuildAsync(
            "Assistant Regional Manager of Beets", [best, middle, worst], TestContext.Current.CancellationToken);

        relevance.For(best.Id).Should().BeGreaterThan(0.9);
        relevance.For(worst.Id).Should().Be(0);
        (relevance.For(best.Id) - relevance.For(middle.Id)).Should().BeGreaterThan(0.3,
            "raw scores nine hundredths apart have to reach the ranker as a difference it can act on");
    }

    /// <summary>
    /// And the guard on that spreading: a library whose best match is barely above the floor should
    /// not have its least-far-away bullet promoted to full relevance.
    /// </summary>
    [Fact]
    public async Task BuildAsync_DampsTheWholeSemanticSignalWhenNothingIsActuallyClose()
    {
        var sut = CreateSut();
        var best = CreateBullet("Organised the annual team offsite.");
        var worst = CreateBullet("Maintained the shared calendar.");

        _vectorStore.IsAvailable = true;
        _vectorStore.SearchResults =
        [
            new BulletSimilarityMatch(best.Id, 0.38f),
            new BulletSimilarityMatch(worst.Id, 0.36f)
        ];

        var relevance = await sut.BuildAsync(
            "Assistant Regional Manager of Beets", [best, worst], TestContext.Current.CancellationToken);

        relevance.For(best.Id).Should().BeLessThan(0.7,
            "nothing here is close to the title, so the top of a weak field is not fully relevant");
        relevance.For(best.Id).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task BuildAsync_ExplainsAnOccupationalMatchByNamingWhatTheOccupationAsksFor()
    {
        var sut = CreateSut();
        // No subject words to match on ("engineer" is stripped), so this can only score through the
        // occupation the title maps to.
        var bullet = CreateBullet(
            "Debugged and refactored the deployment pipeline, cutting release time 40%.",
            skills: ["programming"]);

        var relevance = await sut.BuildAsync("Software Engineer", [bullet], TestContext.Current.CancellationToken);

        Assert.SkipUnless(relevance.IsActive, "The bundled O*NET dataset does not cover this occupation.");
        relevance.Describe(bullet.Id).Should().Contain("typically asked for");
        relevance.Summary.Should().Contain("Software Developers");
    }

    [Fact]
    public async Task BuildAsync_WhenTheIndexIsAvailable_UsesItForBulletsTheDatasetMisses()
    {
        var sut = CreateSut();
        var unmatched = CreateBullet("Organised the annual team offsite.");

        _vectorStore.IsAvailable = true;
        _vectorStore.SearchResults = [new BulletSimilarityMatch(unmatched.Id, 0.81f)];

        var relevance = await sut.BuildAsync(
            "Assistant Regional Manager of Beets", [unmatched], TestContext.Current.CancellationToken);

        relevance.For(unmatched.Id).Should().BeGreaterThan(0,
            "the index can place a bullet the occupational dataset has no entry for");
        relevance.Describe(unmatched.Id).Should().Contain("Reads as close to");
    }

    [Fact]
    public async Task BuildAsync_IgnoresWeakSemanticMatches()
    {
        var sut = CreateSut();
        var unrelated = CreateBullet("Organised the annual team offsite.");

        _vectorStore.IsAvailable = true;
        _vectorStore.SearchResults = [new BulletSimilarityMatch(unrelated.Id, 0.11f)];

        var relevance = await sut.BuildAsync(
            "Assistant Regional Manager of Beets", [unrelated], TestContext.Current.CancellationToken);

        relevance.IsActive.Should().BeFalse("a weak cosine score is noise, not relevance");
    }

    [Fact]
    public async Task BuildAsync_WhenTheIndexThrows_StillReturnsOccupationalScores()
    {
        var sut = CreateSut();
        var bullet = CreateBullet(
            "Built and maintained C# web services and their SQL database schemas.",
            technologies: ["C#", "SQL"],
            skills: ["software development", "programming"]);

        _vectorStore.IsAvailable = true;
        _vectorStore.SearchException = new HttpRequestException("index down");

        var relevance = await sut.BuildAsync("Software Developer", [bullet], TestContext.Current.CancellationToken);

        Assert.SkipUnless(relevance.IsActive, "The bundled O*NET dataset does not cover this occupation.");
        relevance.For(bullet.Id).Should().BeGreaterThan(0, "selection must survive the index being down");
    }
}
