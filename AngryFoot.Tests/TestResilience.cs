using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace AngryFoot.Tests;

internal static class TestResilience
{
    /// <summary>
    /// Mirrors the production ApiClient resilience configuration: API calls that hit
    /// real AI can exceed the default 10s attempt timeout - a deep-review generation by
    /// a wide margin - and unsafe HTTP methods (POST /api/bullets, POST /api/generations)
    /// must never be retried because a retry after a slow attempt creates duplicate rows.
    /// </summary>
    public static void ConfigureStandardHandler(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(10);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(12);
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(20);
        options.Retry.DisableForUnsafeHttpMethods();
    }
}
