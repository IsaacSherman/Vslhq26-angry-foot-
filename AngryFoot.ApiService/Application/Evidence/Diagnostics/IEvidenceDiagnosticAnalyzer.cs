using AngryFoot.ApiService.Domain;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Evidence.Diagnostics;

/// <summary>
/// One reason a resume might be improved, found without asking the user to guess what the tool
/// noticed. Analyzers are independent: each is registered, run, and tested on its own, and one
/// that fails is dropped rather than taking the report with it.
/// </summary>
internal interface IEvidenceDiagnosticAnalyzer
{
    /// <summary>The <see cref="CoverageDiagnosticCodes"/> value this analyzer emits.</summary>
    string Code { get; }

    Task<IReadOnlyList<CoverageDiagnosticDto>> AnalyzeAsync(
        DiagnosticContext context,
        CancellationToken cancellationToken);
}

/// <summary>
/// Keeps any one analyzer from drowning the report. A thin library against a twenty-requirement
/// posting has twenty things wrong with it, and a list that long is read as noise rather than as
/// twenty pieces of advice.
/// </summary>
internal static class DiagnosticBudget
{
    public const int MaxPerCode = 5;

    /// <param name="describeRemainder">Given the number held back, the sentence saying so.</param>
    public static IReadOnlyList<CoverageDiagnosticDto> Cap(
        IReadOnlyList<CoverageDiagnosticDto> ordered,
        Func<int, string> describeRemainder)
    {
        if (ordered.Count <= MaxPerCode)
        {
            return ordered;
        }

        var kept = ordered.Take(MaxPerCode).ToList();
        kept.Add(new CoverageDiagnosticDto(
            DiagnosticSeverityDto.Info,
            ordered[0].Code,
            describeRemainder(ordered.Count - MaxPerCode),
            new EvidenceRationaleDto(
                Requirement: null,
                SupportingEvidence: [],
                MissingEvidence: [],
                Reasoning: "Only the most consequential are listed individually, so the rest of the report stays readable."),
            BulletIds: []));

        return kept;
    }

    /// <summary>
    /// Enough of a bullet to tell it apart from the others in a list of diagnostics. Per-bullet
    /// advice reads as one repeated complaint without it: five identical lines saying a bullet
    /// needs a number are five things to open before learning which bullets they are about.
    /// </summary>
    public static string Excerpt(Bullet bullet, int maxLength = 60)
    {
        var text = bullet.BulletText.Trim();
        if (text.Length <= maxLength)
        {
            return text;
        }

        // Cut on a word boundary when there is one nearby, so the excerpt does not end mid-word.
        var window = text[..maxLength];
        var lastSpace = window.LastIndexOf(' ');
        return (lastSpace > maxLength / 2 ? window[..lastSpace] : window).TrimEnd(',', '.', ';', ' ') + "...";
    }
}
