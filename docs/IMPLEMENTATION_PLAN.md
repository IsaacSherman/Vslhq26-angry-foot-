# AngryFoot — Tailored Resume & Cover Letter Generator
## Implementation Plan (MVP, ~1 Working Day)

---

## 1. Executive Summary

AngryFoot is a **local-first, single-user** application that maintains a curated database of achievement-oriented resume bullets, enriches them with AI-generated metadata (skills, technologies, tags, job categories), and — given a job description — selects, rewrites, and assembles a tailored **Resume (Markdown)** and **Cover Letter (Markdown)**. Every generation is persisted for later review.

The solution is a **modular monolith** built on the existing **.NET Aspire** scaffold already present in the repository:

- **AngryFoot.AppHost** — Aspire orchestrator (runs API + Web together).
- **AngryFoot.ApiService** — ASP.NET Core Web API (REST), EF Core + SQLite, AI pipeline. Already references `Azure.AI.OpenAI`, `Microsoft.Extensions.AI`, `Microsoft.SemanticKernel.Connectors.SqliteVec`.
- **AngryFoot.Web** — Blazor **Server** UI (already configured with `AddInteractiveServerComponents`).
- **AngryFoot.ServiceDefaults** — shared telemetry/health/resilience.
- **AngryFoot.Tests** — xUnit test project.

The AI layer is a **pipeline of small, single-responsibility stages** (tag → analyze → rank → rewrite → resume → cover letter), each with structured JSON contracts, deterministic parsing, and graceful degradation. This maximizes consistency, testability, and parallel development.

**Design north star:** simplicity and a working MVP in ~8 hours. No microservices, no message bus, no distributed infrastructure, no DOCX/PDF in MVP.

**Frontend recommendation: Blazor Server** (justified in §3.4).

---

## 2. High-Level Architecture Diagram

```
                        ┌─────────────────────────────────────────┐
                        │        AngryFoot.AppHost (Aspire)         │
                        │   orchestration • service discovery •     │
                        │        health checks • telemetry          │
                        └───────────────┬──────────────┬────────────┘
                                        │              │
                    WithReference(api)  │              │
                                        ▼              ▼
        ┌───────────────────────────────────┐   ┌──────────────────────────────┐
        │        AngryFoot.Web (Blazor       │   │   AngryFoot.ApiService (API) │
        │        Server, Interactive)        │   │                              │
        │                                    │   │  Controllers / Minimal APIs  │
        │  Pages:                            │   │   ┌────────────────────────┐ │
        │   • Bullets (CRUD/search)          │──▶│   │  Application Services   │ │
        │   • Profile                        │   │   │  (Bullet, Profile,      │ │
        │   • Generate (job desc → outputs)  │   │   │   Generation, Artifact) │ │
        │   • History (artifacts)            │   │   └───────────┬────────────┘ │
        │                                    │   │               │              │
        │  Typed HttpClient (ApiClient)      │   │   ┌───────────▼────────────┐ │
        └───────────────────────────────────┘   │   │   AI Pipeline (stages)  │ │
                                                 │   │  tag/analyze/rank/      │ │
                                                 │   │  rewrite/resume/cover   │ │
                                                 │   └───────────┬────────────┘ │
                                                 │               │              │
                                                 │   ┌───────────▼────────────┐ │
                                                 │   │  EF Core DbContext      │ │
                                                 │   └───────────┬────────────┘ │
                                                 └───────────────┼──────────────┘
                                                                 ▼
                                                        ┌──────────────────┐
                                                        │  SQLite (file)   │
                                                        │  angryfoot.db    │
                                                        └──────────────────┘
                                                                 ▲
                                                                 │ IChatClient
                                                        ┌──────────────────┐
                                                        │  Azure OpenAI /   │
                                                        │  OpenAI (chat)    │
                                                        └──────────────────┘
```

**Request flow (generation):** Web → `POST /api/generations` → GenerationService orchestrates AI pipeline (job analysis → bullet ranking → rewrite → resume assembly → cover letter) → persists `GenerationArtifact` → returns Markdown to Web.

---

## 3. Solution Architecture

### 3.1 Layering (inside ApiService — modular monolith)

```
AngryFoot.ApiService
├── Api/                 REST endpoints (Minimal API endpoint groups OR controllers)
├── Application/         Application services (orchestration, no EF/HTTP leakage)
│   ├── Bullets/
│   ├── Profile/
│   ├── Generation/
│   └── Artifacts/
├── Ai/                  AI pipeline stages + prompt templates + JSON contracts
│   ├── Contracts/       DTOs for structured I/O
│   ├── Stages/          BulletTagger, JobAnalyzer, BulletRanker, BulletRewriter,
│   │                    ResumeGenerator, CoverLetterGenerator
│   └── Prompts/         *.md / const string prompt templates
├── Data/                EF Core DbContext, entity configs, migrations, seeding
│   └── Entities/
├── Domain/              Entities + value objects (POCO), enums
└── Contracts/           Public API request/response DTOs (shared shape with Web)
```

**Rule:** `Api` → `Application` → (`Ai`, `Data`). `Ai` and `Data` never call back up. DTOs in `Contracts` are the ONLY types crossing the API boundary. This keeps workstreams isolated (see §9).

### 3.2 Shared DTO strategy (critical for parallelism)

Create a small **shared class library `AngryFoot.Contracts`** referenced by BOTH `ApiService` and `Web`. It contains only:
- Request/response records (e.g., `BulletDto`, `CreateBulletRequest`, `GenerationRequest`, `GenerationResultDto`, `ProfileDto`).
- Enums shared across the wire.

This lets the Backend and Frontend agents compile against a **single source of truth** with zero duplication and minimal merge conflict surface. It is the primary coordination artifact.

### 3.3 Persistence

- **EF Core 9/10 + SQLite** provider (`Microsoft.EntityFrameworkCore.Sqlite`).
- Single `AngryFootDbContext`.
- **Code-first migrations**; DB auto-created/migrated on API startup (`db.Database.Migrate()`).
- SQLite file lives under a known content-root path (`Data Source=angryfoot.db`).
- AI-list metadata (tags/skills/technologies/categories) stored as **JSON columns** (`string` property with EF value converter to/from `List<string>`) — simplest for MVP, avoids join tables. Future vector search hook noted in §12.

### 3.4 Frontend: Blazor Server (recommended) — justification

| Factor | Blazor Server | Blazor WASM |
|---|---|---|
| Time-to-MVP | **Fast** — direct DI, no separate client build/auth for API | Slower — CORS, serialization, larger surface |
| Local-first single user | **Ideal** — always low latency on localhost | No benefit; adds complexity |
| API access | Can call API via typed HttpClient **or** even in-process; simple | Must call REST over HTTP with CORS |
| Streaming AI responses | Easy via SignalR circuit + `StateHasChanged` | Requires extra plumbing |
| Payload/offline | N/A for local tool | Larger download, no server secrets |

**Decision:** Blazor **Server**. The scaffold already uses it. For a single-user local tool, the circuit model gives the simplest, fastest path and trivial live streaming of AI output. WASM's offline/scale benefits are irrelevant here. (WASM remains a future option; keep UI logic in services, not code-behind, to ease migration.)

### 3.5 AI client wiring

- Register `IChatClient` (from `Microsoft.Extensions.AI`) backed by Azure OpenAI (`Azure.AI.OpenAI`) in `ApiService/Program.cs`.
- Config via **user-secrets** (already referenced): `AzureOpenAI:Endpoint`, `AzureOpenAI:ApiKey` (or `OpenAI:ApiKey`), `AzureOpenAI:ChatDeployment` (e.g., `gpt-4o-mini`).
- Provide an `IChatClient` abstraction so tests can inject a **fake/stub** deterministic client (no network in unit tests).
- Use `ChatOptions` with `ResponseFormat = ChatResponseFormat.Json` where supported; parse with `System.Text.Json`.

---

## 4. Repository Structure

```
AngryFoot.slnx                         (existing)
aspire.config.json                     (existing)
docs/
  IMPLEMENTATION_PLAN.md               (this file)
  API.md                               (living API contract — owned by Backend agent)
AngryFoot.AppHost/                     (existing) orchestration
AngryFoot.ServiceDefaults/             (existing) telemetry/health/resilience
AngryFoot.Contracts/          NEW      shared DTOs/enums (netstandard/net10 classlib)
AngryFoot.ApiService/                  (existing) — expanded per §3.1
  Api/
  Application/
  Ai/
  Data/
  Domain/
AngryFoot.Web/                         (existing Blazor Server) — expanded
  Components/
    Pages/
      Bullets.razor
      BulletEdit.razor
      Profile.razor
      Generate.razor
      History.razor
    Layout/
  Services/
    ApiClient.cs                       typed HttpClient wrappers
AngryFoot.Tests/                       (existing) xUnit
  Ai/           (stage/contract tests with fake IChatClient)
  Api/          (integration tests via WebApplicationFactory)
  Data/         (repository/migration tests, SQLite in-memory)
```

**New projects to add & reference:**
- `AngryFoot.Contracts` (classlib). Referenced by `ApiService`, `Web`, `Tests`.
- Add to `AngryFoot.slnx`.

---

## 5. Database Schema

SQLite, EF Core code-first. List-type metadata persisted as JSON text columns.

```
Bullets
  Id              TEXT (GUID)  PK
  BulletText      TEXT         NOT NULL
  Tags            TEXT (json)  NOT NULL default '[]'
  Skills          TEXT (json)  NOT NULL default '[]'
  Technologies    TEXT (json)  NOT NULL default '[]'
  JobCategories   TEXT (json)  NOT NULL default '[]'   -- AI: likely job categories
  Impact          TEXT (json)  NOT NULL default '[]'   -- (rec) quantified metrics extracted
  EnrichmentState TEXT         NOT NULL default 'Pending' -- Pending|Enriched|Failed
  SourceEmployer  TEXT NULL    -- (rec) optional link to a WorkHistory employer name
  CreatedDate     TEXT (utc)   NOT NULL
  ModifiedDate    TEXT (utc)   NOT NULL

Profile                         -- single row (Id fixed = 1 or single GUID)
  Id                 TEXT PK
  Name               TEXT
  Email              TEXT
  Phone              TEXT
  LinkedIn           TEXT
  GitHub             TEXT
  ProfessionalSummary TEXT
  ModifiedDate       TEXT (utc)

WorkHistory
  Id           TEXT PK
  ProfileId    TEXT FK -> Profile.Id
  Employer     TEXT NOT NULL
  Title        TEXT
  Location     TEXT NULL
  StartDate    TEXT NULL
  EndDate      TEXT NULL          -- null = present
  SortOrder    INTEGER default 0

Education
  Id           TEXT PK
  ProfileId    TEXT FK -> Profile.Id
  Institution  TEXT
  Credential   TEXT               -- degree/program
  Field        TEXT NULL
  GraduationDate TEXT NULL
  SortOrder    INTEGER default 0

Certifications
  Id           TEXT PK
  ProfileId    TEXT FK -> Profile.Id
  Name         TEXT
  Issuer       TEXT NULL
  IssueDate    TEXT NULL
  SortOrder    INTEGER default 0

GenerationArtifacts
  Id                TEXT PK
  JobTitle          TEXT NULL
  Company           TEXT NULL
  JobDescription    TEXT NOT NULL   -- original, verbatim
  ResumeMarkdown    TEXT NOT NULL
  CoverLetterMarkdown TEXT NOT NULL
  SelectedBulletIds TEXT (json)     -- audit of which bullets were used
  JobAnalysisJson   TEXT (json)     -- cached analysis for transparency/debug
  CreatedDate       TEXT (utc) NOT NULL
```

**Indexes (MVP-light):** PK indexes suffice. Optional: index `Bullets.EnrichmentState`, `GenerationArtifacts.CreatedDate`. Full-text/vector search deferred (§12).

**Recommended extra fields (low complexity, high value):**
- `Bullets.Impact` — quantified metrics ("30%", "$1.2M") extracted by AI, boosts ranking & ATS.
- `Bullets.EnrichmentState` — lets UI show pending/failed enrichment and enables retry.
- `Bullets.SourceEmployer` — allows grouping bullets under the right employer in the resume.

---

## 6. Entity Definitions (C#)

Located in `AngryFoot.ApiService/Domain/`. Wire DTOs (in `AngryFoot.Contracts`) mirror these minus EF concerns.

```csharp
public class Bullet
{
    public Guid Id { get; set; }
    public string BulletText { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public List<string> Skills { get; set; } = [];
    public List<string> Technologies { get; set; } = [];
    public List<string> JobCategories { get; set; } = [];
    public List<string> Impact { get; set; } = [];
    public EnrichmentState EnrichmentState { get; set; } = EnrichmentState.Pending;
    public string? SourceEmployer { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime ModifiedDate { get; set; }
}

public enum EnrichmentState { Pending, Enriched, Failed }

public class Profile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string LinkedIn { get; set; } = "";
    public string GitHub { get; set; } = "";
    public string ProfessionalSummary { get; set; } = "";
    public List<WorkHistory> WorkHistory { get; set; } = [];
    public List<Education> Education { get; set; } = [];
    public List<Certification> Certifications { get; set; } = [];
    public DateTime ModifiedDate { get; set; }
}

public class WorkHistory {
    public Guid Id { get; set; } public Guid ProfileId { get; set; }
    public string Employer { get; set; } = ""; public string? Title { get; set; }
    public string? Location { get; set; }
    public string? StartDate { get; set; } public string? EndDate { get; set; }
    public int SortOrder { get; set; }
}
public class Education {
    public Guid Id { get; set; } public Guid ProfileId { get; set; }
    public string Institution { get; set; } = ""; public string? Credential { get; set; }
    public string? Field { get; set; } public string? GraduationDate { get; set; }
    public int SortOrder { get; set; }
}
public class Certification {
    public Guid Id { get; set; } public Guid ProfileId { get; set; }
    public string Name { get; set; } = ""; public string? Issuer { get; set; }
    public string? IssueDate { get; set; } public int SortOrder { get; set; }
}

public class GenerationArtifact {
    public Guid Id { get; set; }
    public string? JobTitle { get; set; }
    public string? Company { get; set; }
    public string JobDescription { get; set; } = "";
    public string ResumeMarkdown { get; set; } = "";
    public string CoverLetterMarkdown { get; set; } = "";
    public List<Guid> SelectedBulletIds { get; set; } = [];
    public string JobAnalysisJson { get; set; } = "{}";
    public DateTime CreatedDate { get; set; }
}
```

EF value converters map each `List<string>`/`List<Guid>` to a JSON `TEXT` column.

---

## 7. REST API Design

Base: `/api`. JSON. Conventional status codes. `ProblemDetails` for errors (already enabled).

### Bullets
| Method | Route | Body | Returns |
|---|---|---|---|
| GET | `/api/bullets?search=&tag=&skill=&tech=&category=` | — | `BulletDto[]` (filter/search) |
| GET | `/api/bullets/{id}` | — | `BulletDto` / 404 |
| POST | `/api/bullets` | `CreateBulletRequest { bulletText }` | `201 BulletDto` (enrichment kicked off) |
| PUT | `/api/bullets/{id}` | `UpdateBulletRequest { bulletText }` | `BulletDto` (re-enriched) |
| DELETE | `/api/bullets/{id}` | — | `204` |
| POST | `/api/bullets/{id}/enrich` | — | `BulletDto` (force re-enrich / retry) |

Search semantics (MVP): case-insensitive `LIKE` on `BulletText` + membership match on tags/skills/tech/category JSON (evaluate in-memory after load — dataset is small for a single user).

### Profile
| Method | Route | Body | Returns |
|---|---|---|---|
| GET | `/api/profile` | — | `ProfileDto` (creates empty if none) |
| PUT | `/api/profile` | `ProfileDto` | `ProfileDto` |

(Work history/education/certifications edited as nested collections within `ProfileDto` — simplest for single-user MVP.)

### Generation
| Method | Route | Body | Returns |
|---|---|---|---|
| POST | `/api/generations` | `GenerationRequest { jobDescription, jobTitle?, company?, maxBullets? }` | `GenerationResultDto` (resume + cover letter markdown + audit), persisted |
| POST | `/api/generations/analyze` | `{ jobDescription }` | `JobAnalysisDto` (optional preview stage) |

### Artifacts (history)
| Method | Route | Returns |
|---|---|---|
| GET | `/api/artifacts` | `ArtifactSummaryDto[]` (id, jobTitle, company, createdDate) |
| GET | `/api/artifacts/{id}` | `GenerationArtifactDto` (full) |
| DELETE | `/api/artifacts/{id}` | `204` |

### Key DTOs (in `AngryFoot.Contracts`)
```csharp
public record BulletDto(Guid Id, string BulletText, List<string> Tags,
    List<string> Skills, List<string> Technologies, List<string> JobCategories,
    List<string> Impact, string EnrichmentState,
    DateTime CreatedDate, DateTime ModifiedDate);

public record CreateBulletRequest(string BulletText);
public record UpdateBulletRequest(string BulletText);

public record GenerationRequest(string JobDescription, string? JobTitle,
    string? Company, int? MaxBullets);

public record GenerationResultDto(Guid ArtifactId, string ResumeMarkdown,
    string CoverLetterMarkdown, JobAnalysisDto Analysis, List<Guid> SelectedBulletIds);

public record JobAnalysisDto(List<string> RequiredSkills, List<string> PreferredSkills,
    List<string> Technologies, List<string> Keywords, List<string> ExperienceThemes,
    string? InferredTitle, string? InferredSeniority);
```

The Backend agent maintains `docs/API.md` as the authoritative contract; changes there are the only cross-team coordination needed.

---

## 8. AI Prompt Architecture

**Principle:** many small deterministic stages, each returning **strict JSON** (except the two Markdown-emitting stages). Every stage: system prompt fixes role + output schema; user prompt supplies data; `temperature` low (0–0.3) for extraction/ranking, moderate (0.4–0.6) for rewriting/generation. All parsing tolerant (extract JSON substring, validate, fall back).

Shared infra: `IChatClient` (Microsoft.Extensions.AI), `JsonStageRunner<TOut>` helper that: builds messages → requests JSON → deserializes → on failure retries once with a "return valid JSON only" nudge → on second failure returns a safe default and logs.

### Stage 1 — Bullet Tagger
- **Trigger:** bullet create/update.
- **Input:** single `BulletText`.
- **Output JSON:** `{ skills[], technologies[], tags[], jobCategories[], impact[] }`.
- **Prompt strategy:** "You are a resume analyst. Extract ONLY what is explicitly present. Do not invent. Return JSON matching schema." Provide 1–2 few-shot examples.
- **Failure handling:** retry once; on failure set `EnrichmentState=Failed`, keep bullet usable (empty metadata). UI offers retry.
- **Testing:** fake `IChatClient` returning canned JSON → assert mapping. One "malformed JSON" test asserts `Failed` state, no crash.

### Stage 2 — Job Analyzer
- **Input:** `jobDescription`.
- **Output:** `JobAnalysisDto` (required/preferred skills, technologies, keywords, experience themes, inferred title/seniority).
- **Strategy:** low temp, JSON schema, "extract, do not add skills not implied."
- **Failure:** retry once; fallback = keyword split heuristic (naive tokenization) so pipeline still proceeds.
- **Testing:** canned JD → deterministic fake; assert fields populated; malformed → heuristic fallback path covered.

### Stage 3 — Bullet Ranker
- **Input:** `JobAnalysis` + list of candidate bullets (id + text + skills/tech/tags).
- **Output JSON:** `[{ bulletId, score (0–100), reason }]`.
- **Strategy:** Ask model to score relevance to required/preferred skills & themes. To bound tokens/cost, **pre-filter** in code first (cheap overlap score on skills/tech/keywords) to top N (e.g., 25), then send those to the model for precise ranking. Select top `MaxBullets` (default 8–12).
- **Failure:** if AI ranking fails, fall back to the deterministic overlap score (fully offline-capable). Guarantees generation always succeeds.
- **Testing:** overlap pre-filter unit-tested deterministically; AI ranking tested with fake client; fallback path tested.

### Stage 4 — Bullet Rewriter
- **Input:** selected bullet texts + `JobAnalysis` (target keywords/tone).
- **Output JSON:** `[{ bulletId, rewritten }]`.
- **Strategy (critical guardrails):** system prompt: "Rewrite for clarity, impact, and ATS keyword alignment. PRESERVE ALL FACTS. Do NOT invent metrics, technologies, employers, or achievements. If a target keyword is not supported by the original, do not add it. Keep each bullet one sentence, action-verb first." Provide the original alongside each rewrite for traceability.
- **Failure:** on failure or empty output for a bullet, **fall back to the original bullet text** (never drop content). Optional lightweight validation: flag bullets whose rewrite introduces a number/tech token absent from the original.
- **Testing:** fake client; assert every input bullet has an output (fallback to original when missing). Fabrication-guard unit test on the validator.

### Stage 5 — Resume Generator
- **Input:** `ProfileDto` + rewritten bullets (grouped by employer via `SourceEmployer`/best-effort) + selected skills.
- **Output:** **Markdown** (the fixed layout in the spec).
- **Strategy:** Prefer **deterministic templating in C#** for structure (headings, contact line, employer sections) and only optionally use the model to polish the Professional Summary + Skills ordering. This makes output reliable and testable, and saves tokens. (A single-prompt "write the whole resume" is discouraged — harder to keep factual/consistent.)
- **Failure:** template path has no AI dependency → always produces valid Markdown. AI summary polish is best-effort; fallback to `Profile.ProfessionalSummary`.
- **Testing:** golden-file/snapshot test: given fixed profile + bullets, assert Markdown contains required sections & bullets. No AI needed for core assertion.

### Stage 6 — Cover Letter Generator
- **Input:** `jobDescription`, `ProfileDto` (name/summary/contact), top rewritten bullets, `JobAnalysis` (tone/terminology).
- **Output:** **Markdown** cover letter (concise: greeting, 3 short paragraphs, sign-off).
- **Strategy:** moderate temp; "Use only experience present in the provided profile/bullets. Do not fabricate. Mirror the role's terminology. 250–350 words." Provide structural scaffold to keep it tight.
- **Failure:** retry once; fallback to a minimal template letter assembled from profile + top 3 bullets.
- **Testing:** fake client returns canned letter → assert persisted & non-empty; fallback template unit-tested.

**Cross-cutting AI testing approach:** a single `FakeChatClient` in the Tests project, configured per test to return scripted responses keyed by stage, enables **fully offline, deterministic** CI. A small number of **manual/integration** smoke tests (skippable via env var `RUN_AI_INTEGRATION=1`) hit the real endpoint.

---

## 9. Agent / Team Decomposition

Six workstreams. **A0 and A1 are the coordination spine and should land first** (they define the contracts everyone else compiles against). After that, A2–A5 proceed largely in parallel with minimal conflict because each owns distinct folders/projects.

### A0 — Foundations & Contracts (unblocks everyone; ~1h)
- **Responsibilities:** Create `AngryFoot.Contracts` project + all DTOs/enums; add to solution; wire references (`ApiService`, `Web`, `Tests`). Register `IChatClient` + `AngryFootDbContext` + EF SQLite in `ApiService/Program.cs`; add initial migration + startup `Migrate()`. Seed empty Profile. Add `FakeChatClient` + `IChatClient` abstraction. Create `docs/API.md` skeleton.
- **Deliverables:** compiling contracts lib, DbContext, migration, DI wiring, fake AI client.
- **Interfaces:** DTOs (§7), `IChatClient`, `AngryFootDbContext`.
- **Dependencies:** none.
- **Effort:** ~1h. **AC:** solution builds; `dotnet ef migrations` applied; empty DB created on run; `GET /api/profile` returns empty profile.

### A1 — Backend/API & Persistence (~2.5h)
- **Responsibilities:** Entities, EF configs/converters, repositories/`Application` services for Bullets, Profile, Artifacts; all REST endpoints in §7 except the AI internals (calls into A_AI services via interfaces). Owns `docs/API.md`.
- **Deliverables:** Working CRUD + search + artifacts endpoints; integration tests via `WebApplicationFactory` + SQLite in-memory.
- **Interfaces consumed:** `IBulletTagger`, `IGenerationOrchestrator` (from A2) — depend on **interfaces**, not implementations (dependency inversion enables parallel work with stubs).
- **Dependencies:** A0. **Effort:** ~2.5h. **AC:** all Bullets/Profile/Artifacts endpoints pass integration tests with a fake AI service.

### A2 — AI Pipeline (~2.5h)
- **Responsibilities:** Implement all six stages (§8) + `JsonStageRunner` + prompts; `IGenerationOrchestrator` that chains analyze→rank→rewrite→resume→cover and returns `GenerationResultDto`; deterministic fallbacks.
- **Deliverables:** `Ai/` folder complete; `POST /api/generations` and `/analyze` functional; unit tests with `FakeChatClient`.
- **Interfaces exposed:** `IBulletTagger`, `IJobAnalyzer`, `IBulletRanker`, `IBulletRewriter`, `IResumeGenerator`, `ICoverLetterGenerator`, `IGenerationOrchestrator` (defined in A0/agreed early).
- **Dependencies:** A0 (contracts, `IChatClient`). Coordinates interface signatures with A1 up front, then independent. **Effort:** ~2.5h. **AC:** given canned AI responses, orchestrator produces valid resume+cover Markdown; all fallbacks covered by tests.

### A3 — Bullet & Profile Frontend (~2h)
- **Responsibilities:** Blazor pages `Bullets.razor`, `BulletEdit.razor`, `Profile.razor`; `ApiClient` methods for bullets & profile; search/filter UI; show enrichment state + retry.
- **Deliverables:** functional CRUD/search UI wired to API.
- **Dependencies:** A0 (DTOs), A1 (endpoints). Can start against stubbed API using DTO shapes immediately. **Effort:** ~2h. **AC:** create/edit/delete/search bullets and edit profile end-to-end.

### A4 — Generation & History Frontend (~2h)
- **Responsibilities:** `Generate.razor` (paste JD → title/company → generate → render resume & cover-letter Markdown side-by-side with copy buttons), `History.razor` (list + view past artifacts).
- **Deliverables:** generation UI + history UI; Markdown rendered (e.g., `Markdig` to HTML for preview) with raw copy.
- **Dependencies:** A0 (DTOs), A1 (artifacts), A2 (generation endpoint). **Effort:** ~2h. **AC:** paste JD → view + copy resume/cover letter; revisit from history.

### A5 — Testing, Aspire Wiring & Docs (~1h, overlaps)
- **Responsibilities:** Ensure AppHost runs API+Web together; health checks; end-to-end smoke test; README quickstart (user-secrets setup, run instructions); optional real-AI integration test gate.
- **Dependencies:** touches everything late. **Effort:** ~1h. **AC:** `dotnet run` on AppHost brings up both; happy-path smoke test green; README lets a new dev run in <5 min.

**Merge-conflict avoidance:** each agent owns disjoint folders/files. Shared touch-points are `ApiService/Program.cs` (DI registration — keep additions in clearly separated regions or partial `Program` extension methods) and `docs/API.md` (Backend-owned). The `AngryFoot.Contracts` project is frozen early by A0 to minimize churn.

---

## 10. Sprint Plan (one 8-hour day)

| Time | A0/A1 Backend | A2 AI | A3 Bullets/Profile UI | A4 Gen/History UI |
|---|---|---|---|---|
| 0:00–1:00 | **A0 foundations** (contracts, DbContext, DI, fake AI) — everyone waits/reads | agree interfaces w/ backend | scaffold pages vs DTOs | scaffold pages vs DTOs |
| 1:00–3:00 | Bullet/Profile/Artifact services + endpoints + tests | implement stages 1–3 + runner | Bullets CRUD + search wired | Generate page skeleton |
| 3:00–5:00 | Artifacts + wire AI interfaces | stages 4–6 + orchestrator + `/generations` | Profile page + enrichment UI | render markdown + copy |
| 5:00–6:30 | integration tests green | fallbacks + unit tests | polish/validation | History page wired |
| 6:30–8:00 | **Integration / A5:** Aspire run, E2E smoke, bugfix, README, real-AI smoke | | | |

Milestones: **M1 (1:00)** contracts frozen & solution builds. **M2 (3:00)** bullets CRUD live end-to-end. **M3 (5:00)** first full resume+cover generated. **M4 (8:00)** demo-ready MVP.

---

## 11. Development Sequence (critical path)

1. **A0 foundations** (blocks all) — contracts, DbContext, migration, DI, FakeChatClient.
2. **Interfaces agreed** between A1 & A2 (AI service interfaces) — enables parallel stubbed dev.
3. **A1 Bullets/Profile** + **A2 stages 1–3** in parallel.
4. **A3 UI** starts as soon as bullet/profile endpoints exist (or stubbed).
5. **A2 orchestrator + `/generations`** → unblocks **A4 generation UI**.
6. **Artifacts endpoint** → **A4 history**.
7. **A5 integration**, Aspire run, smoke tests, README.

Critical path: A0 → AI orchestrator (A2) → Generation UI (A4). Keep A2 unblocked; give it priority on any early questions.

---

## 12. Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| AI returns malformed/invalid JSON | Pipeline breaks | `JsonStageRunner` tolerant parse + 1 retry + safe default; `ResponseFormat=Json`; low temp |
| AI fabricates facts in rewrites | Wrong/dishonest resume | Strong guardrail prompts; keep original alongside; fallback to original text; optional fabrication validator flags new numbers/tech |
| AI latency/cost on ranking (many bullets) | Slow/expensive | Deterministic pre-filter to top N before AI ranking; small model (gpt-4o-mini) |
| AI provider outage / no key configured | Blocks generation | Deterministic fallbacks at every stage (overlap ranking, template resume/cover) → app works offline, degraded quality |
| Missing/invalid API key config | Startup confusion | README user-secrets steps; clear startup log warning if key absent; fake client for dev/tests |
| Contract churn between agents | Merge conflicts/rework | Freeze `AngryFoot.Contracts` at M1; single `docs/API.md` owner; program DI split into extension methods |
| EF JSON-column querying limits | Search misbehaves | Load small single-user dataset & filter in memory for MVP; revisit with FTS later |
| SQLite file locking (concurrent circuits) | Rare write errors | Single-user; scoped DbContext per request; keep transactions short |
| Scope creep (DOCX/PDF/templates) | Miss 8h target | Explicitly deferred (§ Future); enforce Definition of Done |
| Blazor Server long AI call blocks UI | Perceived hang | Async calls + loading state + optional streaming; disable button while running |

---

## 13. Definition of Done (MVP)

**Functional**
- [ ] Create/read/edit/delete/search/filter bullets via UI; each create/update triggers AI enrichment (skills, technologies, tags, job categories, impact) stored as structured metadata; failed enrichment is visible and retryable.
- [ ] Single Profile (name, email, phone, LinkedIn, GitHub, summary, work history, education, certifications) editable and persisted.
- [ ] Paste a job description → system analyzes it, ranks & selects relevant bullets, rewrites them with factual-accuracy guardrails, and produces a **Resume (Markdown)** in the specified layout **and** a **Cover Letter (Markdown)**.
- [ ] Every generation is saved with job title, company, original JD, both artifacts, and bullet audit; past generations viewable in History.

**Technical**
- [ ] `dotnet run` on `AngryFoot.AppHost` starts API + Blazor Web together with healthy health checks.
- [ ] SQLite DB auto-created/migrated on startup.
- [ ] AI accessed via `IChatClient`; a `FakeChatClient` powers deterministic offline tests.
- [ ] Every AI stage has a deterministic fallback so generation never hard-fails.
- [ ] `AngryFoot.Contracts` is the single shared DTO source referenced by API and Web.

**Quality**
- [ ] Unit tests: each AI stage (mapping + malformed-input fallback), ranking pre-filter, resume snapshot.
- [ ] Integration tests: Bullets/Profile/Artifacts endpoints via `WebApplicationFactory` + in-memory SQLite.
- [ ] One end-to-end happy-path smoke test (fake AI): JD → resume + cover letter persisted.
- [ ] README documents user-secrets AI config and run steps; new dev productive in <5 min.
- [ ] No secrets committed; key via user-secrets/env only.

**Explicitly out of scope (future hooks, not MVP):** DOCX/PDF export, resume templates, multiple profiles, vector/embedding search, job-application tracking, dynamic resume structures. Code is structured (interfaces, service layer, JSON metadata, `IChatClient`) so these can be added without rework.
