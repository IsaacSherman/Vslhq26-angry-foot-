using System.IO.Compression;
using System.Text;
using AngryFoot.ApiService.Application.Profile;
using AngryFoot.Contracts;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class LinkedInImportServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    // Fresh ProfileService/LinkedInImportService per call mirrors the scoped-per-request
    // lifetime in production, same as ProfileServiceTests.
    private ProfileService CreateProfileService() => new(_database.CreateContext());
    private LinkedInImportService CreateSut() => new(CreateProfileService());

    public void Dispose() => _database.Dispose();

    private static MemoryStream BuildZip(params (string FileName, string Content)[] files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (fileName, content) in files)
            {
                var entry = archive.CreateEntry(fileName);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private const string ProfileCsv = "First Name,Last Name,Summary\nAda,Lovelace,\"Building analytical engines, one bit at a time.\"\n";
    private const string EmailCsv = "Email Address\nada@example.com\n";
    private const string PositionsCsv = "Company Name,Title,Location,Started On,Finished On\nAcme Corp,Senior Engineer,Remote,Jan 2020,Dec 2022\nBeta Inc,Engineer,NYC,Jan 2018,Dec 2019\n";
    private const string EducationCsv = "School Name,Degree Name,Field Of Study,End Date\nState University,BS,Computer Science,2016\n";
    private const string CertificationsCsv = "Name,Authority,Started On\nAZ-204,Microsoft,2025\n";

    [Fact]
    public async Task ImportAsync_WithFullExport_MapsAllFields()
    {
        var sut = CreateSut();
        using var zip = BuildZip(
            ("Profile.csv", ProfileCsv),
            ("Email Addresses.csv", EmailCsv),
            ("Positions.csv", PositionsCsv),
            ("Education.csv", EducationCsv),
            ("Certifications.csv", CertificationsCsv));

        var result = await sut.ImportAsync(zip, CancellationToken.None);

        result.Profile.Name.Should().Be("Ada Lovelace");
        result.Profile.Email.Should().Be("ada@example.com");
        result.Profile.ProfessionalSummary.Should().Be("Building analytical engines, one bit at a time.");

        result.Profile.WorkHistory.Should().HaveCount(2);
        result.Profile.WorkHistory[0].Employer.Should().Be("Acme Corp");
        result.Profile.WorkHistory[0].SortOrder.Should().Be(0, "export order is already most-recent-first");
        result.Profile.WorkHistory[1].Employer.Should().Be("Beta Inc");

        result.Profile.Education.Should().ContainSingle().Which.Institution.Should().Be("State University");
        result.Profile.Certifications.Should().ContainSingle().Which.Name.Should().Be("AZ-204");

        result.WorkHistoryFound.Should().BeTrue();
        result.EducationFound.Should().BeTrue();
        result.CertificationsFound.Should().BeTrue();
    }

    [Fact]
    public async Task ImportAsync_WithOnlyPositionsCsv_PopulatesWorkHistory_AndLeavesRestEmpty()
    {
        var sut = CreateSut();
        using var zip = BuildZip(("Positions.csv", PositionsCsv));

        var result = await sut.ImportAsync(zip, CancellationToken.None);

        result.Profile.Name.Should().BeEmpty("Profile.csv was not part of this partial export");
        result.Profile.WorkHistory.Should().HaveCount(2);
        result.Profile.Education.Should().BeEmpty();
        result.Profile.Certifications.Should().BeEmpty();

        result.WorkHistoryFound.Should().BeTrue();
        result.EducationFound.Should().BeFalse("Education.csv was not part of this export, e.g. the Profile-only download");
        result.CertificationsFound.Should().BeFalse("Certifications.csv was not part of this export, e.g. the Profile-only download");
    }

    [Fact]
    public async Task ImportAsync_WithNoRecognizedFiles_ThrowsInvalidLinkedInExportException()
    {
        var sut = CreateSut();
        using var zip = BuildZip(("Random.csv", "Column\nValue\n"));

        var act = () => sut.ImportAsync(zip, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidLinkedInExportException>();
    }

    [Fact]
    public async Task ImportAsync_PreservesFieldsNotPresentInTheExport()
    {
        var profileService = CreateProfileService();
        await profileService.UpsertAsync(
            new ProfileDto(Guid.Empty, "Old Name", "old@example.com", "555-0100", "linkedin.com/in/ada", "github.com/ada", "Old summary.", [], [], [], DateTime.UtcNow),
            CancellationToken.None);

        var sut = new LinkedInImportService(profileService);
        using var zip = BuildZip(("Positions.csv", PositionsCsv));

        var result = await sut.ImportAsync(zip, CancellationToken.None);

        result.Profile.Phone.Should().Be("555-0100", "the export has no phone field, so the existing value must survive");
        result.Profile.LinkedIn.Should().Be("linkedin.com/in/ada");
        result.Profile.GitHub.Should().Be("github.com/ada");
        result.Profile.Name.Should().Be("Old Name", "Profile.csv wasn't in this export, so the existing name must survive");
        result.Profile.WorkHistory.Should().HaveCount(2);
    }
}
