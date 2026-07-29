using AngryFoot.ApiService.Ai;
using AwesomeAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

namespace AngryFoot.Tests.Unit;

public class AiServiceCollectionExtensionsTests
{
    private static (IChatClient Client, AiConfigurationStatus Status) Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddAngryFootAi(configuration);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IChatClient>(), provider.GetRequiredService<AiConfigurationStatus>());
    }

    [Fact]
    public void AddAngryFootAi_WithNoConfiguration_RegistersStaticClientAndUnconfiguredStatus()
    {
        var (client, status) = Build(new Dictionary<string, string?>());

        client.Should().BeOfType<StaticResponseChatClient>();
        status.IsConfigured.Should().BeFalse();
        status.Message.Should().Contain("AzureOpenAI:Endpoint", "the message must tell the user how to configure AI");
    }

    [Fact]
    public void AddAngryFootAi_WithEndpointButNoKey_FallsBackToStaticClient()
    {
        var (client, status) = Build(new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = "https://example.openai.azure.com"
        });

        client.Should().BeOfType<StaticResponseChatClient>();
        status.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void AddAngryFootAi_WithEndpointAndKey_RegistersRealClientAndConfiguredStatus()
    {
        var (client, status) = Build(new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = "https://example.openai.azure.com",
            ["AzureOpenAI:Key"] = "test-key",
            ["AzureOpenAI:ChatDeployment"] = "my-deployment"
        });

        client.Should().NotBeOfType<StaticResponseChatClient>();
        status.IsConfigured.Should().BeTrue();
        status.Message.Should().Contain("my-deployment");
    }

    [Fact]
    public void AddAngryFootAi_ExtractsDeploymentFromEndpointPath_WhenNoneConfigured()
    {
        var (_, status) = Build(new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = "https://example.openai.azure.com/openai/deployments/path-deployment/chat/completions",
            ["AzureOpenAI:ApiKey"] = "test-key"
        });

        status.IsConfigured.Should().BeTrue();
        status.Message.Should().Contain("path-deployment", "the deployment embedded in the endpoint URL is honored");
    }

    [Fact]
    public void AddAngryFootAi_WithoutDeployment_UsesDefaultDeployment()
    {
        var (_, status) = Build(new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = "https://example.openai.azure.com",
            ["AzureOpenAI:ApiKey"] = "test-key"
        });

        status.IsConfigured.Should().BeTrue();
        status.Message.Should().Contain("gpt-5-mini", "gpt-5-mini is the documented default deployment");
    }
}
