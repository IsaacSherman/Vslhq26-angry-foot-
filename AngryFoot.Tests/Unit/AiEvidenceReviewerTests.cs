using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AwesomeAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// The reviewer is the only place an AI can move the coverage number, and it may only do so through
/// the per-requirement strengths. These tests are mostly about what it is <em>not</em> allowed to
/// do: cite bullets it was never shown, invent requirements, or talk a missing requirement up to
/// evidenced in one step.
/// </summary>
public class AiEvidenceReviewerTests
{
    private static readonly Bullet AzureBullet = new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        BulletText = "Worked with Azure across several services.",
        Skills = [],
        Technologies = []
    };

    private static readonly Bullet UnrelatedBullet = new()
    {
        Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        BulletText = "Ran the weekly planning meeting.",
        Skills = [],
        Technologies = []
    };

    private static JobAnalysisDto Analysis(params string[] required) => new(required, [], [], [], [], null, null);

    private static AiEvidenceReviewer CreateSut(string response)
        => new(ChatClientMocks.ReturningText(response).Object, NullLogger<AiEvidenceReviewer>.Instance);

    private static AiEvidenceReviewer CreateThrowingSut(Exception exception)
        => new(ChatClientMocks.Throwing(exception).Object, NullLogger<AiEvidenceReviewer>.Instance);

    private static IReadOnlyList<RequirementEvidence> Baseline(JobAnalysisDto analysis, params Bullet[] bullets)
        => EvidenceCoverageEngine.Evaluate(RequirementSet.From(analysis), bullets);

    private static Task<EvidenceReview?> ReviewAsync(
        AiEvidenceReviewer sut,
        JobAnalysisDto analysis,
        params Bullet[] bullets)
        => sut.ReviewAsync("A role.", analysis, Baseline(analysis, bullets), bullets, null, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ReviewAsync_CanLowerAStrengthWithoutLimit()
    {
        var analysis = Analysis("azure");
        var strongBullet = new Bullet { Id = AzureBullet.Id, BulletText = "Migrated 40 services to Azure." };
        var sut = CreateSut($$"""
            {"summary":"Thin.","requirements":[{"requirement":"azure","strength":"Missing","bulletIds":[],"reasoning":"The bullet is about migration generally."}]}
            """);

        var review = await ReviewAsync(sut, analysis, strongBullet);

        review!.Evidence.Single().Strength.Should().Be(EvidenceStrengthDto.Missing,
            "a reviewer arguing the evidence is thinner than it looks costs the candidate nothing but honesty");
    }

    [Fact]
    public async Task ReviewAsync_RaisingAStrengthIsCappedAtOneStep()
    {
        var analysis = Analysis("kubernetes");
        var sut = CreateSut($$"""
            {"requirements":[{"requirement":"kubernetes","strength":"Strong","bulletIds":["{{UnrelatedBullet.Id}}"],"reasoning":"Related platform work."}]}
            """);

        var review = await ReviewAsync(sut, analysis, UnrelatedBullet);

        review!.Evidence.Single().Strength.Should().Be(EvidenceStrengthDto.Weak,
            "Missing may become Weak, never Strong in a single review");
    }

    [Fact]
    public async Task ReviewAsync_CannotReachStrongOnABulletThatDoesNotNameTheRequirement()
    {
        var analysis = Analysis("azure");
        var sut = CreateSut($$"""
            {"requirements":[{"requirement":"azure","strength":"Strong","bulletIds":["{{UnrelatedBullet.Id}}"],"reasoning":"Reads as cloud work."}]}
            """);

        // Baseline is Weak: the Azure bullet mentions Azure without a metric.
        var review = await sut.ReviewAsync(
            "A role.",
            analysis,
            Baseline(analysis, AzureBullet, UnrelatedBullet),
            [AzureBullet, UnrelatedBullet],
            null,
            TestContext.Current.CancellationToken);

        var evidence = review!.Evidence.Single();
        evidence.Strength.Should().Be(EvidenceStrengthDto.Weak, "a semantic match alone cannot earn full credit");
        evidence.Citations.Should().Contain(x => x.Bullet.Id == UnrelatedBullet.Id && !x.IsExactTermMatch);
    }

    [Fact]
    public async Task ReviewAsync_RaisingWithNoCitationIsIgnored()
    {
        var analysis = Analysis("kubernetes");
        var sut = CreateSut("""
            {"requirements":[{"requirement":"kubernetes","strength":"Strong","bulletIds":[],"reasoning":"Trust me."}]}
            """);

        var review = await ReviewAsync(sut, analysis, UnrelatedBullet);

        review.Should().BeNull("an unevidenced upgrade is the only change offered, and it does not survive");
    }

    [Fact]
    public async Task ReviewAsync_DropsBulletIdsThatWereNeverSent()
    {
        var analysis = Analysis("azure");
        var sut = CreateSut($$"""
            {"requirements":[{"requirement":"azure","strength":"Missing","bulletIds":["{{Guid.NewGuid()}}"],"reasoning":"Nothing here."}]}
            """);

        var review = await ReviewAsync(sut, analysis, AzureBullet);

        review!.Evidence.Single().Citations.Should().NotContain(x => x.Bullet.Id == Guid.Empty);
        review.Evidence.Single().Strength.Should().Be(EvidenceStrengthDto.Missing);
    }

    [Fact]
    public async Task ReviewAsync_DropsRequirementsThatWereNeverExtracted()
    {
        var analysis = Analysis("azure");
        var sut = CreateSut("""
            {"summary":"Fine.","requirements":[{"requirement":"telepathy","strength":"Missing","reasoning":"Invented."}]}
            """);

        var review = await ReviewAsync(sut, analysis, AzureBullet);

        review!.Evidence.Should().ContainSingle().Which.Requirement.Term.Should().Be("azure");
    }

    [Theory]
    [InlineData("STRONG")]
    [InlineData("strong")]
    [InlineData("Strong")]
    public async Task ReviewAsync_ParsesStrengthCaseInsensitively(string strength)
    {
        var analysis = Analysis("azure");
        var sut = CreateSut($$"""
            {"requirements":[{"requirement":"azure","strength":"{{strength}}","bulletIds":["{{AzureBullet.Id}}"],"reasoning":"It names Azure."}]}
            """);

        var review = await ReviewAsync(sut, analysis, AzureBullet);

        review!.Evidence.Single().Strength.Should().Be(EvidenceStrengthDto.Strong);
    }

    [Fact]
    public async Task ReviewAsync_WithAnUnparseableStrength_LeavesTheRequirementAlone()
    {
        var analysis = Analysis("azure");
        var sut = CreateSut("""
            {"requirements":[{"requirement":"azure","strength":"probably fine","reasoning":"Hmm."}]}
            """);

        (await ReviewAsync(sut, analysis, AzureBullet)).Should().BeNull();
    }

    [Fact]
    public async Task ReviewAsync_TurnsUnsupportedClaimsIntoWarnings()
    {
        var analysis = Analysis("azure");
        var sut = CreateSut($$"""
            {"unsupportedClaims":[{"message":"This bullet claims org-wide impact the rest of the library does not support.","bulletIds":["{{AzureBullet.Id}}"],"reasoning":"No other bullet describes org-wide scope."}]}
            """);

        var review = await ReviewAsync(sut, analysis, AzureBullet);

        var diagnostic = review!.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Severity.Should().Be(DiagnosticSeverityDto.Warning);
        diagnostic.Code.Should().Be(CoverageDiagnosticCodes.UnsupportedClaim);
        diagnostic.BulletIds.Should().ContainSingle().Which.Should().Be(AzureBullet.Id);
        diagnostic.Why.SupportingEvidence.Should().NotBeEmpty("an accusation must show what it is about");
    }

    [Fact]
    public async Task ReviewAsync_DropsAnUnsupportedClaimThatCitesNoKnownBullet()
    {
        var analysis = Analysis("azure");
        var sut = CreateSut("""
            {"unsupportedClaims":[{"message":"Something is wrong somewhere.","bulletIds":[],"reasoning":"Vibes."}]}
            """);

        (await ReviewAsync(sut, analysis, AzureBullet)).Should().BeNull(
            "a warning the user cannot trace to a bullet is the opacity this report removes");
    }

    [Fact]
    public async Task ReviewAsync_WithAnUnparseableResponse_ReturnsNull()
    {
        (await ReviewAsync(CreateSut("not json at all"), Analysis("azure"), AzureBullet))
            .Should().BeNull();
    }

    [Fact]
    public async Task ReviewAsync_WhenTheCallFails_ReturnsNull()
    {
        var sut = CreateThrowingSut(new HttpRequestException("down"));

        (await ReviewAsync(sut, Analysis("azure"), AzureBullet)).Should().BeNull();
    }

    [Fact]
    public async Task ReviewAsync_WhenCancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var analysis = Analysis("azure");
        var sut = CreateThrowingSut(new OperationCanceledException(cts.Token));

        var act = () => sut.ReviewAsync("A role.", analysis, Baseline(analysis, AzureBullet), [AzureBullet], null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ReviewAsync_WithSummaryOnly_StillCountsAsAReview()
    {
        var review = await ReviewAsync(
            CreateSut("""{"summary":"Your library speaks to the platform work but not the leadership ask."}"""),
            Analysis("azure"),
            AzureBullet);

        review!.Summary.Should().StartWith("Your library speaks");
        review.Evidence.Single().Strength.Should().Be(EvidenceStrengthDto.Weak, "the baseline is untouched");
    }
}
