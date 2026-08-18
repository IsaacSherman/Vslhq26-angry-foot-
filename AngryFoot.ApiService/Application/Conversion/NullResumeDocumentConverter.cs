using AngryFoot.Contracts;

namespace AngryFoot.ApiService.Application.Conversion;

/// <summary>
/// Used when no markitdown endpoint is configured. Mirrors <c>NullBulletVectorStore</c>'s role: the
/// app must behave exactly as it did before file import existed unless a developer opts in, so this
/// refuses with an explanation rather than degrading into a worse conversion.
/// </summary>
internal sealed class NullResumeDocumentConverter : IResumeDocumentConverter
{
    public bool IsAvailable => false;

    public Task<string> ConvertAsync(Stream content, string fileName, CancellationToken cancellationToken)
        => throw new ResumeConversionException(
            $"Reading {ImportLimits.DescribeResumeFormats()} files needs the markitdown container, which is not running. "
            + "Paste the resume text instead.");
}
