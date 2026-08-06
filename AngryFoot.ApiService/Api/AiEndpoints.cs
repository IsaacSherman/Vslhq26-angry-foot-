using AngryFoot.ApiService.Ai;
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

    private static Ok<AiStatusResponse> GetAiStatus(AiConfigurationStatus status, RetrievalConfigurationStatus retrievalStatus)
    {
        return TypedResults.Ok(new AiStatusResponse(
            IsHealthy: status.IsConfigured,
            Status: status.IsConfigured ? "Healthy" : "Unhealthy",
            Message: status.Message,
            RetrievalEnabled: retrievalStatus.IsEnabled,
            RetrievalMessage: retrievalStatus.Message));
    }
}
