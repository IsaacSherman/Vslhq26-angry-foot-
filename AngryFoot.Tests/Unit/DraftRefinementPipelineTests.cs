using AngryFoot.ApiService.Application.Refinement;
using AngryFoot.Contracts;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

public class DraftRefinementPipelineTests
{
    private const string CriticJson = """{"critique":"No metric, and 'various systems' is vague.","alternative":"The reviewer's bullet."}""";
    private const string ReviserJson = """{"revised":"The author's second attempt."}""";
    private const string ArbiterJson = """{"merged":"The merged bullet.","rationale":"Kept v1a's structure and v2's specificity."}""";

    private static RefinementRequest Request(string draft = "The first draft.") => new(
        ArtifactKind: "resume bullet",
        OutputContract: "a single resume bullet as plain text",
        SourceMaterial: "The bullet the candidate wrote: did stuff with various systems",
        Draft: draft);

    /// <summary>
    /// Routes each call to the agent it belongs to by matching the system prompt, and keeps every
    /// prompt so tests can assert on what each agent was and was not shown.
    /// </summary>
    private sealed class AgentScript
    {
        public string? Critic { get; init; } = CriticJson;
        public string? Reviser { get; init; } = ReviserJson;
        public string? Arbiter { get; init; } = ArbiterJson;

        public List<string> Prompts { get; } = [];
        public string CriticPrompt => Prompts.Single(x => IsCritic(x));
        public string ReviserPrompt => Prompts.Single(x => IsReviser(x));
        public string ArbiterPrompt => Prompts.Single(x => IsArbiter(x));

        public ScriptedChatClient ToChatClient() => new((messages, _) =>
        {
            var conversation = string.Join("\n", messages.Select(x => x.Text));
            Prompts.Add(conversation);

            var response =
                IsCritic(conversation) ? Critic
                : IsReviser(conversation) ? Reviser
                : IsArbiter(conversation) ? Arbiter
                : throw new InvalidOperationException("Unrecognized agent prompt.");

            return response ?? throw new HttpRequestException("agent unavailable");
        });

        private static bool IsCritic(string prompt) => prompt.Contains("exacting resume editor");
        private static bool IsReviser(string prompt) => prompt.Contains("You wrote the draft");
        private static bool IsArbiter(string prompt) => prompt.Contains("final arbiter");
    }

    private static DraftRefinementPipeline CreateSut(AgentScript script, IRefinementGrounding? grounding = null)
        => new(
            script.ToChatClient(),
            grounding ?? new FakeRefinementGrounding(),
            NullLogger<DraftRefinementPipeline>.Instance);

    [Fact]
    public async Task RefineAsync_ProducesAllFourVersions_AndRecommendsTheSynthesis()
    {
        var script = new AgentScript();
        var sut = CreateSut(script);

        var result = await sut.RefineAsync(Request(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Versions.Select(x => x.Label).Should().Equal(
            DraftVersionLabels.InitialDraft,
            DraftVersionLabels.CriticAlternative,
            DraftVersionLabels.AuthorRevision,
            DraftVersionLabels.Synthesis);
        result.RecommendedLabel.Should().Be(DraftVersionLabels.Synthesis);
        result.Recommended!.Text.Should().Be("The merged bullet.");
        result.Critique.Should().Be("No metric, and 'various systems' is vague.");
        result.Versions[0].Text.Should().Be("The first draft.", "v1 is the caller's draft, untouched");
        result.Versions[3].Rationale.Should().Be("Kept v1a's structure and v2's specificity.");
    }

    [Fact]
    public async Task RefineAsync_DoesNotShowTheAuthorTheReviewersAlternative()
    {
        var script = new AgentScript();
        var sut = CreateSut(script);

        await sut.RefineAsync(Request(), CancellationToken.None);

        script.ReviserPrompt.Should().Contain("No metric", "the author revises against the critique");
        script.ReviserPrompt.Should().NotContain(
            "The reviewer's bullet.",
            "showing the author the alternative would collapse v1a and v2 into the same answer");
        script.ArbiterPrompt.Should().Contain("The reviewer's bullet.", "the arbiter compares every version");
        script.ArbiterPrompt.Should().Contain("The author's second attempt.");
    }

    [Fact]
    public async Task RefineAsync_PassesGroundingToTheCriticAndArbiterOnly()
    {
        var grounding = new FakeRefinementGrounding("- Shipped the billing service.");
        var script = new AgentScript();
        var sut = CreateSut(script, grounding);

        await sut.RefineAsync(Request(), CancellationToken.None);

        grounding.Queries.Should().ContainSingle("grounding is retrieved once and reused");
        script.CriticPrompt.Should().Contain("Shipped the billing service.");
        script.ArbiterPrompt.Should().Contain("Shipped the billing service.");
        script.ReviserPrompt.Should().NotContain("Shipped the billing service.");
    }

    /// <summary>
    /// Observed against real AI: given a vague bullet, the reviewer harvested achievements out of
    /// the grounding block and attributed them to work the bullet was not about. Grounding exists
    /// to catch overreach, so licensing it as a source of claims defeats the point of the pass.
    /// </summary>
    [Fact]
    public async Task RefineAsync_ForbidsTheReviewerFromImportingOtherBulletsAchievements()
    {
        var grounding = new FakeRefinementGrounding("- Cut release time from 20 minutes to under 2.");
        var script = new AgentScript();
        var sut = CreateSut(script, grounding);

        await sut.RefineAsync(Request(), CancellationToken.None);

        script.CriticPrompt.Should().Contain(
            "come from this item's own source material",
            "claims are sourced from the item being refined, not from the library");
        script.CriticPrompt.Should().Contain(
            "do not copy their achievements",
            "the grounding block has to say what it is not for, or it reads as source material");
    }

    [Fact]
    public async Task RefineAsync_UsesTheGroundingQueryWhenGiven()
    {
        var grounding = new FakeRefinementGrounding();
        var sut = CreateSut(new AgentScript(), grounding);

        await sut.RefineAsync(Request() with { GroundingQuery = "the original bullet" }, CancellationToken.None);

        grounding.Queries.Should().ContainSingle().Which.Should().Be("the original bullet");
    }

    [Fact]
    public async Task CritiqueAsync_StopsAfterTheReviewer()
    {
        var script = new AgentScript();
        var sut = CreateSut(script);

        var critique = await sut.CritiqueAsync(Request(), CancellationToken.None);

        critique!.Critique.Should().Be("No metric, and 'various systems' is vague.");
        critique.Alternative.Should().Be("The reviewer's bullet.");
        script.Prompts.Should().ContainSingle("the revision and synthesis stages wait for the user");
    }

    [Fact]
    public async Task CompleteAsync_RunsTheRemainingStagesAgainstAnExistingCritique()
    {
        var script = new AgentScript();
        var sut = CreateSut(script);

        var result = await sut.CompleteAsync(
            Request(),
            new RefinementCritique("Too vague.", "The reviewer's bullet."),
            CancellationToken.None);

        result!.Versions.Select(x => x.Label).Should().Equal(
            DraftVersionLabels.InitialDraft,
            DraftVersionLabels.CriticAlternative,
            DraftVersionLabels.AuthorRevision,
            DraftVersionLabels.Synthesis);
        script.Prompts.Should().HaveCount(2, "the reviewer already ran");
        script.ReviserPrompt.Should().Contain("Too vague.");
    }

    [Fact]
    public async Task CompleteAsync_TreatsUserGuidanceAsBindingForTheRemainingAgents()
    {
        var script = new AgentScript();
        var sut = CreateSut(script);

        await sut.CompleteAsync(
            Request() with { UserGuidance = "  'systems' means HVAC controls, not software  " },
            new RefinementCritique("Too vague.", "The reviewer's bullet."),
            CancellationToken.None);

        foreach (var prompt in new[] { script.ReviserPrompt, script.ArbiterPrompt })
        {
            prompt.Should().Contain("'systems' means HVAC controls, not software");
            prompt.Should().Contain("binding", "guidance outranks the critique rather than competing with it");
        }
    }

    [Fact]
    public async Task CompleteAsync_WithoutGuidance_SaysNothingAboutIt()
    {
        var script = new AgentScript();
        var sut = CreateSut(script);

        await sut.CompleteAsync(Request(), new RefinementCritique("Too vague.", null), CancellationToken.None);

        script.ReviserPrompt.Should().NotContain("clarified");
        script.ArbiterPrompt.Should().NotContain("clarified");
    }

    [Fact]
    public async Task CompleteAsync_WithABlankCritique_ReturnsNullWithoutCallingAi()
    {
        var script = new AgentScript();
        var sut = CreateSut(script);

        var result = await sut.CompleteAsync(Request(), new RefinementCritique("   ", null), CancellationToken.None);

        result.Should().BeNull();
        script.Prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task RefineAsync_WhenTheCritiqueFails_ReturnsNull()
    {
        var sut = CreateSut(new AgentScript { Critic = "not json" });

        var result = await sut.RefineAsync(Request(), CancellationToken.None);

        result.Should().BeNull("with no critique there is nothing for the later stages to work from");
    }

    [Fact]
    public async Task RefineAsync_WhenTheRevisionFails_DropsThatVersionAndKeepsGoing()
    {
        var sut = CreateSut(new AgentScript { Reviser = null });

        var result = await sut.RefineAsync(Request(), CancellationToken.None);

        result!.Versions.Select(x => x.Label).Should().NotContain(DraftVersionLabels.AuthorRevision);
        result.RecommendedLabel.Should().Be(DraftVersionLabels.Synthesis);
    }

    [Fact]
    public async Task RefineAsync_WhenTheSynthesisFails_RecommendsTheAuthorsRevision()
    {
        var sut = CreateSut(new AgentScript { Arbiter = "no json here either" });

        var result = await sut.RefineAsync(Request(), CancellationToken.None);

        result!.Versions.Select(x => x.Label).Should().NotContain(DraftVersionLabels.Synthesis);
        result.RecommendedLabel.Should().Be(DraftVersionLabels.AuthorRevision);
        result.Recommended!.Text.Should().Be("The author's second attempt.");
    }

    [Fact]
    public async Task RefineAsync_WhenOnlyTheCritiqueSurvives_RecommendsTheInitialDraft()
    {
        var sut = CreateSut(new AgentScript { Reviser = null, Arbiter = null });

        var result = await sut.RefineAsync(Request(), CancellationToken.None);

        result!.RecommendedLabel.Should().Be(
            DraftVersionLabels.InitialDraft,
            "the reviewer's alternative is the least-vetted version and is never recommended on its own");
    }

    [Fact]
    public async Task RefineAsync_WithBlankDraft_MakesNoAiCalls()
    {
        var script = new AgentScript();
        var sut = CreateSut(script);

        var result = await sut.RefineAsync(Request("   "), CancellationToken.None);

        result.Should().BeNull();
        script.Prompts.Should().BeEmpty();
    }

    [Fact]
    public async Task RefineAsync_WithNoAlternativeFromTheCritic_StillOffersTheRevision()
    {
        var sut = CreateSut(new AgentScript
        {
            Critic = """{"critique":"Too vague.","alternative":"  "}""",
            Arbiter = null
        });

        var result = await sut.RefineAsync(Request(), CancellationToken.None);

        result!.Versions.Select(x => x.Label).Should().Equal(
            DraftVersionLabels.InitialDraft,
            DraftVersionLabels.AuthorRevision);
    }
}
