namespace AngryFoot.ApiService.Application.Conversion;

/// <summary>Whether uploaded resumes can be converted, and why not when they cannot.</summary>
public sealed record ConversionConfigurationStatus(bool IsEnabled, string Message);

public static class DocumentConversionServiceCollectionExtensions
{
    public static WebApplicationBuilder AddAngryFootDocumentConversion(this WebApplicationBuilder builder)
    {
        var endpoint = builder.Configuration["Markitdown:Endpoint"];

        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var baseAddress))
        {
            // The MCP server answers on /mcp/ and 307s the slashless form. The redirect is harmless
            // but costs a round trip on every upload, so the slash is spelled out here.
            var mcpEndpoint = new Uri(baseAddress, "/mcp/");

            builder.Services.AddSingleton(new MarkitdownOptions(mcpEndpoint));
            builder.Services.AddSingleton<IResumeDocumentConverter, MarkitdownDocumentConverter>();

            builder.Services.AddSingleton(new ConversionConfigurationStatus(
                true, $"Resume file import enabled via markitdown at {baseAddress}."));

            return builder;
        }

        builder.Services.AddSingleton<IResumeDocumentConverter, NullResumeDocumentConverter>();
        builder.Services.AddSingleton(new ConversionConfigurationStatus(
            false,
            "Resume file import disabled (no markitdown endpoint is configured); resumes can still be imported by pasting their text."));

        return builder;
    }
}
