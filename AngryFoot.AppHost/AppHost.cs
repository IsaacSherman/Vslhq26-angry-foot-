using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var azureOpenAiEndpoint = builder.Configuration["AzureOpenAI:Endpoint"];
var azureOpenAiApiKey = builder.Configuration["AzureOpenAI:Key"]
    ?? builder.Configuration["AzureOpenAI:ApiKey"];
var azureOpenAiDeployment = builder.Configuration["AzureOpenAI:ChatDeployment"]
    ?? builder.Configuration["AzureOpenAI:Deployment"]
    ?? builder.Configuration["AzureOpenAI:Model"];
var azureOpenAiEmbeddingDeployment = builder.Configuration["AzureOpenAI:EmbeddingDeployment"];

var apiService = builder.AddProject<Projects.AngryFoot_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

if (!string.IsNullOrWhiteSpace(azureOpenAiEndpoint))
{
    apiService = apiService.WithEnvironment("AzureOpenAI__Endpoint", azureOpenAiEndpoint);
}

if (!string.IsNullOrWhiteSpace(azureOpenAiApiKey))
{
    apiService = apiService.WithEnvironment("AzureOpenAI__ApiKey", azureOpenAiApiKey);
}

if (!string.IsNullOrWhiteSpace(azureOpenAiDeployment))
{
    apiService = apiService.WithEnvironment("AzureOpenAI__ChatDeployment", azureOpenAiDeployment);
}

if (!string.IsNullOrWhiteSpace(azureOpenAiEmbeddingDeployment))
{
    apiService = apiService.WithEnvironment("AzureOpenAI__EmbeddingDeployment", azureOpenAiEmbeddingDeployment);
}

// Qdrant-backed bullet retrieval is opt-in: it requires Docker to run the container, so it
// must never become a hard requirement to `dotnet run`/`dotnet test` this solution. Enable it
// with `Qdrant:Enabled=true` (user-secrets on this AppHost project, or a `Qdrant__Enabled`
// env var). When disabled (the default), the API service falls back to its existing
// deterministic keyword ranking.
if (builder.Configuration.GetValue("Qdrant:Enabled", false))
{
    var qdrant = builder.AddQdrant("qdrant")
        .WithDataVolume();

    apiService = apiService.WithReference(qdrant).WaitFor(qdrant);
}

builder.AddProject<Projects.AngryFoot_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
