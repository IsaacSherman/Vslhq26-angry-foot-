var builder = DistributedApplication.CreateBuilder(args);

var azureOpenAiEndpoint = builder.Configuration["AzureOpenAI:Endpoint"];
var azureOpenAiApiKey = builder.Configuration["AzureOpenAI:Key"]
    ?? builder.Configuration["AzureOpenAI:ApiKey"];
var azureOpenAiDeployment = builder.Configuration["AzureOpenAI:ChatDeployment"]
    ?? builder.Configuration["AzureOpenAI:Deployment"]
    ?? builder.Configuration["AzureOpenAI:Model"];

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

builder.AddProject<Projects.AngryFoot_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
