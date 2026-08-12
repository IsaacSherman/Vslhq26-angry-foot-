using System.Text.RegularExpressions;
using AngryFoot.ApiService.Application.Benchmarks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// Guards the dataset that actually ships. It is hand-refreshed from O*NET rather than fetched
/// at runtime, so a bad refresh has to fail here rather than in front of a user.
/// </summary>
public class OccupationBenchmarkDatasetTests
{
    private static readonly OccupationBenchmarkData Data = LoadShippedDataset();

    private static OccupationBenchmarkData LoadShippedDataset()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Application", "Benchmarks", "Data", OccupationBenchmarkDataset.DataFileName);
        return OccupationBenchmarkDataset.Load(path, NullLogger.Instance);
    }

    [Fact]
    public void ShippedDataset_Loads()
    {
        Data.IsAvailable.Should().BeTrue("the dataset is copied to the output directory by the ApiService project");
        Data.Occupations.Should().HaveCountGreaterThan(10);
        Data.SourceVersion.Should().NotBeNullOrWhiteSpace();
        Data.Attribution.Should().Contain("O*NET", "the CC BY 4.0 licence requires attribution");
    }

    [Fact]
    public void ShippedDataset_HasUniqueValidSocCodes()
    {
        Data.Occupations.Select(o => o.SocCode).Should().OnlyHaveUniqueItems();

        foreach (var occupation in Data.Occupations)
        {
            Regex.IsMatch(occupation.SocCode, @"^\d{2}-\d{4}\.\d{2}$")
                .Should().BeTrue($"'{occupation.SocCode}' should be a SOC code");
        }
    }

    [Fact]
    public void ShippedDataset_HasUsableItemsForEveryOccupation()
    {
        foreach (var occupation in Data.Occupations)
        {
            occupation.Title.Should().NotBeNullOrWhiteSpace();
            occupation.Items.Should().NotBeEmpty($"{occupation.Title} needs requirements to benchmark against");

            foreach (var item in occupation.Items)
            {
                item.Name.Should().NotBeNullOrWhiteSpace();
                item.Importance.Should().BeInRange(1, 100);
                item.EvidenceTerms.Should().NotBeEmpty($"'{item.Name}' needs at least one term to match bullets on");
                item.EvidenceTerms.Should().OnlyContain(term => term.Length >= 3,
                    "terms shorter than three characters match letters inside unrelated words");
            }
        }
    }

    [Fact]
    public void ShippedDataset_MapsTheCommonTechRoleTitles()
    {
        string[] titles =
        [
            "Senior Software Engineer",
            "Software Engineer II",
            "Web Developer",
            "Data Scientist",
            "Information Security Analyst",
            "Database Administrator",
            "QA Engineer",
            "IT Project Manager"
        ];

        foreach (var title in titles)
        {
            OccupationTitleMatcher.Match(title, Data.Occupations)
                .Should().NotBeNull($"'{title}' is a common target title and should map to an occupation");
        }
    }

    [Fact]
    public void ShippedDataset_MapsSoftwareEngineerToSoftwareDevelopers()
    {
        var match = OccupationTitleMatcher.Match("Senior Software Engineer", Data.Occupations);

        match!.Occupation.SocCode.Should().Be("15-1252.00");
    }

    [Fact]
    public void ShippedDataset_DoesNotMapUnrelatedOccupations()
    {
        OccupationTitleMatcher.Match("Registered Nurse", Data.Occupations)
            .Should().BeNull("the dataset covers technology occupations, and a wrong match is worse than none");
    }
}
