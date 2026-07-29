# Bullet'r

Bullet'r is a local-first resume tailoring app. It stores achievement bullets, enriches them with AI metadata, analyzes a target job description, and generates a tailored resume + cover letter in Markdown.

## Tech Stack

- .NET 10
- ASP.NET Core Web API
- Blazor Server
- EF Core + SQLite
- .NET Aspire AppHost
- Azure OpenAI via `Azure.AI.OpenAI` + `Microsoft.Extensions.AI`

## Prerequisites

- .NET 10 SDK
- Azure OpenAI resource and deployed chat model

## Configure Secrets

Set secrets on the **ApiService** project:

```powershell
dotnet user-secrets --project .\AngryFoot.ApiService set "AzureOpenAI:Endpoint" "https://<your-resource>.openai.azure.com/"
dotnet user-secrets --project .\AngryFoot.ApiService set "AzureOpenAI:ApiKey" "<your-api-key>"
```

Deployment defaults to `gpt-5-mini`.

Optional overrides:

- `AzureOpenAI:ChatDeployment` or `AzureOpenAI:Deployment`
- `AzureOpenAI:Key` instead of `AzureOpenAI:ApiKey`

## Run

Start the distributed app from AppHost:

```powershell
dotnet run --project .\AngryFoot.AppHost
```

## Main UI Routes

- `/bullets` — manage bullet library (CRUD/search/filter/re-enrich)
- `/profile` — edit candidate profile, work history, education, certifications
- `/generate` — analyze job description + generate resume/cover letter markdown
- `/history` — view and delete prior generation artifacts

## Test

```powershell
dotnet test .\AngryFoot.slnx
```
