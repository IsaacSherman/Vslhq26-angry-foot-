namespace AngryFoot.Contracts;

/// <summary>
/// Upload ceilings and accepted formats, shared by the endpoints that refuse a file and the pages
/// that stop it before it is streamed, so the two can never disagree about what gets rejected.
/// </summary>
public static class ImportLimits
{
    public const long MaxLinkedInFileSizeBytes = 25 * 1024 * 1024;

    /// <summary>
    /// Lower than the LinkedIn ceiling: a resume is base64-encoded into a JSON-RPC string on its way
    /// to the converter, which inflates it by a third, and a real resume is well under a megabyte.
    /// </summary>
    public const long MaxResumeFileSizeBytes = 10 * 1024 * 1024;

    public static IReadOnlyList<string> ResumeFileExtensions { get; } = [".pdf", ".docx"];

    /// <summary>The <c>accept</c> attribute for a resume file picker, derived so it cannot drift.</summary>
    public static string ResumeFileAccept { get; } = string.Join(',', ResumeFileExtensions);

    public static string DescribeResumeFormats() => string.Join(" or ", ResumeFileExtensions);
}
