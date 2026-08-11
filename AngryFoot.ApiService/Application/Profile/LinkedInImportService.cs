using System.Globalization;
using System.IO.Compression;
using AngryFoot.Contracts;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;

namespace AngryFoot.ApiService.Application.Profile;

public sealed class InvalidLinkedInExportException : Exception
{
    public InvalidLinkedInExportException(string message) : base(message)
    {
    }

    public InvalidLinkedInExportException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public interface ILinkedInProfileImportService
{
    Task<LinkedInImportResultDto> ImportAsync(Stream zipStream, CancellationToken cancellationToken);
}

/// <summary>
/// Parses a LinkedIn "Get a copy of your data" export (a zip of CSVs) into a
/// prefilled <see cref="ProfileDto"/> for the user to review before saving.
/// Never writes to the database directly &#8212; <see cref="IProfileService.UpsertAsync"/>
/// remains the sole persistence path.
/// </summary>
public sealed class LinkedInImportService(IProfileService profileService) : ILinkedInProfileImportService
{
    private const string PositionsFileName = "Positions.csv";
    private const string EducationFileName = "Education.csv";
    private const string CertificationsFileName = "Certifications.csv";

    // LinkedIn's export schema varies slightly by locale/version, but these filenames
    // are stable. A subset may be absent if the user's export is partial (e.g. the
    // Profile-only download, which omits Positions/Education/Certifications entirely).
    private static readonly string[] RecognizedFileNames =
    [
        "Profile.csv", "Email Addresses.csv", PositionsFileName, EducationFileName, CertificationsFileName
    ];

    public async Task<LinkedInImportResultDto> ImportAsync(Stream zipStream, CancellationToken cancellationToken)
    {
        using var archive = OpenArchive(zipStream);

        var entriesByName = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (!RecognizedFileNames.Any(entriesByName.ContainsKey))
        {
            throw new InvalidLinkedInExportException(
                "This doesn't look like a LinkedIn data export. Expected files such as Profile.csv or " +
                "Positions.csv were not found in the archive.");
        }

        var current = await profileService.GetAsync(cancellationToken);

        var (name, summary) = ReadProfile(entriesByName);
        var email = ReadPrimaryEmail(entriesByName);
        var workHistory = ReadWorkHistory(entriesByName);
        var education = ReadEducation(entriesByName);
        var certifications = ReadCertifications(entriesByName);

        var profile = current with
        {
            Name = name ?? current.Name,
            Email = email ?? current.Email,
            ProfessionalSummary = summary ?? current.ProfessionalSummary,
            WorkHistory = workHistory ?? current.WorkHistory,
            Education = education ?? current.Education,
            Certifications = certifications ?? current.Certifications
        };

        return new LinkedInImportResultDto(
            profile,
            WorkHistoryFound: entriesByName.ContainsKey(PositionsFileName),
            EducationFound: entriesByName.ContainsKey(EducationFileName),
            CertificationsFound: entriesByName.ContainsKey(CertificationsFileName));
    }

    private static ZipArchive OpenArchive(Stream zipStream)
    {
        try
        {
            return new ZipArchive(zipStream, ZipArchiveMode.Read);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidLinkedInExportException("The uploaded file is not a valid zip archive.", ex);
        }
    }

    private static (string? Name, string? Summary) ReadProfile(IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        var row = ReadRows<ProfileRow>(entries, "Profile.csv").FirstOrDefault();
        if (row is null)
        {
            return (null, null);
        }

        var name = string.Join(" ", new[] { row.FirstName, row.LastName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        return (
            string.IsNullOrWhiteSpace(name) ? null : name,
            string.IsNullOrWhiteSpace(row.Summary) ? null : row.Summary.Trim());
    }

    private static string? ReadPrimaryEmail(IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        return ReadRows<EmailRow>(entries, "Email Addresses.csv")
            .Select(row => row.EmailAddress?.Trim())
            .FirstOrDefault(email => !string.IsNullOrWhiteSpace(email));
    }

    private static IReadOnlyList<WorkHistoryDto>? ReadWorkHistory(IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        var rows = ReadRows<PositionRow>(entries, "Positions.csv");
        if (rows.Count == 0)
        {
            return null;
        }

        return rows
            .Select((row, index) => new WorkHistoryDto(
                Guid.Empty,
                row.CompanyName?.Trim() ?? string.Empty,
                row.Title?.Trim(),
                row.Location?.Trim(),
                row.StartedOn?.Trim(),
                row.FinishedOn?.Trim(),
                index))
            .ToArray();
    }

    private static IReadOnlyList<EducationDto>? ReadEducation(IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        var rows = ReadRows<EducationRow>(entries, "Education.csv");
        if (rows.Count == 0)
        {
            return null;
        }

        return rows
            .Select((row, index) => new EducationDto(
                Guid.Empty,
                row.SchoolName?.Trim() ?? string.Empty,
                row.DegreeName?.Trim(),
                row.FieldOfStudy?.Trim(),
                row.EndDate?.Trim(),
                index))
            .ToArray();
    }

    private static IReadOnlyList<CertificationDto>? ReadCertifications(IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        var rows = ReadRows<CertificationRow>(entries, "Certifications.csv");
        if (rows.Count == 0)
        {
            return null;
        }

        return rows
            .Select((row, index) => new CertificationDto(
                Guid.Empty,
                row.Name?.Trim() ?? string.Empty,
                row.Authority?.Trim(),
                row.StartedOn?.Trim(),
                index))
            .ToArray();
    }

    private static List<T> ReadRows<T>(IReadOnlyDictionary<string, ZipArchiveEntry> entries, string fileName) where T : class
    {
        if (!entries.TryGetValue(fileName, out var entry))
        {
            return [];
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        // LinkedIn's headers drift slightly across export versions/locales, so missing
        // columns and unexpected extra columns must not fail the whole import.
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            MissingFieldFound = null,
            HeaderValidated = null
        });

        return csv.GetRecords<T>().ToList();
    }

    private sealed class ProfileRow
    {
        [Name("First Name")]
        public string? FirstName { get; set; }

        [Name("Last Name")]
        public string? LastName { get; set; }

        [Name("Summary")]
        public string? Summary { get; set; }
    }

    private sealed class EmailRow
    {
        [Name("Email Address")]
        public string? EmailAddress { get; set; }
    }

    private sealed class PositionRow
    {
        [Name("Company Name")]
        public string? CompanyName { get; set; }

        [Name("Title")]
        public string? Title { get; set; }

        [Name("Location")]
        public string? Location { get; set; }

        [Name("Started On")]
        public string? StartedOn { get; set; }

        [Name("Finished On")]
        public string? FinishedOn { get; set; }
    }

    private sealed class EducationRow
    {
        [Name("School Name")]
        public string? SchoolName { get; set; }

        [Name("Degree Name")]
        public string? DegreeName { get; set; }

        [Name("Field Of Study")]
        public string? FieldOfStudy { get; set; }

        [Name("End Date")]
        public string? EndDate { get; set; }
    }

    private sealed class CertificationRow
    {
        [Name("Name")]
        public string? Name { get; set; }

        [Name("Authority")]
        public string? Authority { get; set; }

        [Name("Started On")]
        public string? StartedOn { get; set; }
    }
}
