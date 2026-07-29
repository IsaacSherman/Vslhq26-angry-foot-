using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using System.ClientModel;

namespace AngryFoot.ApiService.Ai;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAngryFootAi(this IServiceCollection services, IConfiguration configuration)
    {
        const string defaultDeployment = "gpt-5-mini";

        var endpoint = configuration["AzureOpenAI:Endpoint"];
        var apiKey = configuration["AzureOpenAI:ApiKey"]
            ?? configuration["AzureOpenAI:Key"]
            ?? configuration["OpenAI:ApiKey"];
        var deployment = configuration["AzureOpenAI:ChatDeployment"]
            ?? configuration["AzureOpenAI:Deployment"]
            ?? configuration["AzureOpenAI:Model"]  // Support model name as alias
            ?? defaultDeployment;
        var serviceVersion = configuration["AzureOpenAI:ServiceVersion"];
        NormalizeEndpointAndDeployment(ref endpoint, ref deployment);

        if (!string.IsNullOrWhiteSpace(endpoint)
            && !string.IsNullOrWhiteSpace(apiKey)
            && !string.IsNullOrWhiteSpace(deployment))
        {
            services.AddSingleton<IChatClient>(_ =>
            {
                var options = CreateClientOptions(serviceVersion);

                var client = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey), options);
                return client.GetChatClient(deployment).AsIChatClient();
            });

            // Register health check for AI endpoint
            //services.AddHealthChecks()
            //    .AddCheck<AiHealthCheck>("ai-endpoint");

            return services;
        }

        services.AddSingleton<IChatClient>(_ => new StaticResponseChatClient(
            "AI is not configured. Set AzureOpenAI:Endpoint and AzureOpenAI:ApiKey (or AzureOpenAI:Key), and set AzureOpenAI:ChatDeployment (or AzureOpenAI:Deployment / AzureOpenAI:Model). Optionally set AzureOpenAI:ServiceVersion."));

        return services;
    }

    private static AzureOpenAIClientOptions CreateClientOptions(string? serviceVersion)
    {
        if (!string.IsNullOrWhiteSpace(serviceVersion)
            && Enum.TryParse<AzureOpenAIClientOptions.ServiceVersion>(serviceVersion, ignoreCase: true, out var parsedVersion))
        {
            return new AzureOpenAIClientOptions(parsedVersion);
        }

        return new AzureOpenAIClientOptions();
    }

    private static void NormalizeEndpointAndDeployment(ref string? endpoint, ref string? deployment)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 3
            && segments[0].Equals("openai", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("deployments", StringComparison.OrdinalIgnoreCase))
        {
            var deploymentFromEndpoint = segments[2];
            if (string.IsNullOrWhiteSpace(deployment))
            {
                deployment = deploymentFromEndpoint;
            }
        }

        // Azure.AI.OpenAI expects the resource base endpoint (scheme + host).
        // If a path is provided (for example /openai/v1 or /openai/deployments/<name>),
        // the SDK will append its own route and can otherwise produce invalid URLs.
        var baseUri = new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri;
        endpoint = baseUri.ToString().TrimEnd('/');
    }
}
