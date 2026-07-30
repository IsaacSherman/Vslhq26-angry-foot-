# Bullet'r

Bullet'r is a local-first resume tailoring app. It stores achievement bullets, enriches them with AI metadata, analyzes a target job description, and generates a tailored resume + cover letter in Markdown.

It is competing in the [https://github.com/live360events/vslhq26-hackathon], in the category Best Azure OpenAI / LLM-Powered App as primary and Best Microsoft Copilot Integration as secondary.

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