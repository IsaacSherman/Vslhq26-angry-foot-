using System.Text.Json;
using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// Enrichment the author owns. The rule under test throughout is that re-running the tagger is a
/// suggestion, not an authority: what the author wrote survives it, and what they removed stays
/// removed however many times the tagger proposes it again.
/// </summary>
public class BulletEnrichmentTests : IDisposable
{
    private static readonly BulletTagging AiTagging = new(
        Tags: ["Impact"],
        Skills: ["Code Review"],
        Technologies: ["c#"],
        JobCategories: ["Backend Engineering"],
        Impact: ["30%"]);

    private readonly SqliteTestDatabase _database = new();
    private readonly Mock<IBulletTagger> _tagger = new();
    private readonly FakeBulletVectorStore _vectorStore = new();

    public BulletEnrichmentTests()
    {
        _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AiTagging);
    }

    private BulletService CreateSut()
        => new(_database.Context, _tagger.Object, _vectorStore, NullLogger<BulletService>.Instance);

    public void Dispose() => _database.Dispose();

    private async Task<BulletDto> CreateBulletAsync(string text = "Mentored two interns through weekly 1:1s.")
        => await CreateSut().CreateAsync(new CreateBulletRequest(text), TestContext.Current.CancellationToken);

    private static SetBulletEnrichmentRequest Enrichment(
        string[]? tags = null,
        string[]? skills = null,
        string[]? technologies = null,
        string[]? categories = null)
        => new(tags ?? ["Impact"], skills ?? ["Code Review"], technologies ?? ["c#"], categories ?? ["Backend Engineering"]);

    public class SurvivingReEnrichment : BulletEnrichmentTests
    {
        [Fact]
        public async Task AValueTheAuthorAddedIsKeptWhenTheTaggerRunsAgain()
        {
            var bullet = await CreateBulletAsync();
            await CreateSut().SetEnrichmentAsync(
                bullet.Id,
                Enrichment(skills: ["Code Review", "Technical Leadership"]),
                TestContext.Current.CancellationToken);

            var enriched = await CreateSut().EnrichAsync(bullet.Id, TestContext.Current.CancellationToken);

            enriched!.Skills.Should().Contain("Technical Leadership", "the author knows what the work was");
            enriched.Skills.Should().Contain("Code Review", "the tagger still proposes this one");
        }

        [Fact]
        public async Task AValueTheAuthorRemovedIsNotReinstated()
        {
            var bullet = await CreateBulletAsync();
            await CreateSut().SetEnrichmentAsync(
                bullet.Id,
                Enrichment(skills: []),
                TestContext.Current.CancellationToken);

            var enriched = await CreateSut().EnrichAsync(bullet.Id, TestContext.Current.CancellationToken);

            enriched!.Skills.Should().NotContain("Code Review", "a removal that does not stick is not a removal");
        }

        [Fact]
        public async Task EditingTheTextKeepsWhatTheAuthorWrote()
        {
            var bullet = await CreateBulletAsync();
            await CreateSut().SetEnrichmentAsync(
                bullet.Id,
                Enrichment(skills: ["Code Review", "Technical Leadership"]),
                TestContext.Current.CancellationToken);

            var updated = await CreateSut().UpdateAsync(
                bullet.Id,
                new UpdateBulletRequest("Mentored two interns and ran the code review rota."),
                TestContext.Current.CancellationToken);

            updated!.Skills.Should().Contain("Technical Leadership");
        }

        [Fact]
        public async Task PromotingARevisionKeepsWhatTheAuthorWrote()
        {
            var bullet = await CreateBulletAsync();
            await CreateSut().SetEnrichmentAsync(
                bullet.Id,
                Enrichment(skills: ["Code Review", "Technical Leadership"]),
                TestContext.Current.CancellationToken);

            // Promotion routes through UpdateAsync with no tagging, so it re-runs the tagger.
            var promoted = await CreateSut().UpdateAsync(
                bullet.Id,
                new UpdateBulletRequest("Mentored two interns, lifting their first-pass review rate."),
                TestContext.Current.CancellationToken);

            promoted!.Skills.Should().Contain("Technical Leadership");
        }
    }

    public class NotSpendingAiCallsNeedlessly : BulletEnrichmentTests
    {
        [Fact]
        public async Task ChangingOnlyTheEmployerDoesNotReRunTheTagger()
        {
            var bullet = await CreateBulletAsync();
            _tagger.Invocations.Clear();

            await CreateSut().UpdateAsync(
                bullet.Id,
                new UpdateBulletRequest(bullet.BulletText, SourceEmployer: "Marmot Signal Works"),
                TestContext.Current.CancellationToken);

            _tagger.Verify(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
                "enrichment describes the wording, and the wording did not change");
        }

        [Fact]
        public async Task ChangingOnlyTheEmployerLeavesEnrichmentIntact()
        {
            var bullet = await CreateBulletAsync();

            var updated = await CreateSut().UpdateAsync(
                bullet.Id,
                new UpdateBulletRequest(bullet.BulletText, SourceEmployer: "Marmot Signal Works"),
                TestContext.Current.CancellationToken);

            updated!.Skills.Should().BeEquivalentTo(bullet.Skills);
            updated.EnrichmentState.Should().Be(EnrichmentStateDto.Enriched);
        }

        [Fact]
        public async Task ChangingTheTextStillReRunsTheTagger()
        {
            var bullet = await CreateBulletAsync();
            _tagger.Invocations.Clear();

            await CreateSut().UpdateAsync(
                bullet.Id,
                new UpdateBulletRequest("Something else entirely."),
                TestContext.Current.CancellationToken);

            _tagger.Verify(x => x.TagAsync("Something else entirely.", It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    public class Proposals : BulletEnrichmentTests
    {
        [Fact]
        public async Task AProposalReportsWhatWouldChangeWithoutChangingIt()
        {
            var bullet = await CreateBulletAsync();
            await CreateSut().SetEnrichmentAsync(bullet.Id, Enrichment(skills: []), TestContext.Current.CancellationToken);

            var proposal = await CreateSut().ProposeEnrichmentAsync(bullet.Id, TestContext.Current.CancellationToken);

            proposal!.ForText.Should().Be(bullet.BulletText);
            proposal.Facets.Single(x => x.Facet == EnrichmentFacetDto.Skills)
                .Added.Should().Contain("Code Review");

            var unchanged = await CreateSut().GetByIdAsync(bullet.Id, TestContext.Current.CancellationToken);
            unchanged!.Skills.Should().BeEmpty("asking what the tagger thinks must not apply it");
        }

        [Fact]
        public async Task AProposalNeverOffersToRemoveWhatTheAuthorWrote()
        {
            var bullet = await CreateBulletAsync();
            await CreateSut().SetEnrichmentAsync(
                bullet.Id,
                Enrichment(skills: ["Code Review", "Technical Leadership"]),
                TestContext.Current.CancellationToken);

            var proposal = await CreateSut().ProposeEnrichmentAsync(bullet.Id, TestContext.Current.CancellationToken);

            proposal!.Facets.Single(x => x.Facet == EnrichmentFacetDto.Skills)
                .Removed.Should().NotContain(
                    "Technical Leadership",
                    "accepting a proposal wholesale must not be how the author loses their own work");
        }

        [Fact]
        public async Task AProposalReportsWhatBothAgreeOn()
        {
            var bullet = await CreateBulletAsync();

            var proposal = await CreateSut().ProposeEnrichmentAsync(bullet.Id, TestContext.Current.CancellationToken);

            proposal!.Facets.Single(x => x.Facet == EnrichmentFacetDto.Skills)
                .Unchanged.Should().Contain("Code Review");
        }

        [Fact]
        public async Task AProposalForAMissingBulletIsNotFound()
        {
            var proposal = await CreateSut().ProposeEnrichmentAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

            proposal.Should().BeNull();
        }
    }

    public class Provenance : BulletEnrichmentTests
    {
        [Fact]
        public async Task OnlyTheValuesTheAuthorIntroducedAreMarkedAsTheirs()
        {
            var bullet = await CreateBulletAsync();

            var saved = await CreateSut().SetEnrichmentAsync(
                bullet.Id,
                Enrichment(skills: ["Code Review", "Technical Leadership"]),
                TestContext.Current.CancellationToken);

            var skills = saved!.Enrichment!.Skills;
            skills.Single(x => x.Value == "Technical Leadership").Origin.Should().Be(EnrichmentOriginDto.Authored);
            skills.Single(x => x.Value == "Code Review").Origin.Should().Be(
                EnrichmentOriginDto.Suggested,
                "keeping a suggestion is not writing it, and calling it authored would stop enrichment ever refreshing it");
        }

        [Fact]
        public async Task AKeptSuggestionCanStillBeRefreshedByALaterEnrichment()
        {
            var bullet = await CreateBulletAsync();
            await CreateSut().SetEnrichmentAsync(
                bullet.Id,
                Enrichment(skills: ["Code Review", "Technical Leadership"]),
                TestContext.Current.CancellationToken);

            _tagger.Setup(x => x.TagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AiTagging with { Skills = ["Code Review", "Pair Programming"] });

            var enriched = await CreateSut().EnrichAsync(bullet.Id, TestContext.Current.CancellationToken);

            enriched!.Skills.Should().Contain("Pair Programming", "the tagger is still allowed to add to a bullet");
            enriched.Skills.Should().Contain("Technical Leadership", "what the author wrote survives regardless");
        }

        [Fact]
        public async Task AValueTheAuthorNeverTouchedReportsAsSuggested()
        {
            var bullet = await CreateBulletAsync();

            bullet.Enrichment!.Skills.Should().OnlyContain(x => x.Origin == EnrichmentOriginDto.Suggested);
        }

        [Fact]
        public async Task EditingEnrichmentReIndexesTheBullet()
        {
            var bullet = await CreateBulletAsync();
            _vectorStore.Upserted.Clear();

            await CreateSut().SetEnrichmentAsync(
                bullet.Id,
                Enrichment(technologies: ["Python"]),
                TestContext.Current.CancellationToken);

            _vectorStore.Upserted.Should().ContainSingle(
                "skills and technologies are part of a bullet's embedding text, so editing them changes what it retrieves for");
        }

        [Fact]
        public async Task SettingEnrichmentOnAMissingBulletIsNotFound()
        {
            var result = await CreateSut().SetEnrichmentAsync(
                Guid.NewGuid(), Enrichment(), TestContext.Current.CancellationToken);

            result.Should().BeNull();
        }
    }

    public class Storage : BulletEnrichmentTests
    {
        [Fact]
        public void TheMigrationsDefaultForTheNewColumnsIsReadableJson()
        {
            // The migration hard-codes this literal because a value converter cannot supply a SQL
            // default. If the serialized shape ever drifts from it, every pre-existing bullet starts
            // throwing on read - so pin the two together here rather than find out at startup.
            const string migrationDefault = @"{""tags"":[],""skills"":[],""technologies"":[],""jobCategories"":[]}";

            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

            JsonSerializer.Serialize(EnrichmentSet.Empty(), options).Should().Be(migrationDefault);
            JsonSerializer.Deserialize<EnrichmentSet>(migrationDefault, options)!.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public async Task ProvenanceSurvivesAReload()
        {
            var bullet = await CreateBulletAsync();
            await CreateSut().SetEnrichmentAsync(
                bullet.Id,
                Enrichment(skills: ["Technical Leadership"]),
                TestContext.Current.CancellationToken);

            var reloaded = await CreateSut().GetByIdAsync(bullet.Id, TestContext.Current.CancellationToken);

            reloaded!.Enrichment!.Skills.Should().ContainSingle()
                .Which.Should().BeEquivalentTo(new EnrichmentValueDto("Technical Leadership", EnrichmentOriginDto.Authored));
            reloaded.Enrichment.Suppressed.Should().Contain(x => x.Value == "Code Review");
        }
    }
}
