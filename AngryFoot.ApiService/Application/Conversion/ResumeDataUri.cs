namespace AngryFoot.ApiService.Application.Conversion;

/// <summary>
/// Builds the <c>data:</c> URI handed to markitdown's <c>convert_to_markdown</c> tool.
/// </summary>
/// <remarks>
/// A data URI carries no filename, so the media type is the only thing telling MarkItDown which
/// converter to run. A wrong or missing one produces empty Markdown rather than an error, so the
/// table is explicit and an extension missing from it is refused here instead of being sent and
/// silently returning nothing.
/// </remarks>
internal static class ResumeDataUri
{
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    public static string From(byte[] content, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (!MediaTypes.TryGetValue(extension, out var mediaType))
        {
            throw new ResumeConversionException($"{fileName} is not a format the converter can read.");
        }

        return $"data:{mediaType};base64,{Convert.ToBase64String(content)}";
    }

    public static bool CanConvert(string fileName) => MediaTypes.ContainsKey(Path.GetExtension(fileName));
}
