# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Working agreement

### 1. DRY, above everything else

One fact, one place. This is the principle that outranks the rest: before adding a constant, DTO,
keyword list, scoring rule, or prompt, find where that concept already lives and extend it there.
Copy-paste-and-adjust is not a shortcut here, it is the defect. Where DRY collides with any other rule
in this file, DRY wins.

Where it already applies in this repo:

- Every shape crossing the Web↔API boundary is defined once in `AngryFoot.Contracts` and nowhere else.
  Web and ApiService both reference it so they cannot drift apart silently.
- Web display-only mappings live in `AngryFoot.Web/Models/` (`CoverageBands`, `RevisionModeLabels`).
  Domain rules do not; they live in the API service.
- REST and MCP are two doors over the same scoped services. A rule belongs in `Application/` so both
  doors get it, never in one endpoint.
- The HTTP resilience settings in [Program.cs](AngryFoot.Web/Program.cs) are mirrored in
  [TestResilience.cs](AngryFoot.Tests/TestResilience.cs). This is the one tolerated duplication —
  the test project does not reference Web — so change both, and treat it as a wart, not a pattern.

### 2. Make surgical changes

Change what the task requires and stop. Do not reformat, rename, re-order, or "tidy" code you happened
to open. Do not add an interface, options class, or abstraction layer for a second caller that does not
exist yet. If a fix is three lines, it is three lines — a rewrite of the surrounding method needs a
reason stated out loud, not a preference. Unrelated problems you notice go in the response as a note,
not in the diff.

The one expansion this rule permits: if keeping the diff small would mean duplicating something, don't.
Extract it and use it in both places — see rule 1.

### 3. Be ruthless about comments

Comments are not code, but they rot like code, and nothing fails when they do — which is exactly why
they are dangerous. Write one only when the code genuinely cannot carry the meaning itself. Reach first
for a descriptive name, a smaller function, the right level of abstraction, a type that makes the
invalid state unrepresentable.

The test: **a comment that describes _what_ the code does is a refactor you skipped.** Delete it and fix
the code. A comment earns its place only by explaining *why* — the alternative that was considered and
lost, the bug a value guards against, the external constraint that makes an obvious approach wrong.
That reasoning is not recoverable from the code, so it is the only thing worth committing.
[AiChatClientExtensions.cs](AngryFoot.ApiService/Ai/AiChatClientExtensions.cs) sets the bar; when you
edit code whose comment no longer matches, updating the comment is part of the change, not a follow-up.

### 4. Unit tests are documentation

They are also the fastest way to find out you were wrong, so hold them to the standard of the code —
higher, if anything, because nothing downstream will catch a test that quietly asserts nothing.

**Test the mechanism, not the language.** Asserting that a property returns what the constructor set
tests the runtime. Assert the behaviour *we* wrote: that the ranker put the relevant bullet first, that
the reviewer refused to raise a strength without a citation, that the parser found the object inside the
prose. If a test would still pass with the body of the method deleted, it is not a test.

**Unhappy paths and edges, with extreme prejudice.** In this repo that has a specific meaning: every AI
feature has a deterministic fallback, so a test that only covers "the model answered correctly" leaves
half the feature unverified. Cover the unparseable answer, the call that throws, cancellation, the id
the model invented, the empty collection, the value at the boundary. The fakes exist for exactly this —
`ChatClientMocks.ReturningText`/`.Throwing`, `ScriptedChatClient`, `FakeRefinementPipeline`,
`FakeBulletVectorStore`. `AiEvidenceReviewerTests` and `AiChatClientExtensionsTests` are the models to
follow; both spend most of their length on what happens when things go wrong.

**Be communicative.** The convention is `Method_Condition_Expectation`, written to read as a sentence —
`ReviewAsync_RaisingWithNoCitationIsIgnored`,
`GetJsonResponseAsync_DoesNotRetryATransportFailureAsThoughItWereTheSchema`. A reader scanning the
method list should learn the rules of the unit without opening a body. Name the subject `sut`, assert
with AwesomeAssertions, keep one behaviour per test so a failure names itself, and skip
arrange/act/assert ceremony comments — rule 3 applies here too. A shared fixture or helper earns a
doc comment when it encodes a non-obvious reason (see the envelope helper in `BulletRewriteServiceTests`).

**Upgrade what you touch.** A test that does not meet this bar gets fixed when you work on it, not
noted for later.

### 5. C# idiom in use

File-scoped namespaces, primary constructors, `internal sealed` application types, `sealed record` for
DTOs, collection expressions, nullable enabled. Short, single-purpose classes. Match what is there.

## Commands

```bash
dotnet build                                                     # solution is AngryFoot.slnx
dotnet run --project AngryFoot.AppHost                           # API + Web + Qdrant container + dashboard
dotnet run --project AngryFoot.AppHost -- --Qdrant:Enabled=false # same, without Docker
dotnet test                                                      # never requires Docker or AI
```

Tests run on **xunit v3 over Microsoft.Testing.Platform**, not VSTest — there is no `dotnet test --filter`.
Runner args go after `--`:

```bash
dotnet test -- --filter-class "*BulletRankingServiceTests*"
```

```bash
dotnet test -- --filter-method "*WhenAiFails*"
```

```bash
dotnet test -- --filter-namespace "AngryFoot.Tests.Unit"
```

Simple filters (`--filter-class` / `--filter-method` / `--filter-namespace`, plus `--filter-not-*`)
cannot be combined with `--filter-query`. `--no-build` works and is much faster when nothing changed.

EF Core migrations (local tool manifest):

```bash
dotnet tool restore
```

```bash
dotnet ef migrations add <Name> --project AngryFoot.ApiService
```

Migrations are applied at startup by `MigrateAndSeedAsync` — there is no `database update` step to run.

`RealAiSmokeTests` bill a real Azure OpenAI resource, so they no-op unless `RUN_AI_INTEGRATION=1` is set
alongside configured user secrets. They pass silently when skipped; a green run does not mean they ran.

`.claude/launch.json` defines two preview profiles: `apphost` (Qdrant off) and `apphost-rag` (Qdrant on).

## Architecture

.NET Aspire solution. `AngryFoot.AppHost` orchestrates `AngryFoot.ApiService` (Minimal API + MCP server)
and `AngryFoot.Web` (Blazor Server), with an optional Qdrant container. `AngryFoot.Contracts` holds the
shared DTOs; `AngryFoot.ServiceDefaults` holds resilience, service discovery, and OpenTelemetry.

The API service is layered `Api/` → `Application/` → `Data/` + `Domain/`. Endpoint files only map routes
and translate results to status codes; all behaviour is in `Application/`. REST and MCP
([`Mcp/BulletTools.cs`](AngryFoot.ApiService/Mcp/BulletTools.cs)) are two front doors over the *same*
scoped services — a rule enforced in `Application/`, not in an endpoint, holds for both.

The generation pipeline is chained in
[GenerationOrchestrator.cs](AngryFoot.ApiService/Application/Generation/GenerationOrchestrator.cs), which
has two entry points: `GenerateAsync` (a real posting: analyze → coverage → retrieve → rewrite → assemble
→ cover letter) and `GenerateGenericAsync` (no posting: `GenericBulletRankingService` +
`TargetTitleRelevanceService`, then the same rewrite and assembly stages, no coverage and no letter).
Both persist a `GenerationArtifact`. README's *Generic resume* section explains the ranking weights.

### Invariants worth knowing before you edit

- **Every AI feature degrades to a deterministic path.** `IChatClient` is *always* registered — when
  Azure OpenAI is unconfigured it resolves to `StaticResponseChatClient`, so a service can never test
  for AI by null-checking its dependency. Services attempt the call, fail to parse, log, and fall back.
  `AiConfigurationStatus` is the flag, and it exists for `/api/ai/status`, not for branching logic.
- **AI JSON goes through `GetJsonResponseAsync<T>`.** It derives a JSON schema from `T`, retries once
  unconstrained if the deployment rejects it, and still parses tolerantly. Do not hand-roll a
  "return JSON like this" prompt. A schema constrains shape only — keep validating that returned ids
  and values are real.
- **Semantic retrieval is optional and silent.** No embedding deployment (or no Qdrant) means
  `NullBulletVectorStore` and a fall back to `BulletRankingService`'s keyword scan. Anything you add on
  the retrieval path needs both branches to work.
- **Tests must never need Docker or a live model.** `TestDatabase.AppHostArgs` disables Qdrant and
  `TestAiConfiguration.AppHostArgs` blanks the endpoint so the heuristic path runs regardless of whose
  user secrets are on the machine. Integration tests point SQLite at a temp file — the real database
  lives at `%LOCALAPPDATA%\AngryFoot\angryfoot.db` and survives rebuilds.
- **Retries are disabled for unsafe HTTP methods.** `POST /api/bullets` and `POST /api/generations` are
  not idempotent; a retry after a slow AI call creates duplicate rows.
- **The generation explanation costs no AI call.** It is computed after the choices are made and stored
  with the artifact, so it can never disagree with the resume it describes, and History replays the
  explanation for the resume actually sent. Keep it deterministic.
- **Evidence diagnostics are a plugin list.** Add an `IEvidenceDiagnosticAnalyzer` and register it in
  [Program.cs](AngryFoot.ApiService/Program.cs); the engine picks it up.
- ApiService application types are `internal sealed`; tests reach them via `InternalsVisibleTo`. Only
  endpoint mappers, DI extensions, and contracts are public. Keep it that way.
- Web talks to the API exclusively through [ApiClient.cs](AngryFoot.Web/Services/ApiClient.cs). No
  `HttpClient` use in components.

## Docs

[README.md](README.md) is the long-form reference — every feature has a section explaining the design and
its limits, and the *Known limitations* list is honest and current. [docs/API.md](docs/API.md) is the
endpoint contract with request/response shapes. Both are hand-maintained: when you change behaviour or a
payload, update the section that describes it.
