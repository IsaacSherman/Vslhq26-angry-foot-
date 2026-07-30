# Bullet'r

Helps a job candidate generate a bespoke resume for a target job description by storing achievement bullets, enriching them with AI metadata, analyzing the target job description, and generating a tailored resume + cover letter in Markdown.

## Team

- **Angry Foot**
- **Members:**
  - Isaac Sherman (@IsaacSherman)

## Category

- **Event:** [VSLHQ26 Hackathon](https://github.com/live360events/vslhq26-hackathon)
- **Primary:** Azure OpenAI/LLM app 
- **Secondary (optional):** Copilot integration

## What it does

Jobs practically require customized resumes to even be seen because of the prescreening for relevant skills.  Bullet'r both generates a resume from a job description (and the user's entered 'Bullets') and provides guidance on creating additional bullets to strengthen their application.  It also enhances/provides guidance on enhancing bullets written by the user if desired


## Architecture

Bullet'r is a .NET Aspire solution with two runnable services and three supporting projects:

- **AngryFoot.AppHost** — Aspire orchestrator. Launches the API service and web frontend, wires up service discovery, and hosts the developer dashboard.
- **AngryFoot.Web** — Blazor Server UI (Bullets, Profile, Generate, History pages). Talks to the API service exclusively through a typed `ApiClient` (`HttpClient` resolved via Aspire service discovery, `https+http://apiservice`). Renders generated Markdown with a Preview/Markdown toggle (Markdig).
- **AngryFoot.ApiService** — ASP.NET Core Minimal API hosting two front doors over the same application services: REST endpoints (`/api/bullets`, `/api/profile`, `/api/generations`, `/api/artifacts`, `/api/ai/status`) and an MCP server (`/mcp`, streamable HTTP) exposing bullet CRUD as tools for AI agents like Copilot. Application services handle bullet enrichment (AI tagging), job analysis, candidate-fit assessment, bullet ranking/rewriting, and resume/cover-letter assembly. Every AI-backed service calls Azure OpenAI through the provider-agnostic `IChatClient` abstraction and degrades to deterministic heuristics when AI is unavailable. Persistence is EF Core over a per-user SQLite database.
- **AngryFoot.Contracts** — shared DTOs referenced by both Web and ApiService, so the UI and API can never drift apart silently.
- **AngryFoot.ServiceDefaults** — shared Aspire plumbing: HTTP resilience (retry/circuit-breaker), service discovery, and OpenTelemetry tracing/metrics for both services.
- **AngryFoot.Tests** — xUnit suite: fast Moq-based unit tests for every service, plus Aspire integration tests that boot the full AppHost against isolated temp databases (including an end-to-end MCP client test).

```mermaid
flowchart LR
    subgraph AppHost["AngryFoot.AppHost (Aspire orchestrator)"]
        direction LR
        Web["AngryFoot.Web<br/>Blazor Server UI"]
        subgraph Api["AngryFoot.ApiService"]
            REST["REST endpoints<br/>/api/*"]
            MCP["MCP server<br/>/mcp"]
            Services["Application services<br/>bullets · profile · generation · fit"]
            Chat["IChatClient"]
            EF["EF Core"]
        end
    end

    Agent["MCP clients<br/>(Copilot, Claude, ...)"]
    AOAI["Azure OpenAI<br/>(heuristic fallback if down)"]
    DB[("SQLite<br/>per-user db")]

    Web -- "ApiClient (service discovery)" --> REST
    Agent --> MCP
    REST --> Services
    MCP --> Services
    Services --> Chat --> AOAI
    Services --> EF --> DB

    Contracts["AngryFoot.Contracts (shared DTOs)"] -.-> Web
    Contracts -.-> Api
    Defaults["AngryFoot.ServiceDefaults<br/>(resilience · discovery · OpenTelemetry)"] -.-> Web
    Defaults -.-> Api
```

The generation pipeline inside the API service runs as a chain: job description → `HeuristicJobAnalyzer` (extract requirements) → `FitAssessmentService` (score the user's chances against their bullet library) → `BulletRankingService` (pick the best bullets) → `BulletRewriteService` (tailor them to the job) → `ResumeMarkdownService` + `CoverLetterService` (assemble Markdown) → persisted as a `GenerationArtifact` (browsable on the History page).

## Tech stack

- **Languages:** C# (.NET 10), Razor
- **Frameworks/libraries:** ASP.NET Core Minimal APIs, Blazor Server, .NET Aspire 13.4 (orchestration, service discovery, dashboard), Entity Framework Core 10 + SQLite, Microsoft.Extensions.AI (`IChatClient` abstraction), ModelContextProtocol C# SDK (MCP server + client), Serilog (rolling file logs), Markdig (Markdown rendering), Bootstrap 5. Tests: xUnit v3, Moq, AwesomeAssertions, Aspire.Hosting.Testing.
- **AI models/services:** Azure OpenAI chat completions (default deployment `gpt-5-mini`) for bullet enrichment, job analysis, fit assessment, bullet rewriting, and cover-letter drafting. Every AI feature has a deterministic heuristic fallback, so the app remains fully functional with no AI configured.
- **Hosting:** Local-first. The Aspire AppHost runs both services on Kestrel with dynamically assigned ports; no cloud deployment or Docker required. Data lives in a per-user SQLite database.

## Getting started

### Prerequisites

- **.NET 10 SDK** (the repo builds with a 10.0.4xx preview SDK; Aspire 13.4 is pulled in via the AppHost project SDK — no separate workload install needed)
- **Azure OpenAI resource with a chat deployment** (optional). Without it the app runs entirely on heuristic fallbacks; with it you get full AI enrichment, fit assessment, and generation. You will need three values: the resource endpoint URL (`https://<resource>.openai.azure.com`), an API key, and the chat deployment name.
- No Docker, no database server — SQLite is embedded and created on first run.

### Setup

```bash
# Clone the repo
git clone https://github.com/IsaacSherman/Vslhq26-angry-foot-.git
cd Vslhq26-angry-foot-

#Configure Azure OpenAI via user secrets - see Configuration below
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com/openai/v1" --project AngryFoot.AppHost
dotnet user-secrets set "AzureOpenAI:ApiKey" "<your-api-key>" --project AngryFoot.AppHost
 (Optional): dotnet user-secrets set "AzureOpenAI:ChatDeployment" "gpt-5-mini" --project AngryFoot.AppHost
 Defaults to gpt-5-mini if not set.  If you have multiple deployments, you can set this to the one you want to use for chat completions.
# Run everything (API + Web + Aspire dashboard)
dotnet run --project AngryFoot.AppHost

# Run the test suite
dotnet test
```

The Aspire dashboard URL is printed on startup; it lists the live endpoints for the web frontend and API service (ports are assigned dynamically).

### Configuration

Secrets are stored with **.NET user secrets** (never committed; there is no `.env` file). All settings are read from standard .NET configuration, so environment variables work too (replace `:` with `__`, e.g. `AzureOpenAI__ApiKey`).

| Setting | Shape | Purpose |
|---|---|---|
| `AzureOpenAI:Endpoint` | `https://<resource>.openai.azure.com` | Azure OpenAI resource URL. If a full deployment path is pasted, the deployment name is extracted automatically. |
| `AzureOpenAI:ApiKey` (or `AzureOpenAI:Key`) | opaque key string | Azure OpenAI API key |
| `AzureOpenAI:ChatDeployment` (or `Deployment` / `Model`) | deployment name, e.g. `gpt-5-mini` | Chat deployment to use; defaults to `gpt-5-mini` |
| `AzureOpenAI:ServiceVersion` | e.g. `V2024_10_21` | Optional Azure OpenAI API version pin |
| `ConnectionStrings:angryfoot` | `Data Source=<path>` | Optional SQLite override. Default: `%LOCALAPPDATA%\AngryFoot\angryfoot.db` (per-user, survives rebuilds; integration tests point this at isolated temp files) |

If AI is not configured, `GET /api/ai/status` reports Unhealthy with setup instructions, and all AI features fall back to heuristics. Both services write rolling log files to their own `Logs/` directory in addition to the console. The MCP endpoint is served at `http://localhost:<apiservice-port>/mcp` (streamable HTTP; find the port on the Aspire dashboard).

## Demo

- Video file in this repo: `./demo/demo.mp4`

## Known limitations

- **MCP integration is not completely implemented.** The app only supports a single MCP service (`apiservice`) with no multi-service support or service discovery, and the MCP tools currently cover bullet management only — profile editing and resume generation are not yet exposed as tools.
- **Single user, no authentication.** The REST API and MCP endpoint are unauthenticated and the database holds exactly one profile, so the app is local-use only; anything that can reach the ports can read and write.
- **Markdown output only.** Generated resumes and cover letters are Markdown; there is no PDF or DOCX export yet, so final formatting happens in whatever tool you paste into.
- **Bullets map to employers by exact name match.** A bullet lands under a work-history entry only when its employer field matches the profile entry (case-insensitive); unassigned bullets fall into a generic "Selected Experience" section.
- **Generation is synchronous.** A full generation chains several AI calls inside one HTTP request (typically 30-90 seconds) with no progress streaming, background queue, or cancellation UI.
- **Fit assessment only sees the bullet library.** It does not weigh work-history dates, education, or certifications, so requirements like "7+ years of experience" are not evaluated.
- **AI output is not fact-checked.** Prompts forbid inventing metrics or technologies, but there is no verification pass — generated content should be reviewed before sending to a real employer.
- **Heuristic fallbacks are English- and .NET-centric.** The keyword lists behind offline tagging, job analysis, and ranking are tuned for English-language, Microsoft-stack roles; other domains degrade to weaker matches when AI is unavailable.
- **No pagination or rate limiting.** Bullet and history lists load everything at once, and AI-backed endpoints have no throttling, which is fine for a single local user but not for shared deployment.

## Open Source Software (FOSS) Attribution

Bullet'r is built on the following open-source packages. Versions and licenses reflect the package metadata shipped with each NuGet package (`dotnet list package` for the full transitive graph).

### Platform & SDKs

| Component | Version | License | Project |
|---|---|---|---|
| .NET / ASP.NET Core / Blazor | net10.0 | MIT | <https://github.com/dotnet> |
| Aspire (AppHost SDK, orchestration) | 13.4.6 | MIT | <https://github.com/microsoft/aspire> |

### API Service (`AngryFoot.ApiService`)

| Package | Version | License | Purpose |
|---|---|---|---|
| Azure.AI.OpenAI | 2.1.0 | MIT | Azure OpenAI client for chat completions |
| Microsoft.AspNetCore.OpenApi | 10.0.8 | MIT | OpenAPI document generation |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.8 | MIT | EF Core ORM with SQLite provider |
| Microsoft.EntityFrameworkCore.Design | 10.0.8 | MIT | EF Core migrations tooling (build-time) |
| Microsoft.Extensions.AI | 10.8.1 | MIT | Provider-agnostic `IChatClient` abstractions |
| Microsoft.Extensions.AI.OpenAI | 10.8.1 | MIT | OpenAI adapter for Microsoft.Extensions.AI |
| Microsoft.Extensions.Configuration.UserSecrets | 10.0.10 | MIT | Local secret storage for AI credentials |
| Microsoft.Extensions.Logging.Console | 10.0.10 | MIT | Console logging provider |
| ModelContextProtocol.AspNetCore | 2.0.0 | Apache-2.0 | MCP server over streamable HTTP (`/mcp`) |
| Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 | Serilog bridge for Microsoft.Extensions.Logging |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | Rolling file logging to `./Logs/` |

### Web Frontend (`AngryFoot.Web`)

| Package | Version | License | Purpose |
|---|---|---|---|
| Markdig | 1.3.2 | BSD-2-Clause | Markdown-to-HTML rendering for resume/cover letter preview |
| Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 | Serilog bridge for Microsoft.Extensions.Logging |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | Rolling file logging to `./Logs/` |
| Bootstrap (vendored in `wwwroot/lib`) | 5.3.3 | MIT | CSS framework for the Blazor UI |

### Service Defaults (`AngryFoot.ServiceDefaults`)

| Package | Version | License | Purpose |
|---|---|---|---|
| Microsoft.Extensions.Http.Resilience | 10.6.0 | MIT | Standard HTTP retry/timeout/circuit-breaker policies |
| Microsoft.Extensions.ServiceDiscovery | 10.6.0 | MIT | Aspire service discovery (`https+http://apiservice`) |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.15.3 | Apache-2.0 | OTLP telemetry exporter |
| OpenTelemetry.Extensions.Hosting | 1.15.3 | Apache-2.0 | OpenTelemetry host integration |
| OpenTelemetry.Instrumentation.AspNetCore | 1.15.2 | Apache-2.0 | ASP.NET Core tracing/metrics |
| OpenTelemetry.Instrumentation.Http | 1.15.1 | Apache-2.0 | HttpClient tracing/metrics |
| OpenTelemetry.Instrumentation.Runtime | 1.15.1 | Apache-2.0 | .NET runtime metrics |

### Tests (`AngryFoot.Tests`)

| Package | Version | License | Purpose |
|---|---|---|---|
| Aspire.Hosting.Testing | 13.4.6 | MIT | Integration testing against the full AppHost |
| AwesomeAssertions | 9.5.0 | Apache-2.0 | Fluent assertion library |
| coverlet.collector | 6.0.2 | MIT | Code coverage collection |
| Microsoft.Extensions.AI | 10.8.1 | MIT | `IChatClient` abstractions for test fakes |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT | .NET test platform |
| ModelContextProtocol | 2.0.0 | Apache-2.0 | MCP client for end-to-end `/mcp` tests |
| Moq | 4.20.72 | BSD-3-Clause | Mocking framework |
| xunit.v3 | 3.0.1 | Apache-2.0 | Test framework |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 | Visual Studio / `dotnet test` runner |

### Notable transitive components

| Component | License | Notes |
|---|---|---|
| Serilog (core) | Apache-2.0 | Pulled in by the Serilog packages above |
| SQLitePCLRaw / e_sqlite3 | Apache-2.0 | Native SQLite bindings used by EF Core |
| SQLite | Public Domain | The embedded database engine itself |
| Polly / Polly.Core | BSD-3-Clause | Resilience engine behind Microsoft.Extensions.Http.Resilience |
| OpenAI (official .NET library) | MIT | Underlying client used by Azure.AI.OpenAI |

All listed licenses (MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, Public Domain) are permissive. If you redistribute this application, retain the copyright and license notices of these projects per their respective terms.

# Special Thanks
Special thanks to Ryan (https://github.com/RyCox), who helped me polish the idea that's been kicking around in my brain for years.
# License
This project is under the MIT license.  MIT License

Copyright (c) 2026 Isaac Ben Sherman

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
