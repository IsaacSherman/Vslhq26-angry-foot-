using AngryFoot.ApiService.Application.Conversion;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace AngryFoot.Tests.Unit;

public class DocumentConversionServiceCollectionExtensionsTests
{
    private static (Type? Converter, ConversionConfigurationStatus Status) Build(string? endpoint)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Markitdown:Endpoint"] = endpoint });

        builder.AddAngryFootDocumentConversion();

        var status = (ConversionConfigurationStatus)builder.Services
            .Single(descriptor => descriptor.ServiceType == typeof(ConversionConfigurationStatus))
            .ImplementationInstance!;

        return (builder.Services.Single(descriptor => descriptor.ServiceType == typeof(IResumeDocumentConverter)).ImplementationType, status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    public void WithNoUsableEndpoint_RegistersTheNullConverter(string? endpoint)
    {
        var (converter, status) = Build(endpoint);

        converter.Should().Be(typeof(NullResumeDocumentConverter));
        status.IsEnabled.Should().BeFalse();
        status.Message.Should().Contain("pasting", "the message has to name the path that still works");
    }

    [Fact]
    public void WithAnEndpoint_RegistersTheMarkitdownConverter()
    {
        var (converter, status) = Build("http://localhost:3001");

        converter.Should().Be(typeof(MarkitdownDocumentConverter));
        status.IsEnabled.Should().BeTrue();
        status.Message.Should().Contain("localhost:3001");
    }

    [Fact]
    public async Task TheNullConverter_RefusesWithAMessageNamingTheFormatsAndTheAlternative()
    {
        var converter = new NullResumeDocumentConverter();

        var convert = async () => await converter.ConvertAsync(
            new MemoryStream([1, 2, 3]), "resume.pdf", TestContext.Current.CancellationToken);

        (await convert.Should().ThrowAsync<ResumeConversionException>())
            .Which.Message.Should().Contain(".pdf").And.Contain("Paste");
    }
}
