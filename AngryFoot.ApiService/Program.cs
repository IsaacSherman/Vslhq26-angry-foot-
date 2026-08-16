using AngryFoot.ApiService.Ai;
using AngryFoot.ApiService.Api;
using AngryFoot.ApiService.Application.Artifacts;
using AngryFoot.ApiService.Application.Benchmarks;
using AngryFoot.ApiService.Application.Bullets;
using AngryFoot.ApiService.Application.Evidence;
using AngryFoot.ApiService.Application.Evidence.Diagnostics;
using AngryFoot.ApiService.Application.Generation;
using AngryFoot.ApiService.Application.Profile;
using AngryFoot.ApiService.Application.Refinement;
using AngryFoot.ApiService.Data;
using AngryFoot.ApiService.Mcp;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Log to a rolling file in ./Logs/ in addition to the default console/OTel providers.
var logsPath = Path.Combine(builder.Environment.ContentRootPath, "Logs");
Directory.CreateDirectory(logsPath);
builder.Logging.AddSerilog(new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(
        Path.Combine(logsPath, "apiservice-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .CreateLogger(), dispose: true);

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddAngryFootAi(builder.Configuration);
builder.AddAngryFootRetrieval();
builder.Services.AddScoped<IRefinementGrounding, BulletLibraryGrounding>();
builder.Services.AddScoped<IDraftRefinementPipeline, DraftRefinementPipeline>();
builder.Services.AddScoped<IBulletTagger, OpenAiBulletTagger>();
builder.Services.AddScoped<IBulletService, BulletService>();
builder.Services.AddScoped<IBulletRewriteAssistant, BulletRewriteAssistant>();
builder.Services.AddScoped<IBulletRevisionService, BulletRevisionService>();
builder.Services.AddScoped<IBulletDuplicateDetector, BulletDuplicateDetector>();
builder.Services.AddScoped<IResumeBulletImportService, ResumeBulletImportService>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<ILinkedInProfileImportService, LinkedInImportService>();
builder.Services.AddScoped<IArtifactService, ArtifactService>();
builder.Services.AddScoped<IJobAnalyzer, HeuristicJobAnalyzer>();
builder.Services.AddScoped<IEvidenceReviewer, AiEvidenceReviewer>();
builder.Services.AddScoped<IEvidenceCoverageAnalyzer, EvidenceCoverageService>();
builder.Services.AddScoped<IEvidenceDiagnosticAnalyzer, MissingSkillAnalyzer>();
builder.Services.AddScoped<IEvidenceDiagnosticAnalyzer, WeakEvidenceAnalyzer>();
builder.Services.AddScoped<IEvidenceDiagnosticAnalyzer, DuplicateBulletAnalyzer>();
builder.Services.AddScoped<IEvidenceDiagnosticAnalyzer, MeasurableImpactAnalyzer>();
builder.Services.AddScoped<IEvidenceDiagnosticAnalyzer, OverusedWordingAnalyzer>();
builder.Services.AddScoped<IEvidenceDiagnosticAnalyzer, BulletOrderingAnalyzer>();
builder.Services.AddSingleton<IOccupationBenchmarkDataset, OccupationBenchmarkDataset>();
builder.Services.AddScoped<IOccupationBenchmarkService, OccupationBenchmarkService>();
builder.Services.AddScoped<BulletRetrievalService>();
builder.Services.AddScoped<BulletRankingService>();
builder.Services.AddScoped<GenericBulletRankingService>();
builder.Services.AddScoped<TargetTitleRelevanceService>();
builder.Services.AddScoped<BulletRewriteService>();
builder.Services.AddScoped<ResumeMarkdownService>();
builder.Services.AddScoped<CoverLetterService>();
builder.Services.AddScoped<IGenerationOrchestrator, GenerationOrchestrator>();

// MCP server over streamable HTTP; tools reuse the same scoped application services
// as the REST endpoints.
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools([typeof(BulletTools)]);

builder.Services.AddDbContext<AngryFootDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("angryfoot");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        // Default to a per-user data directory: the content root can be read-only in
        // deployed environments, and a DB next to the binaries gets shared by tests.
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AngryFoot");
        Directory.CreateDirectory(dataDirectory);
        var databasePath = Path.Combine(dataDirectory, "angryfoot.db");

        // One-time migration from the previous location next to the app binaries.
        var legacyPath = Path.Combine(builder.Environment.ContentRootPath, "angryfoot.db");
        if (!File.Exists(databasePath) && File.Exists(legacyPath))
        {
            File.Copy(legacyPath, databasePath);
        }

        connectionString = $"Data Source={databasePath}";
    }

    options.UseSqlite(connectionString);
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

app.MapMcp("/mcp");

var api = app.MapGroup("/api");
api.MapProfileEndpoints();
api.MapBulletEndpoints();
api.MapArtifactEndpoints();
api.MapGenerationEndpoints();

app.MapDefaultEndpoints();

app.Run();

