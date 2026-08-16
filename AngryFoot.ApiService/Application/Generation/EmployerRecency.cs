using AngryFoot.ApiService.Domain;

namespace AngryFoot.ApiService.Application.Generation;

/// <summary>
/// How recent each employer in the work history is, on a 0-1 scale where 1 is the current or most
/// recent role.
/// <para>
/// Derived from <see cref="WorkHistory.SortOrder"/> rather than from the date fields: those are
/// free text ("2019", "Jan 2019", "Spring 2019"), and a resume's work history is already ordered
/// most-recent-first by the position the user put it in. Reading the order they arranged beats
/// parsing the strings they typed.
/// </para>
/// </summary>
internal sealed class EmployerRecency
{
    /// <summary>
    /// What a bullet scores when its employer is not in the work history, or it names none at all.
    /// The midpoint, deliberately: an unplaceable bullet should not be buried under everything
    /// datable, and should not leapfrog the current role either.
    /// </summary>
    private const double UnknownRecency = 0.5;

    private readonly IReadOnlyDictionary<string, (double Score, int Position, int Total)> byEmployer;

    private EmployerRecency(IReadOnlyDictionary<string, (double, int, int)> byEmployer)
        => this.byEmployer = byEmployer;

    /// <summary>A history with nothing in it: every bullet is equally recent, so recency decides nothing.</summary>
    public static EmployerRecency None { get; } = new(new Dictionary<string, (double, int, int)>(StringComparer.OrdinalIgnoreCase));

    public static EmployerRecency From(IEnumerable<WorkHistory> history)
    {
        var ordered = history
            .Where(x => !string.IsNullOrWhiteSpace(x.Employer))
            .OrderBy(x => x.SortOrder)
            .Select(x => x.Employer.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ordered.Length == 0)
        {
            return None;
        }

        // One employer is trivially the most recent one; the divisor would otherwise be zero.
        var span = Math.Max(1, ordered.Length - 1);
        var map = new Dictionary<string, (double, int, int)>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < ordered.Length; i++)
        {
            map[ordered[i]] = (1.0 - (i / (double)span), i, ordered.Length);
        }

        return new EmployerRecency(map);
    }

    public double For(string? employer)
    {
        return employer is not null && byEmployer.TryGetValue(employer.Trim(), out var entry)
            ? entry.Score
            : UnknownRecency;
    }

    /// <summary>
    /// Why recency helped or hurt, or null when it did neither - either the employer is unknown, or
    /// it sits in the middle of the history, where there is nothing worth saying about it.
    /// </summary>
    public string? Describe(string? employer)
    {
        if (employer is null || !byEmployer.TryGetValue(employer.Trim(), out var entry))
        {
            return null;
        }

        if (entry.Total == 1)
        {
            return null;
        }

        return entry.Position switch
        {
            0 => $"From your current or most recent role at {employer.Trim()}.",
            var last when last == entry.Total - 1 => $"From {employer.Trim()}, the earliest role in your history.",
            _ => null
        };
    }
}
