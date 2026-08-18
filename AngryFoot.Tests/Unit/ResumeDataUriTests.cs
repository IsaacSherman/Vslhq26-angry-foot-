using System.Text;
using AngryFoot.ApiService.Application.Conversion;
using AngryFoot.Contracts;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

/// <summary>
/// The media type is the only thing telling markitdown which converter to run, so these tests are
/// mostly about the table being right and being refused rather than guessed at when it is not.
/// </summary>
public class ResumeDataUriTests
{
    private static readonly byte[] Content = Encoding.UTF8.GetBytes("resume bytes");

    [Theory]
    [InlineData("resume.pdf", "application/pdf")]
    [InlineData("resume.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("RESUME.DOCX", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void From_DeclaresTheMediaTypeForTheExtension(string fileName, string expected)
    {
        ResumeDataUri.From(Content, fileName).Should().StartWith($"data:{expected};base64,");
    }

    [Fact]
    public void From_CarriesTheFileVerbatim()
    {
        var uri = ResumeDataUri.From(Content, "resume.pdf");
        var payload = uri[(uri.IndexOf(',') + 1)..];

        Convert.FromBase64String(payload).Should().Equal(Content);
    }

    [Theory]
    [InlineData("resume.txt")]
    [InlineData("resume.doc")]
    [InlineData("resume")]
    public void From_RefusesAnExtensionItHasNoMediaTypeFor(string fileName)
    {
        var convert = () => ResumeDataUri.From(Content, fileName);

        convert.Should().Throw<ResumeConversionException>(
            "sending an unknown type returns empty markdown rather than an error, so it has to be caught here")
            .WithMessage($"*{fileName}*");
    }

    [Fact]
    public void EveryOfferedExtensionCanBeConverted()
    {
        // The Web offers this list in its file picker and the endpoint refuses anything outside it;
        // an entry with no media type here would be accepted by both and then convert to nothing.
        ImportLimits.ResumeFileExtensions.Should().OnlyContain(
            extension => ResumeDataUri.CanConvert($"resume{extension}"));
    }
}
