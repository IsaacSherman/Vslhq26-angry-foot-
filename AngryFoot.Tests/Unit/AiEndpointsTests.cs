using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Api;
using AngryFoot.ApiService.Application.Retrieval;
using AngryFoot.Tests.Fakes;
using AwesomeAssertions;

namespace AngryFoot.Tests.Unit;

public class AiEndpointsTests
{
    [Fact]
    public async Task GetAiStatus_WhenRetrievalIsConfiguredButUnhealthy_ReportsRetrievalDisabled()
    {
        var vectorStore = new FakeBulletVectorStore
        {
            Health = new RetrievalHealth(false, "Qdrant is unavailable.")
        };

        var result = await AiEndpoints.GetAiStatus(
            new AiConfigurationStatus(true, "AI configured."),
            new RetrievalConfigurationStatus(true, "Retrieval configured."),
            vectorStore,
            CancellationToken.None);

        result.Value!.RetrievalEnabled.Should().BeFalse();
        result.Value.RetrievalMessage.Should().Be("Qdrant is unavailable.");
    }

    [Fact]
    public async Task GetAiStatus_WhenRetrievalIsConfiguredAndHealthy_ReportsRetrievalEnabled()
    {
        var vectorStore = new FakeBulletVectorStore
        {
            Health = new RetrievalHealth(true, "Semantic retrieval is ready.")
        };

        var result = await AiEndpoints.GetAiStatus(
            new AiConfigurationStatus(true, "AI configured."),
            new RetrievalConfigurationStatus(true, "Retrieval configured."),
            vectorStore,
            CancellationToken.None);

        result.Value!.RetrievalEnabled.Should().BeTrue();
        result.Value.RetrievalMessage.Should().Be("Semantic retrieval is ready.");
    }
}
