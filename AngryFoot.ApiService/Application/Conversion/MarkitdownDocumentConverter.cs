using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AngryFoot.ApiService.Application.Conversion;

/// <summary>
/// Converts uploads by calling the markitdown container over MCP, which keeps every PDF and DOCX
/// parsing dependency out of this service's dependency tree.
/// </summary>
internal sealed record MarkitdownOptions(Uri Endpoint);

internal sealed class MarkitdownDocumentConverter(
    MarkitdownOptions options,
    ILoggerFactory loggerFactory,
    ILogger<MarkitdownDocumentConverter> logger) : IResumeDocumentConverter
{
    /// <summary>Generous enough for a long scanned PDF, short enough that a wedged container
    /// surfaces as a message rather than as a request that never returns.</summary>
    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromSeconds(60);

    public bool IsAvailable => true;

    public async Task<string> ConvertAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var dataUri = ResumeDataUri.From(buffer.ToArray(), fileName);

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(ConversionTimeout);

        try
        {
            // A fresh session per upload. The handshake is two localhost round trips and an upload is
            // rare, so keeping a client alive would buy nothing and cost a reconnect path for every
            // time the container restarts underneath us.
            var transport = new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = options.Endpoint,
                    Name = "markitdown",
                    // markitdown-mcp only speaks Streamable HTTP, and AutoDetect's fallback probe is
                    // an extra connection this server does not survive well.
                    TransportMode = HttpTransportMode.StreamableHttp,
                    // The standalone GET stream exists to receive unsolicited server messages, which
                    // a single convert call has no use for. Left on, closing it takes markitdown's
                    // session manager down with it ("Task group is not initialized" on every request
                    // afterwards), so one upload would poison the container for the next.
                    EnableStandaloneGetStream = false
                },
                loggerFactory);

            await using var client = await McpClient.CreateAsync(
                transport,
                new McpClientOptions
                {
                    // markitdown-mcp is built on mcp-python 1.8.1, which implements 2024-11-05 and
                    // nothing later. Left at the default the client prefers 2026-07-28 and opens
                    // with a server/discover probe that this server answers by tearing down its
                    // session manager, after which every request 500s until the container restarts.
                    ProtocolVersion = "2024-11-05",
                    ClientInfo = new Implementation { Name = "angryfoot", Version = "1.0" }
                },
                loggerFactory,
                attempt.Token);

            var result = await client.CallToolAsync(
                "convert_to_markdown",
                new Dictionary<string, object?> { ["uri"] = dataUri },
                cancellationToken: attempt.Token);

            var markdown = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

            if (result.IsError == true || string.IsNullOrWhiteSpace(markdown))
            {
                logger.LogWarning(
                    "markitdown returned no text for {FileName} (isError: {IsError}).", fileName, result.IsError);
                throw new ResumeConversionException(
                    $"No text could be read from {fileName}. A resume saved as scanned images has no text to extract; "
                    + "paste the text instead.");
            }

            return markdown;
        }
        catch (ResumeConversionException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("markitdown conversion of {FileName} timed out.", fileName);
            throw new ResumeConversionException(
                $"Converting {fileName} took longer than {ConversionTimeout.TotalSeconds:0} seconds. Try a smaller file, "
                + "or paste the text instead.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "markitdown conversion of {FileName} failed.", fileName);
            throw new ResumeConversionException(
                $"The converter could not read {fileName}. Check that the markitdown container is running, "
                + "or paste the text instead.");
        }
    }
}
