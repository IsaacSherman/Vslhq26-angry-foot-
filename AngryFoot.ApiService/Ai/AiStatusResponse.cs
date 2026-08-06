namespace AngryFoot.ApiService.Ai;

public sealed record AiStatusResponse(
    bool IsHealthy,
    string Status,
    string? Message = null,
    bool RetrievalEnabled = false,
    string? RetrievalMessage = null);

public sealed record AiConfigurationStatus(bool IsConfigured, string Message);
