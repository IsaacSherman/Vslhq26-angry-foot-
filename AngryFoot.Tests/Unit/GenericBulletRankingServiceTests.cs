using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class GenericBulletRankingServiceTests
{
    private static Bullet CreateBullet(
        string text,
        string[]? skills = null,
        string[]? technologies = null,
        string? employer = null,
        DateTime? modified = null)
        => new()
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            Skills = (skills ?? []).ToList(),
            Technologies = (technologies ?? []).ToList(),
            SourceEmployer = employer,
            EnrichmentState = EnrichmentState.Enriched,
            ModifiedDate = modified ?? DateTime.UtcNow
        };

    private static IReadOnlyList<string> TextsOf(IEnumerable<RankedBullet> ranked)
        => ranked.Select(x => x.Bullet.BulletText).ToArray();

    [Fact]
    public void Rank_PrefersTheBulletThatQuantifiesItsResult()
    {
        var sut = new GenericBulletRankingService();
        var measured = CreateBullet("Cut deployment time by 40% at Contoso.", technologies: ["azure"]);
        var unmeasured = CreateBullet("Improved the deployment process somewhat.", technologies: ["azure"]);

        var result = sut.Rank([unmeasured, measured], take: 2);

        result[0].Bullet.Should().BeSameAs(measured);
        result[0].Score.Should().BeGreaterThan(result[1].Score);
    }

    [Fact]
    public void Rank_DoesNotPlaceTwoNearDuplicatesSideBySide()
    {
        var sut = new GenericBulletRankingService();
        var original = CreateBullet("Migrated 40 payment services onto Kubernetes at Contoso.", technologies: ["kubernetes"]);
        var paraphrase = CreateBullet("Migrated 40 payment services to Kubernetes at Contoso.", technologies: ["kubernetes"]);
        var distinct = CreateBullet("Rebuilt the Postgres reporting pipeline, cutting query time 60%.", technologies: ["postgres"]);

        var result = sut.Rank([original, paraphrase, distinct], take: 3);

        TextsOf(result).Take(2).Should().Contain(distinct.BulletText,
            "a bullet that repeats one already selected should lose its slot to a distinct one");
        result[2].Bullet.BulletText.Should().BeOneOf(original.BulletText, paraphrase.BulletText);
    }

    [Fact]
    public void Rank_ReportsTheOverlapItPenalised()
    {
        var sut = new GenericBulletRankingService();
        var original = CreateBullet("Migrated 40 payment services onto Kubernetes at Contoso.");
        var paraphrase = CreateBullet("Migrated 40 payment services to Kubernetes at Contoso.");

        var result = sut.Rank([original, paraphrase], take: 2);

        result[1].Reasons.Should().Contain(x => x.Kind == RankingReasonKind.Overlap);
        result[0].Reasons.Should().NotContain(x => x.Kind == RankingReasonKind.Overlap,
            "the first bullet selected has nothing to repeat");
    }

    [Fact]
    public void Rank_BreaksIntoASecondTechnologyRatherThanStackingOneCluster()
    {
        var sut = new GenericBulletRankingService();
        // Four strong Kubernetes bullets against one equally strong Postgres bullet. Sorted by
        // quality alone the Postgres bullet cannot beat any of them; it has to win on breadth.
        var cluster = new[]
        {
            CreateBullet("Tuned Kubernetes autoscaling, halving cold starts at Contoso.", technologies: ["kubernetes"]),
            CreateBullet("Hardened Kubernetes ingress, dropping 5xx rates 30% at Contoso.", technologies: ["kubernetes"]),
            CreateBullet("Automated Kubernetes upgrades, saving 12 hours a month at Contoso.", technologies: ["kubernetes"]),
            CreateBullet("Sharded Kubernetes workloads across 3 regions at Contoso.", technologies: ["kubernetes"])
        };
        var outlier = CreateBullet("Rebuilt the Postgres reporting pipeline, cutting query time 60% at Contoso.", technologies: ["postgres"]);

        var result = sut.Rank([.. cluster, outlier], take: 3);

        TextsOf(result).Should().Contain(outlier.BulletText,
            "the only bullet covering a second technology should reach the top three");
    }

    [Fact]
    public void Rank_SpreadsAcrossEmployersWhenQualityIsComparable()
    {
        var sut = new GenericBulletRankingService();
        var contoso = Enumerable.Range(0, 5)
            .Select(i => CreateBullet($"Shipped release {i}, cutting defects 20%.", employer: "Contoso"))
            .ToArray();
        var fabrikam = CreateBullet("Launched the billing rewrite, cutting disputes 25%.", employer: "Fabrikam");

        var result = sut.Rank([.. contoso, fabrikam], take: 4);

        result.Select(x => x.Bullet.SourceEmployer).Should().Contain("Fabrikam");
    }

    [Fact]
    public void Rank_TreatsAnUnenrichedBulletAsNeitherNovelNorRepetitive()
    {
        var sut = new GenericBulletRankingService();
        var unenriched = new Bullet
        {
            Id = Guid.NewGuid(),
            BulletText = "Cut deployment time by 40% at Contoso.",
            EnrichmentState = EnrichmentState.Pending,
            ModifiedDate = DateTime.UtcNow
        };

        var result = sut.Rank([unenriched], take: 1);

        result.Should().ContainSingle();
        result[0].Reasons.Should().NotContain(x => x.Kind == RankingReasonKind.Breadth);
        result[0].Score.Should().BePositive("a bullet with no tags is still judged on how it is written");
    }

    [Fact]
    public void Rank_RespectsTakeAndNeverExceedsTheLibrary()
    {
        var sut = new GenericBulletRankingService();
        var bullets = Enumerable.Range(0, 10).Select(i => CreateBullet($"Delivered project {i}.")).ToArray();

        sut.Rank(bullets, take: 3).Should().HaveCount(3);
        sut.Rank(bullets, take: 50).Should().HaveCount(10);
        sut.Rank(bullets, take: 0).Should().ContainSingle("zero is treated the way the keyword ranker treats it");
    }

    [Fact]
    public void Rank_IsDeterministicAcrossRepeatedRuns()
    {
        var sut = new GenericBulletRankingService();
        var stamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var bullets = new[]
        {
            CreateBullet("Delivered the migration.", modified: stamp),
            CreateBullet("Delivered the migration.", modified: stamp),
            CreateBullet("Cut costs by 30%.", modified: stamp)
        };

        var first = sut.Rank(bullets, take: 3).Select(x => x.Bullet.Id);
        var second = sut.Rank(bullets, take: 3).Select(x => x.Bullet.Id);

        first.Should().Equal(second);
    }

    [Fact]
    public void Rank_WithNoBullets_ReturnsNothing()
    {
        var sut = new GenericBulletRankingService();

        sut.Rank([], take: 5).Should().BeEmpty();
    }

    [Fact]
    public void Rank_NamesTheGroundABulletBrokeIntoFirst()
    {
        var sut = new GenericBulletRankingService();
        var first = CreateBullet("Cut Azure spend by 30% at Contoso.", technologies: ["azure"]);
        var second = CreateBullet("Rebuilt the Postgres pipeline, cutting query time 60%.", technologies: ["postgres"]);

        var result = sut.Rank([first, second], take: 2);

        // Which of the two leads is a quality question and not the point here; what matters is that
        // the second one is credited with the technology it was the first to bring.
        var runnerUp = result[1];
        var technology = runnerUp.Bullet.Technologies.Single();
        runnerUp.Reasons.Should().Contain(x =>
            x.Kind == RankingReasonKind.Breadth && x.Text.Contains(technology, StringComparison.OrdinalIgnoreCase));
        result[0].Reasons.Should().NotContain(x => x.Kind == RankingReasonKind.Breadth,
            "everything is new ground for the first bullet, which is not worth saying");
    }
}
