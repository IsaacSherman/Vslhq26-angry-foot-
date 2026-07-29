using AngryFoot.Web;
using AngryFoot.Web.Components;
using AngryFoot.Web.Services;
using Microsoft.Extensions.Http.Resilience;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

builder.Services.AddHttpClient<WeatherApiClient>(client =>
    {
        // This URL uses "https+http://" to indicate HTTPS is preferred over HTTP.
        // Learn more about service discovery scheme resolution at https://aka.ms/dotnet/sdschemes.
        client.BaseAddress = new("https+http://apiservice");
    });

// RemoveAllResilienceHandlers is experimental (EXTEXP0001) but is the only way to
// replace the default handler added by ConfigureHttpClientDefaults in ServiceDefaults.
#pragma warning disable EXTEXP0001
builder.Services.AddHttpClient<ApiClient>(client =>
    {
        client.BaseAddress = new("https+http://apiservice");
        // Must exceed the resilience handler's total request timeout below.
        client.Timeout = TimeSpan.FromMinutes(5);
    })
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler(options =>
    {
        // Generation requests fan out to several sequential AI calls and can run
        // well past the default 10s attempt / 30s total timeouts.
        options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(4);
        // Sampling duration must be at least double the attempt timeout.
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
        // POST /api/generations and POST /api/bullets are not idempotent; a retry
        // after a slow attempt would create duplicate artifacts/bullets.
        options.Retry.DisableForUnsafeHttpMethods();
    });
#pragma warning restore EXTEXP0001

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
