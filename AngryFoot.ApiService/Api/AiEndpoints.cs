using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Application.Retrieval;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AngryFoot.ApiService.Api;

public static class AiEndpoints
{
    public static void MapAiEndpoints(this WebApplication app)
    {
        var aiGroup = app.MapGroup("/api/ai")
            .WithName("AI");

        aiGroup.MapGet("/status", GetAiStatus)
            .WithName("GetAiStatus")
            .WithDescription("Get the current status of the AI endpoint configuration.");
    }

    internal static async Task<Ok<AiStatusResponse>> GetAiStatus(
        AiConfigurationStatus status,
        RetrievalConfigurationStatus retrievalStatus,
        IBulletVectorStore vectorStore,
        CancellationToken cancellationToken)
    {
        var retrievalHealth = await vectorStore.CheckHealthAsync(cancellationToken);
        var retrievalEnabled = retrievalStatus.IsEnabled && retrievalHealth.IsHealthy;
        var retrievalMessage = retrievalStatus.IsEnabled
            ? retrievalHealth.Message
            : retrievalStatus.Message;

        return TypedResults.Ok(new AiStatusResponse(
            IsHealthy: status.IsConfigured,
            Status: status.IsConfigured ? "Healthy" : "Unhealthy",
            Message: status.Message,
            RetrievalEnabled: retrievalEnabled,
            RetrievalMessage: retrievalMessage));
    }
}
