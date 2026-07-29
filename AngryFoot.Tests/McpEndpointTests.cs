using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace AngryFoot.Tests;

[Collection(IntegrationTestCollection.Name)]
public class McpEndpointTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task McpEndpoint_ExposesBulletTools_AndAddUpdateRoundTripWorks()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AngryFoot_AppHost>(cancellationToken);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter("Aspire.", LogLevel.Warning);
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder => clientBuilder.AddStandardResilienceHandler(TestResilience.ConfigureStandardHandler));
        TestDatabase.UseIsolatedDatabase(appHost);

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        var httpClient = app.CreateHttpClient("apiservice");
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(httpClient.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp
        }, httpClient);

        await using var mcpClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

        // The tool surface mirrors the web app's bullet features.
        var tools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
        var toolNames = tools.Select(t => t.Name).ToArray();
        Assert.Contains("add_bullet", toolNames);
        Assert.Contains("update_bullet", toolNames);
        Assert.Contains("rewrite_bullet", toolNames);
        Assert.Contains("enrich_bullet", toolNames);
        Assert.Contains("get_bullet", toolNames);
        Assert.Contains("list_bullets", toolNames);
        Assert.Contains("delete_bullet", toolNames);

        // Add a bullet with an employer, exactly like the web editor does.
        var addResult = await mcpClient.CallToolAsync(
            "add_bullet",
            new Dictionary<string, object?>
            {
                ["bulletText"] = "Cut MCP integration time by 50% by shipping typed tool endpoints.",
                ["sourceEmployer"] = "Acme Corp"
            },
            cancellationToken: cancellationToken);

        Assert.NotEqual(true, addResult.IsError);
        var added = ParseBullet(addResult);
        Assert.Equal("Acme Corp", added.GetProperty("sourceEmployer").GetString());
        var bulletId = added.GetProperty("id").GetGuid();

        // Update it through MCP and confirm the change is visible to the REST API too.
        var updateResult = await mcpClient.CallToolAsync(
            "update_bullet",
            new Dictionary<string, object?>
            {
                ["id"] = bulletId,
                ["bulletText"] = "Cut MCP integration time by 60% by shipping typed tool endpoints.",
                ["sourceEmployer"] = "Initech"
            },
            cancellationToken: cancellationToken);

        Assert.NotEqual(true, updateResult.IsError);
        var updated = ParseBullet(updateResult);
        Assert.Contains("60%", updated.GetProperty("bulletText").GetString());
        Assert.Equal("Initech", updated.GetProperty("sourceEmployer").GetString());

        var restResponse = await httpClient.GetAsync($"/api/bullets/{bulletId}", cancellationToken);
        restResponse.EnsureSuccessStatusCode();
        var restJson = JsonDocument.Parse(await restResponse.Content.ReadAsStringAsync(cancellationToken)).RootElement;
        Assert.Contains("60%", restJson.GetProperty("bulletText").GetString());

        // Unknown ids surface as tool errors rather than silent successes.
        var missingResult = await mcpClient.CallToolAsync(
            "update_bullet",
            new Dictionary<string, object?>
            {
                ["id"] = Guid.NewGuid(),
                ["bulletText"] = "irrelevant"
            },
            cancellationToken: cancellationToken);

        Assert.Equal(true, missingResult.IsError);
    }

    private static JsonElement ParseBullet(ModelContextProtocol.Protocol.CallToolResult result)
    {
        if (result.StructuredContent is not null)
        {
            return JsonSerializer.SerializeToElement(result.StructuredContent);
        }

        var text = result.Content
            .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
            .Select(x => x.Text)
            .First();
        return JsonDocument.Parse(text).RootElement;
    }
}
