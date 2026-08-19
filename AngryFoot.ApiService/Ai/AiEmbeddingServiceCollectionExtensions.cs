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
    /// Wires up embedding and, on top of it, Qdrant-backed semantic bullet retrieval.
    /// <para>
    /// The two are gated separately on purpose. <see cref="ITextEmbedder"/> needs only an embedding
    /// deployment, because computing a vector does not require somewhere to store it - which is what
    /// lets evidence coverage match by meaning on a machine with no Docker, and over bullets that
    /// were never saved. <see cref="IBulletVectorStore"/> additionally needs Qdrant, since it
    /// genuinely does store and search vectors.
    /// </para>
    /// <para>
    /// Both fall back to a null object rather than going unregistered, so no service can test for
    /// embeddings by null-checking its dependency, and neither feature is ever a hard requirement to
    /// run the app.
    /// </para>
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

        if (embeddingsConfigured)
        {
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(_ =>
            {
                var client = new AzureOpenAIClient(new Uri(endpoint!), new ApiKeyCredential(apiKey!));
                return client.GetEmbeddingClient(embeddingDeployment!).AsIEmbeddingGenerator(embeddingDimensions);
            });

            services.AddSingleton<ITextEmbedder, AzureOpenAiTextEmbedder>();
        }
        else
        {
            services.AddSingleton<ITextEmbedder, NullTextEmbedder>();
        }

        if (embeddingsConfigured && qdrantConfigured)
        {
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
        var stillMatches = embeddingsConfigured
            ? " Evidence coverage still matches requirements by meaning, which needs the embedding deployment only."
            : string.Empty;
        services.AddSingleton(new RetrievalConfigurationStatus(
            false, $"Semantic retrieval disabled ({reason}); generation falls back to keyword ranking over all bullets.{stillMatches}"));

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
