using AngryFoot.Contracts;

namespace AngryFoot.Web.Models;

/// <summary>
/// The one place a coverage number or an evidence state becomes a colour.
/// </summary>
/// <remarks>
/// Aggregates are deliberately neutral. A single number rendered red, amber, or green reads as a
/// grade on the person holding it, which is the thing issue #18 exists to stop; colour is spent
/// instead on the per-requirement rows, where it states a checkable fact about one requirement and
/// points at something the user can act on.
/// </remarks>
public static class CoverageBands
{
    public const string AggregateBadge = "text-bg-secondary";
    public const string AggregateBar = "bg-secondary";

    public static string StrengthBadge(EvidenceStrengthDto strength) => strength switch
    {
        EvidenceStrengthDto.Strong => "text-bg-success",
        EvidenceStrengthDto.Weak => "text-bg-warning",
        _ => "text-bg-danger"
    };

    public static string StrengthLabel(EvidenceStrengthDto strength) => strength switch
    {
        EvidenceStrengthDto.Strong => "Evidenced",
        EvidenceStrengthDto.Weak => "Mentioned only",
        _ => "No evidence"
    };

    /// <summary>A shape as well as a colour, so the states survive being read without colour.</summary>
    public static string StrengthIcon(EvidenceStrengthDto strength) => strength switch
    {
        EvidenceStrengthDto.Strong => "✓",
        EvidenceStrengthDto.Weak => "–",
        _ => "✗"
    };

    /// <summary>
    /// Editor severities rather than alarm levels: amber for something worth fixing, blue for a
    /// suggestion, grey for a note about the analysis itself. Red is reserved for the evidence
    /// states above, where it means "this requirement has no bullet" and nothing more.
    /// </summary>
    public static string SeverityBadge(DiagnosticSeverityDto severity) => severity switch
    {
        DiagnosticSeverityDto.Warning => "text-bg-warning",
        DiagnosticSeverityDto.Suggestion => "text-bg-info",
        _ => "text-bg-secondary"
    };

    public static string SeverityLabel(DiagnosticSeverityDto severity) => severity switch
    {
        DiagnosticSeverityDto.Warning => "Warning",
        DiagnosticSeverityDto.Suggestion => "Suggestion",
        _ => "Info"
    };

    public static string KindLabel(RequirementKindDto kind) => kind switch
    {
        RequirementKindDto.Required => "Required",
        RequirementKindDto.Preferred => "Preferred",
        _ => "Technology"
    };
}
