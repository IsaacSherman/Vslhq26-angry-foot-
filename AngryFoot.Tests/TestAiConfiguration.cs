namespace AngryFoot.Tests;

internal static class TestAiConfiguration
{
    /// <summary>
    /// App host args that leave the API service with no chat deployment, so every AI-backed service
    /// takes its heuristic path.
    /// <para>
    /// Needed because the app host reads Azure OpenAI credentials from the developer's user secrets:
    /// without this, whether a test exercises the deterministic path depends on whose machine it
    /// runs on. Blanking the endpoint is enough - the API service treats endpoint, key, and
    /// deployment as all-or-nothing - and the app host only forwards values it finds non-blank.
    /// </para>
    /// </summary>
    public static readonly string[] AppHostArgs = [.. TestDatabase.AppHostArgs, "--AzureOpenAI:Endpoint="];
}
