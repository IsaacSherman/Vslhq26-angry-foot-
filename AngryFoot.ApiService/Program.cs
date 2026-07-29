using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Api;
using AngryFoot.ApiService.Application.Artifacts;
using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Application.Profile;
using AngryFoot.ApiService.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddAngryFootAi(builder.Configuration);
builder.Services.AddScoped<IBulletTagger, OpenAiBulletTagger>();
builder.Services.AddScoped<IBulletService, BulletService>();
builder.Services.AddScoped<IBulletRewriteAssistant, BulletRewriteAssistant>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IArtifactService, ArtifactService>();
builder.Services.AddScoped<IJobAnalyzer, HeuristicJobAnalyzer>();
builder.Services.AddScoped<BulletRankingService>();
builder.Services.AddScoped<BulletRewriteService>();
builder.Services.AddScoped<ResumeMarkdownService>();
builder.Services.AddScoped<CoverLetterService>();
builder.Services.AddScoped<IGenerationOrchestrator, GenerationOrchestrator>();

builder.Services.AddDbContext<AngryFootDbContext>(options =>
{
    var databasePath = Path.Combine(builder.Environment.ContentRootPath, "angryfoot.db");
    options.UseSqlite($"Data Source={databasePath}");
});

var app = builder.Build();

await app.Services.MigrateAndSeedAsync();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "API service is running. Navigate to /api/profile for bootstrap profile data.");

app.MapAiEndpoints();

var api = app.MapGroup("/api");
api.MapProfileEndpoints();
api.MapBulletEndpoints();
api.MapArtifactEndpoints();
api.MapGenerationEndpoints();

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];
app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapDefaultEndpoints();

app.Run();

internal sealed record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
