using AngryFoot.ApiService.Ai;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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

    private static async Task<Ok<AiStatusResponse>> GetAiStatus(HealthCheckService healthCheckService)
    {
        try
        {
            var result = await healthCheckService.CheckHealthAsync(registration => registration.Name == "ai-endpoint");
            var aiStatus = HealthStatus.Unhealthy;
            var description = "AI endpoint not configured";

            if (result.Entries.TryGetValue("ai-endpoint", out var aiEntry))
            {
                aiStatus = aiEntry.Status;
                description = aiEntry.Description ?? description;
            }

            return TypedResults.Ok(new AiStatusResponse(
                IsHealthy: aiStatus == HealthStatus.Healthy,
                Status: aiStatus.ToString(),
                Message: description));
        }
        catch (Exception ex)
        {
            return TypedResults.Ok(new AiStatusResponse(
                IsHealthy: false,
                Status: "Unhealthy",
                Message: ex.Message));
        }
    }
}
