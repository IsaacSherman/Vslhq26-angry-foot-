namespace AngryFoot.Contracts;

/// <summary>
/// What the API service has configured, for pages that need to say so before a user tries something
/// that is not available. Optional capabilities each report a flag and a sentence explaining the
/// flag, so a page never has to compose its own reason for why a control is disabled.
/// </summary>
public sealed record AiStatusResponse(
    bool IsHealthy,
    string Status,
    string? Message = null,
    bool RetrievalEnabled = false,
    string? RetrievalMessage = null,
    bool FileImportEnabled = false,
    string? FileImportMessage = null);
