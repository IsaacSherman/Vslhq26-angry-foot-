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

// Qdrant-backed bullet retrieval runs automatically: Aspire launches the latest Qdrant image
// as a Docker container and the API service waits for it to be healthy before serving traffic.
// Vector data is stored locally next to the SQLite database (not in an opaque Docker volume),
// so it survives container recreation the same way angryfoot.db does. Embeddings still require
// AzureOpenAI:EmbeddingDeployment to be configured; without it the API service falls back to
// deterministic keyword ranking even though Qdrant is running. Set Qdrant:Enabled=false
// (user-secrets on this AppHost project, or a `Qdrant__Enabled` env var) to skip starting the
// container entirely, e.g. in environments without Docker.
if (builder.Configuration.GetValue("Qdrant:Enabled", true))
{
    var qdrantDataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AngryFoot", "qdrant");
    Directory.CreateDirectory(qdrantDataPath);

    var qdrant = builder.AddQdrant("qdrant")
        .WithImageTag("latest")
        .WithDataBindMount(qdrantDataPath);

    apiService = apiService.WithReference(qdrant).WaitFor(qdrant);
}

// markitdown converts uploaded PDF and DOCX resumes to Markdown, which keeps every document
// parsing dependency out of the API service and covers both formats with one container instead of
// a library per format. Aspire runs the image the same way it runs Qdrant, and the first launch
// pulls it. Without the container the app is unchanged: resumes are still imported by pasting their
// text. Set Markitdown:Enabled=false (user-secrets on this AppHost project, or a
// `Markitdown__Enabled` env var) to skip starting it, e.g. in environments without Docker.
if (builder.Configuration.GetValue("Markitdown:Enabled", true))
{
    // The image is markitdown-mcp with no CMD, so these become its arguments. It binds inside the
    // container only; Aspire is what maps it to a host port.
    var markitdown = builder.AddContainer("markitdown", "mcp/markitdown")
        .WithHttpEndpoint(targetPort: 3001, name: "http")
        .WithArgs("--http", "--host", "0.0.0.0", "--port", "3001");

    apiService = apiService
        .WithEnvironment("Markitdown__Endpoint", markitdown.GetEndpoint("http"))
        .WaitFor(markitdown);
}

builder.AddProject<Projects.AngryFoot_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
