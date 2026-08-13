using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Application.Artifacts;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class ArtifactServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private ArtifactService CreateSut() => new(_database.Context);

    private static RefinementDto Refinement(string recommended = DraftVersionLabels.Synthesis) => new(
        recommended,
        "Too generic.",
        [
            new DraftVersionDto(DraftVersionLabels.InitialDraft, "Initial draft", "", "First draft body."),
            new DraftVersionDto(DraftVersionLabels.Synthesis, "Synthesis", "", "Merged body.")
        ]);

    private Guid SeedArtifact(bool withRefinements = true)
    {
        var artifact = new GenerationArtifact
        {
            Id = Guid.NewGuid(),
            JobDescription = "A role.",
            ResumeMarkdown = "Merged body.",
            CoverLetterMarkdown = "Merged body.",
            CreatedDate = DateTime.UtcNow,
            ResumeRefinementJson = withRefinements ? AiJsonUtilities.ToJson(Refinement()) : null,
            CoverLetterRefinementJson = withRefinements ? AiJsonUtilities.ToJson(Refinement()) : null
        };

        _database.Context.GenerationArtifacts.Add(artifact);
        _database.Context.SaveChanges();
        return artifact.Id;
    }

    [Fact]
    public async Task SelectVersionsAsync_PromotesTheChosenVersionAndRemembersIt()
    {
        var id = SeedArtifact();

        var result = await CreateSut().SelectVersionsAsync(
            id,
            new SelectArtifactVersionsRequest(DraftVersionLabels.InitialDraft, null),
            CancellationToken.None);

        result.Status.Should().Be(VersionSelectionStatus.Updated);
        result.Artifact!.ResumeMarkdown.Should().Be("First draft body.");
        result.Artifact.ResumeRefinement!.RecommendedLabel.Should().Be(
            DraftVersionLabels.InitialDraft,
            "reloading the artifact should show the picker on the user's choice");
        result.Artifact.CoverLetterMarkdown.Should().Be("Merged body.", "a null label leaves that document alone");
    }

    [Fact]
    public async Task SelectVersionsAsync_MatchesLabelsCaseInsensitively()
    {
        var id = SeedArtifact();

        var result = await CreateSut().SelectVersionsAsync(
            id,
            new SelectArtifactVersionsRequest(null, "V1"),
            CancellationToken.None);

        result.Status.Should().Be(VersionSelectionStatus.Updated);
        result.Artifact!.CoverLetterMarkdown.Should().Be("First draft body.");
    }

    [Fact]
    public async Task SelectVersionsAsync_WithAnUnknownLabel_ChangesNothing()
    {
        var id = SeedArtifact();

        var result = await CreateSut().SelectVersionsAsync(
            id,
            new SelectArtifactVersionsRequest(DraftVersionLabels.InitialDraft, "not-a-version"),
            CancellationToken.None);

        result.Status.Should().Be(VersionSelectionStatus.UnknownVersionLabel);
        _database.Context.GenerationArtifacts.Single().ResumeMarkdown.Should().Be(
            "Merged body.",
            "one bad label rolls back the whole request rather than half-applying it");
    }

    [Fact]
    public async Task SelectVersionsAsync_OnAGenerationWithoutDeepReview_ReportsTheUnknownLabel()
    {
        var id = SeedArtifact(withRefinements: false);

        var result = await CreateSut().SelectVersionsAsync(
            id,
            new SelectArtifactVersionsRequest(DraftVersionLabels.Synthesis, null),
            CancellationToken.None);

        result.Status.Should().Be(VersionSelectionStatus.UnknownVersionLabel);
    }

    [Fact]
    public async Task SelectVersionsAsync_WithAnUnknownArtifact_ReportsNotFound()
    {
        var result = await CreateSut().SelectVersionsAsync(
            Guid.NewGuid(),
            new SelectArtifactVersionsRequest(DraftVersionLabels.Synthesis, null),
            CancellationToken.None);

        result.Status.Should().Be(VersionSelectionStatus.ArtifactNotFound);
        result.Artifact.Should().BeNull();
    }
}
