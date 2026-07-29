using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class BulletRankingServiceTests
{
    private static JobAnalysisDto Analysis(
        string[]? required = null,
        string[]? preferred = null,
        string[]? technologies = null,
        string[]? keywords = null)
        => new(required ?? [], preferred ?? [], technologies ?? [], keywords ?? [], [], null, null);

    private static Bullet CreateBullet(string text, string[]? skills = null, string[]? technologies = null, DateTime? modified = null)
        => new()
        {
            Id = Guid.NewGuid(),
            BulletText = text,
            Skills = (skills ?? []).ToList(),
            Technologies = (technologies ?? []).ToList(),
            ModifiedDate = modified ?? DateTime.UtcNow
        };

    [Fact]
    public void Rank_OrdersByScoreDescending()
    {
        var sut = new BulletRankingService();
        var strong = CreateBullet("Built C# microservices", skills: ["C#"], technologies: ["c#"]);
        var weak = CreateBullet("Organized team lunches");

        var result = sut.Rank([weak, strong], Analysis(required: ["c#"], technologies: ["c#"]), maxBullets: 10);

        result[0].Bullet.Should().BeSameAs(strong);
        result[0].Score.Should().BeGreaterThan(result[1].Score);
        result[1].Score.Should().Be(0);
    }

    [Fact]
    public void Rank_WeighsMetadataIntersectionsAboveTextMatches()
    {
        var sut = new BulletRankingService();
        // Same text hit for both; only one has the skill in its metadata.
        var tagged = CreateBullet("Used c# daily", skills: ["c#"]);
        var untagged = CreateBullet("Used c# daily");

        var result = sut.Rank([untagged, tagged], Analysis(required: ["c#"]), maxBullets: 10);

        result[0].Bullet.Should().BeSameAs(tagged, "a required-skill metadata match adds 12 on top of the text match's 8");
        (result[0].Score - result[1].Score).Should().Be(12);
    }

    [Fact]
    public void Rank_BreaksTiesByMostRecentlyModified()
    {
        var sut = new BulletRankingService();
        var older = CreateBullet("same text", modified: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = CreateBullet("same text", modified: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var result = sut.Rank([older, newer], Analysis(), maxBullets: 10);

        result[0].Bullet.Should().BeSameAs(newer);
    }

    [Fact]
    public void Rank_RespectsMaxBullets()
    {
        var sut = new BulletRankingService();
        var bullets = Enumerable.Range(0, 10).Select(i => CreateBullet($"bullet {i}")).ToArray();

        var result = sut.Rank(bullets, Analysis(), maxBullets: 3);

        result.Should().HaveCount(3);
    }

    [Fact]
    public void Rank_WithZeroMaxBullets_StillReturnsAtLeastOne()
    {
        var sut = new BulletRankingService();

        var result = sut.Rank([CreateBullet("only one")], Analysis(), maxBullets: 0);

        result.Should().ContainSingle();
    }

    [Fact]
    public void Rank_MatchesMetadataCaseInsensitively()
    {
        var sut = new BulletRankingService();
        var bullet = CreateBullet("no text match here", skills: ["Kubernetes"]);

        var result = sut.Rank([bullet], Analysis(required: ["KUBERNETES"]), maxBullets: 5);

        result[0].Score.Should().Be(12);
    }
}
