using AngryFoot.ApiService.Application.Profile;
using AngryFoot.Contracts;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class ProfileServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    // A fresh context per service call mirrors the scoped-per-request lifetime in production.
    private ProfileService CreateSut() => new(_database.CreateContext());

    public void Dispose() => _database.Dispose();

    private static ProfileDto Dto(
        string name = "Ada",
        IReadOnlyList<WorkHistoryDto>? work = null,
        IReadOnlyList<EducationDto>? education = null,
        IReadOnlyList<CertificationDto>? certifications = null)
        => new(
            Guid.Empty,
            name,
            "ada@example.com",
            "555-0100",
            "linkedin.com/in/ada",
            "github.com/ada",
            "Summary.",
            work ?? [],
            education ?? [],
            certifications ?? [],
            DateTime.UtcNow);

    [Fact]
    public async Task GetAsync_OnEmptyDatabase_CreatesAndReturnsEmptyProfile()
    {
        var sut = CreateSut();

        var result = await sut.GetAsync(CancellationToken.None);

        result.Id.Should().NotBeEmpty();
        result.Name.Should().BeEmpty();
        _database.Context.Profiles.Should().ContainSingle();
    }

    [Fact]
    public async Task GetAsync_CalledTwice_DoesNotCreateASecondProfile()
    {
        var first = await CreateSut().GetAsync(CancellationToken.None);
        var second = await CreateSut().GetAsync(CancellationToken.None);

        second.Id.Should().Be(first.Id);
        _database.CreateContext().Profiles.Should().ContainSingle();
    }

    [Fact]
    public async Task UpsertAsync_TrimsFields_AndPersistsChildrenInSortOrder()
    {
        var sut = CreateSut();
        var dto = Dto(
            name: "  Ada Lovelace  ",
            work:
            [
                new WorkHistoryDto(Guid.Empty, "Second Corp", null, null, null, null, 1),
                new WorkHistoryDto(Guid.Empty, "  First Corp  ", "Engineer", "Remote", "2020", "2023", 0)
            ],
            education: [new EducationDto(Guid.Empty, "State U", "BS", "CS", "2016", 0)],
            certifications: [new CertificationDto(Guid.Empty, "AZ-204", "Microsoft", "2025", 0)]);

        var result = await sut.UpsertAsync(dto, CancellationToken.None);

        result.Name.Should().Be("Ada Lovelace");
        result.WorkHistory.Should().HaveCount(2);
        result.WorkHistory[0].Employer.Should().Be("First Corp", "children are ordered by sort order and trimmed");
        result.Education.Should().ContainSingle();
        result.Certifications.Should().ContainSingle();
    }

    [Fact]
    public async Task UpsertAsync_ReplacesExistingChildren_InsteadOfAppending()
    {
        await CreateSut().UpsertAsync(Dto(work:
        [
            new WorkHistoryDto(Guid.Empty, "Old Corp", null, null, null, null, 0)
        ]), CancellationToken.None);

        var result = await CreateSut().UpsertAsync(Dto(work:
        [
            new WorkHistoryDto(Guid.Empty, "New Corp", null, null, null, null, 0)
        ]), CancellationToken.None);

        result.WorkHistory.Should().ContainSingle().Which.Employer.Should().Be("New Corp");
        _database.CreateContext().WorkHistory.Should().ContainSingle("the old row must be deleted, not kept alongside");
    }

    [Fact]
    public async Task UpsertAsync_WithNullStringsAndCollections_DoesNotThrowAndDoesNotLoseData()
    {
        await CreateSut().UpsertAsync(Dto(work:
        [
            new WorkHistoryDto(Guid.Empty, "Keep Corp", null, null, null, null, 0)
        ]), CancellationToken.None);

        // Simulates a JSON payload with null fields, which bypasses nullability annotations.
        var hostile = new ProfileDto(
            Guid.Empty, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, DateTime.UtcNow);

        var result = await CreateSut().UpsertAsync(hostile, CancellationToken.None);

        result.Name.Should().BeEmpty("null strings are coalesced to empty");
        result.WorkHistory.Should().BeEmpty("null collections are treated as empty lists");
    }

    [Fact]
    public async Task UpsertAsync_ReusesTheSingleProfileRow()
    {
        var first = await CreateSut().UpsertAsync(Dto(name: "First"), CancellationToken.None);
        var second = await CreateSut().UpsertAsync(Dto(name: "Second"), CancellationToken.None);

        second.Id.Should().Be(first.Id);
        _database.CreateContext().Profiles.Should().ContainSingle();
    }
}
