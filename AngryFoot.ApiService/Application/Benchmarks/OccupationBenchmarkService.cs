using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Data;
using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;
using Microsoft.EntityFrameworkCore;

namespace AngryFoot.ApiService.Application.Benchmarks;

public interface IOccupationBenchmarkService
{
    /// <summary>
    /// Compares the bullet library against aggregate occupational data for the occupation the
    /// target job title maps to. Returns <c>null</c> when no benchmark dataset is available.
    /// </summary>
    Task<OccupationBenchmarkDto?> BuildAsync(
        string? jobTitle, JobAnalysisDto analysis, CancellationToken cancellationToken);
}

/// <summary>
/// Benchmarks the user's bullet library against published, aggregate labor-market data.
/// <para>
/// The comparison set is an occupational profile from a government dataset - never other
/// people's profiles, never a specific employer's staff. No individual-level data is read,
/// stored, or displayed anywhere in this feature, by design (see issue #5).
/// </para>
/// </summary>
internal sealed class OccupationBenchmarkService(
    AngryFootDbContext dbContext,
    IOccupationBenchmarkDataset dataset,
    ILogger<OccupationBenchmarkService> logger) : IOccupationBenchmarkService
{
    private const int MaxListItems = 8;

    public async Task<OccupationBenchmarkDto?> BuildAsync(
        string? jobTitle, JobAnalysisDto analysis, CancellationToken cancellationToken)
    {
        var data = dataset.Data;
        if (!data.IsAvailable)
        {
            return null;
        }

        // The title the user typed wins; the analyzer's inferred title is the fallback.
        var title = FirstNonBlank(jobTitle, analysis.InferredTitle);
        var match = OccupationTitleMatcher.Match(title, data.Occupations);

        if (match is null)
        {
            logger.LogInformation("No occupational match for job title {JobTitle}.", title);
            return Unmatched(title, data);
        }

        var bullets = await dbContext.Bullets.AsNoTracking().ToListAsync(cancellationToken);
        return Compare(match, bullets, data);
    }

    internal static OccupationBenchmarkDto Compare(
        OccupationMatch match, IReadOnlyList<Bullet> bullets, OccupationBenchmarkData data)
    {
        var occupation = match.Occupation;

        var covered = new List<BenchmarkItemDto>();
        var missing = new List<BenchmarkItemDto>();

        foreach (var item in occupation.Items)
        {
            var dto = new BenchmarkItemDto(item.Name, item.Kind, item.Importance);
            if (bullets.Any(bullet => BulletEvidence.SupportsAny(bullet, item.EvidenceTerms)))
            {
                covered.Add(dto);
            }
            else
            {
                missing.Add(dto);
            }
        }

        var totalWeight = occupation.Items.Sum(item => item.Importance);
        var coverage = totalWeight == 0
            ? 0
            : (int)Math.Round(100.0 * covered.Sum(item => item.Importance) / totalWeight);

        return new OccupationBenchmarkDto(
            occupation.SocCode,
            occupation.Title,
            match.Confidence.ToString(),
            match.MatchedTitle,
            coverage,
            Summarize(match, coverage, covered.Count, occupation.Items.Count),
            covered.Count,
            occupation.Items.Count,
            Rank(covered),
            Rank(missing),
            data.Attribution);
    }

    private static OccupationBenchmarkDto Unmatched(string? title, OccupationBenchmarkData data)
    {
        var summary = string.IsNullOrWhiteSpace(title)
            ? "No job title was given for this posting, so it could not be mapped to an occupation. "
              + "Enter a job title above to compare your library against typical requirements for the occupation."
            : $"\"{title}\" did not map to any occupation in the bundled dataset, which covers technology "
              + "and technology-adjacent occupations only. No occupational comparison is available for this role.";

        return new OccupationBenchmarkDto(
            null,
            null,
            OccupationMatchConfidence.None.ToString(),
            null,
            0,
            summary,
            0,
            0,
            [],
            [],
            data.Attribution);
    }

    private static string Summarize(OccupationMatch match, int coverage, int coveredCount, int totalCount)
    {
        var qualifier = match.Confidence == OccupationMatchConfidence.Exact
            ? string.Empty
            : $" This is the closest occupational match to \"{match.MatchedTitle}\" rather than an exact one.";

        var reading = coverage switch
        {
            >= 75 => $"Your bullets evidence {coveredCount} of the {totalCount} requirements typical of this occupation - broad coverage of what the occupation usually demands.",
            >= 50 => $"Your bullets evidence {coveredCount} of the {totalCount} requirements typical of this occupation, leaving some common expectations unevidenced.",
            >= 25 => $"Your bullets evidence only {coveredCount} of the {totalCount} requirements typical of this occupation; several common expectations have no supporting bullet.",
            _ => $"Your bullets evidence just {coveredCount} of the {totalCount} requirements typical of this occupation, so most of what the occupation usually demands is unevidenced."
        };

        return $"{reading} These are aggregate expectations published for the occupation, not a comparison "
            + $"against any specific person or employer's staff.{qualifier}";
    }

    private static IReadOnlyList<BenchmarkItemDto> Rank(IEnumerable<BenchmarkItemDto> items)
    {
        return items
            .OrderByDescending(item => item.Importance)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
            .ToArray();
    }

    private static string? FirstNonBlank(params string?[] candidates)
    {
        return candidates.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }
}
