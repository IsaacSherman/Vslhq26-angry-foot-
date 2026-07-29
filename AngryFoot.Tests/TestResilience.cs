using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace AngryFoot.Tests;

internal static class TestResilience
{
    /// <summary>
    /// Mirrors the production ApiClient resilience configuration: API calls that hit
    /// real AI can exceed the default 10s attempt timeout, and unsafe HTTP methods
    /// (POST /api/bullets, POST /api/generations) must never be retried because a
    /// retry after a slow attempt creates duplicate rows.
    /// </summary>
    public static void ConfigureStandardHandler(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(4);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
        options.Retry.DisableForUnsafeHttpMethods();
    }
}
