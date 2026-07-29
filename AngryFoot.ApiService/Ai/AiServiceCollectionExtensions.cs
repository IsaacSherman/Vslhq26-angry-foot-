using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using System.ClientModel;

namespace AngryFoot.ApiService.Ai;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAngryFootAi(this IServiceCollection services, IConfiguration configuration)
    {
        var endpoint = configuration["AzureOpenAI:Endpoint"];
        var apiKey = configuration["AzureOpenAI:ApiKey"];
        var deployment = configuration["AzureOpenAI:ChatDeployment"];

        if (!string.IsNullOrWhiteSpace(endpoint)
            && !string.IsNullOrWhiteSpace(apiKey)
            && !string.IsNullOrWhiteSpace(deployment))
        {
            services.AddSingleton<IChatClient>(_ =>
            {
                var client = new AzureOpenAIClient(new Uri(endpoint), new ApiKeyCredential(apiKey));
                return client.GetChatClient(deployment).AsIChatClient();
            });

            return services;
        }

        services.AddSingleton<IChatClient>(_ => new StaticResponseChatClient(
            "AI is not configured. Set AzureOpenAI:Endpoint, AzureOpenAI:ApiKey, and AzureOpenAI:ChatDeployment in user secrets."));

        return services;
    }
}
