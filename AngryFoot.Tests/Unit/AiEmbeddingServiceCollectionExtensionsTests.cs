using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Application.Retrieval;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;

namespace AngryFoot.Tests.Unit;

public class AiEmbeddingServiceCollectionExtensionsTests
{
    private static (IServiceCollection Services, RetrievalConfigurationStatus Status) Build(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);

        builder.AddAngryFootRetrieval();

        var statusDescriptor = builder.Services.Single(d => d.ServiceType == typeof(RetrievalConfigurationStatus));
        var status = (RetrievalConfigurationStatus)statusDescriptor.ImplementationInstance!;

        return (builder.Services, status);
    }

    private static Type? VectorStoreImplementationType(IServiceCollection services)
        => services.Single(d => d.ServiceType == typeof(IBulletVectorStore)).ImplementationType;

    private static Type? EmbedderImplementationType(IServiceCollection services)
        => services.Single(d => d.ServiceType == typeof(ITextEmbedder)).ImplementationType;

    [Fact]
    public void AddAngryFootRetrieval_WithNoConfiguration_RegistersNullStoreAndDisabledStatus()
    {
        var (services, status) = Build(new Dictionary<string, string?>());

        VectorStoreImplementationType(services).Should().Be(typeof(NullBulletVectorStore));
        EmbedderImplementationType(services).Should().Be(typeof(NullTextEmbedder));
        status.IsEnabled.Should().BeFalse();
        status.Message.Should().Contain("EmbeddingDeployment");
    }

    [Fact]
    public void AddAngryFootRetrieval_WithEmbeddingsButNoQdrant_FallsBackToNullStore()
    {
        var (services, status) = Build(new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = "https://example.openai.azure.com",
            ["AzureOpenAI:ApiKey"] = "test-key",
            ["AzureOpenAI:EmbeddingDeployment"] = "text-embedding-3-small"
        });

        VectorStoreImplementationType(services).Should().Be(typeof(NullBulletVectorStore));
        status.IsEnabled.Should().BeFalse();
        status.Message.Should().Contain("qdrant");
    }

    [Fact]
    public void AddAngryFootRetrieval_WithEmbeddingsButNoQdrant_StillRegistersTheEmbedder()
    {
        var (services, status) = Build(new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = "https://example.openai.azure.com",
            ["AzureOpenAI:ApiKey"] = "test-key",
            ["AzureOpenAI:EmbeddingDeployment"] = "text-embedding-3-small"
        });

        // Computing a vector needs no vector database, so matching requirements by meaning must not
        // require Docker to be running.
        EmbedderImplementationType(services).Should().Be(typeof(AzureOpenAiTextEmbedder));
        status.Message.Should().Contain("matches requirements by meaning");
    }

    [Fact]
    public void AddAngryFootRetrieval_WithQdrantButNoEmbeddings_RegistersTheNullEmbedder()
    {
        var (services, _) = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:qdrant"] = "http://localhost:6334"
        });

        EmbedderImplementationType(services).Should().Be(typeof(NullTextEmbedder));
    }

    [Fact]
    public void AddAngryFootRetrieval_WithQdrantButNoEmbeddings_FallsBackToNullStore()
    {
        var (services, status) = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:qdrant"] = "http://localhost:6334"
        });

        VectorStoreImplementationType(services).Should().Be(typeof(NullBulletVectorStore));
        status.IsEnabled.Should().BeFalse();
        status.Message.Should().Contain("EmbeddingDeployment");
    }

    [Fact]
    public void AddAngryFootRetrieval_WithEmbeddingsAndQdrant_RegistersQdrantStoreAndEnabledStatus()
    {
        var (services, status) = Build(new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = "https://example.openai.azure.com",
            ["AzureOpenAI:ApiKey"] = "test-key",
            ["AzureOpenAI:EmbeddingDeployment"] = "text-embedding-3-small",
            ["ConnectionStrings:qdrant"] = "http://localhost:6334"
        });

        VectorStoreImplementationType(services).Should().Be(typeof(QdrantBulletVectorStore));
        EmbedderImplementationType(services).Should().Be(typeof(AzureOpenAiTextEmbedder));
        status.IsEnabled.Should().BeTrue();
        status.Message.Should().Contain("text-embedding-3-small");
    }
}
