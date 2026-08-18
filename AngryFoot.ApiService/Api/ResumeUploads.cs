using AngryFoot.ApiService.Application.Conversion;
using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Api;

/// <summary>
/// The guards every resume upload passes before conversion. Shared by the endpoints that accept one
/// so a file refused by the import is refused identically by the review.
/// </summary>
internal static class ResumeUploads
{
    public static async Task<string> ReadMarkdownAsync(
        IFormFile file,
        IResumeDocumentConverter converter,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            throw new ResumeConversionException("No file was uploaded.");
        }

        if (file.Length > ImportLimits.MaxResumeFileSizeBytes)
        {
            throw new ResumeConversionException(
                $"The uploaded file exceeds the {ImportLimits.MaxResumeFileSizeBytes / (1024 * 1024)} MB size limit.");
        }

        if (!ResumeDataUri.CanConvert(file.FileName))
        {
            throw new ResumeConversionException($"Expected a {ImportLimits.DescribeResumeFormats()} file.");
        }

        await using var stream = file.OpenReadStream();
        return await converter.ConvertAsync(stream, file.FileName, cancellationToken);
    }
}
