namespace AngryFoot.ApiService.Ai;

public sealed record AiStatusResponse(
    bool IsHealthy,
    string Status,
    string? Message = null);
