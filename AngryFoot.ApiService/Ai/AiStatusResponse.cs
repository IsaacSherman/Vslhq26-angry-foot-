namespace AngryFoot.ApiService.Ai;

/// <summary>Whether a chat deployment is configured, and why not when it is not. Surfaced by
/// <c>/api/ai/status</c>; never a branch, because every feature falls back on its own.</summary>
public sealed record AiConfigurationStatus(bool IsConfigured, string Message);
