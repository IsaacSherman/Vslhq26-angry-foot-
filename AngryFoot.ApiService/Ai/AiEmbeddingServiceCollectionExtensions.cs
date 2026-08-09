using System.ClientModel;
using AngryFoot.ApiService.Application.Retrieval;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;

namespace AngryFoot.ApiService.Ai;

public sealed record RetrievalConfigurationStatus(bool IsEnabled, string Message);

public static class AiEmbeddingServiceCollectionExtensions
{
    private const int DefaultEmbeddingDimensions = 1536;

    /// <summary>
    /// Wires up Qdrant-backed semantic bullet retrieval when both an embedding deployment and a
    /// Qdrant connection are configured. Either being absent (the default) registers
    /// <see cref="NullBulletVectorStore"/> so generation falls back to the existing deterministic
    /// keyword ranking - this feature must never become a hard requirement to run the app.
    /// </summary>
    public static WebApplicationBuilder AddAngryFootRetrieval(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var services = builder.Services;

        var endpoint = NormalizeEndpoint(configuration["AzureOpenAI:Endpoint"]);
        var apiKey = configuration["AzureOpenAI:ApiKey"]
            ?? configuration["AzureOpenAI:Key"]
            ?? configuration["OpenAI:ApiKey"];
        var embeddingDeployment = configuration["AzureOpenAI:EmbeddingDeployment"];
        var embeddingDimensions = int.TryParse(configuration["AzureOpenAI:EmbeddingDimensions"], out var configuredDimensions)
            ? configuredDimensions
            : DefaultEmbeddingDimensions;
        var qdrantConnectionString = configuration.GetConnectionString("qdrant");

        var embeddingsConfigured = !string.IsNullOrWhiteSpace(endpoint)
            && !string.IsNullOrWhiteSpace(apiKey)
            && !string.IsNullOrWhiteSpace(embeddingDeployment);
        var qdrantConfigured = !string.IsNullOrWhiteSpace(qdrantConnectionString);

        if (embeddingsConfigured && qdrantConfigured)
        {
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
            {
                var client = new AzureOpenAIClient(new Uri(endpoint!), new ApiKeyCredential(apiKey!));
                return client.GetEmbeddingClient(embeddingDeployment!).AsIEmbeddingGenerator(embeddingDimensions);
            });

            builder.AddQdrantClient("qdrant");
            services.AddSingleton(new RetrievalOptions(embeddingDimensions));
            services.AddSingleton<IBulletVectorStore, QdrantBulletVectorStore>();
            services.AddSingleton(new RetrievalConfigurationStatus(
                true, $"Semantic retrieval enabled via Qdrant with embedding deployment '{embeddingDeployment}'."));

            return builder;
        }

        services.AddSingleton<IBulletVectorStore, NullBulletVectorStore>();

        var reason = !embeddingsConfigured
            ? "AzureOpenAI:EmbeddingDeployment (plus AzureOpenAI:Endpoint/ApiKey) is not configured"
            : "no 'qdrant' connection is configured";
        services.AddSingleton(new RetrievalConfigurationStatus(
            false, $"Semantic retrieval disabled ({reason}); generation falls back to keyword ranking over all bullets."));

        return builder;
    }

    private static string? NormalizeEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return endpoint;
        }

        return new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri.ToString().TrimEnd('/');
    }
}
