# API Contract (MVP)

Base route: `/api`

## Profile

### GET `/api/profile`
Returns the single-user profile. If no profile exists, an empty seeded profile is returned.

### PUT `/api/profile`
Upserts profile details and nested `workHistory`, `education`, and `certifications` collections.

Request/response shape (`ProfileDto`):
```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "name": "",
  "email": "",
  "phone": "",
  "linkedIn": "",
  "gitHub": "",
  "professionalSummary": "",
  "workHistory": [
    {
      "id": "00000000-0000-0000-0000-000000000000",
      "employer": "",
      "title": "",
      "location": "",
      "startDate": "",
      "endDate": "",
      "sortOrder": 0
    }
  ],
  "education": [],
  "certifications": [],
  "modifiedDate": "2026-01-01T00:00:00Z"
}
```

## Bullets

### GET `/api/bullets?search=&tag=&skill=&technology=&category=`
Returns all bullets, filtered by optional query params.

### GET `/api/bullets/{id}`
Returns a single bullet (`200`) or `404`.

### POST `/api/bullets`
Creates a bullet and enriches metadata (currently via pluggable tagger abstraction).

Request:
```json
{ "bulletText": "Reduced widget waste by 30% by redesigning the assembly process." }
```

An optional `tagging` from a prior `/api/bullets/assess` of the same text is used instead of running
enrichment again; see that endpoint.

Returns `201 Created` with `BulletDto` and `Location: /api/bullets/{id}`.

### PUT `/api/bullets/{id}`
Updates bullet text and re-enriches metadata. Returns `200` with `BulletDto` or `404`.

### DELETE `/api/bullets/{id}`
Deletes bullet. Returns `204` or `404`.

### POST `/api/bullets/{id}/enrich`
Force re-enriches metadata for an existing bullet. Returns `200` with `BulletDto` or `404`.

### POST `/api/bullets/rewrite`
Suggests an improved rewrite without saving anything. Nothing is persisted.

Request:
```json
{ "bulletText": "did the migration", "deepReview": false }
```

With `deepReview: true` the draft is run through the critique-and-revise pass start to finish (three
extra AI calls) and the response carries a `refinement` block. `rewrittenText` is always the
recommended version, so callers that ignore `refinement` still benefit.

Response (`RewriteBulletResponse`):
```json
{
  "rewrittenText": "Delivered the platform migration.",
  "suggestions": ["Add a measurable result"],
  "refinement": {
    "recommendedLabel": "synthesis",
    "critique": "No metric, and 'the migration' is vague.",
    "versions": [
      { "label": "v1", "title": "Initial draft", "rationale": "...", "text": "..." },
      { "label": "v2", "title": "Reviewer's alternative", "rationale": "...", "text": "..." },
      { "label": "v1a", "title": "Author's revision", "rationale": "...", "text": "..." },
      { "label": "synthesis", "title": "Synthesis", "rationale": "...", "text": "..." }
    ]
  }
}
```

`refinement` is `null` when deep review was not requested, or when the rewrite fell back to
heuristics and there was no AI draft to critique.

### POST `/api/bullets/rewrite/critique`
Phase one of a **guided** deep review: drafts the rewrite, has it reviewed, and stops so the user
can correct anything the reviewer misread. Same request shape as `/api/bullets/rewrite`.

Returns `200` with `BulletRewriteCritiqueResponse`, or `204 No Content` when there was no AI draft
to critique (the caller should fall back to `/api/bullets/rewrite`).

```json
{
  "originalText": "did the migration",
  "draft": "Delivered the platform migration.",
  "critique": "No metric, and 'the migration' is vague.",
  "alternative": "Migrated the billing platform to Azure.",
  "suggestions": ["Add a measurable result"]
}
```

### POST `/api/bullets/rewrite/complete`
Phase two: runs the revision and synthesis stages over a phase-one result. The whole phase-one
payload round-trips through the client, so the server holds no state between the two calls.

Request (`CompleteBulletRewriteRequest`) is the phase-one response plus `guidance`:
```json
{
  "originalText": "did the migration",
  "draft": "Delivered the platform migration.",
  "critique": "No metric, and 'the migration' is vague.",
  "alternative": "Migrated the billing platform to Azure.",
  "suggestions": ["Add a measurable result"],
  "guidance": "\"the migration\" was the billing platform, and I led it solo."
}
```

`guidance` is optional and is treated as fact by the remaining agents, outranking the critique.
Returns `200` with `RewriteBulletResponse`. `400` if `draft` or `critique` is missing.

`BulletDto` shape:
```json
{
  "id": "00000000-0000-0000-0000-000000000000",
  "bulletText": "",
  "tags": [],
  "skills": [],
  "technologies": [],
  "jobCategories": [],
  "impact": [],
  "sourceEmployer": null,
  "enrichmentState": "Enriched",
  "createdDate": "2026-01-01T00:00:00Z",
  "modifiedDate": "2026-01-01T00:00:00Z"
}
```

### Bullet versions

A version is an alternative wording of a bullet, kept alongside it. Creating one never modifies the
bullet; only the promote call does.

`BulletDto` and `BulletRevisionDto` both carry a `quality` object: `score`, a `signals[]` array,
`wordCount`, and `diagnostics[]` (the same shape evidence coverage uses, Why panel included).
`score` always equals the sum of the weights of the earned signals.

Each signal is `{ name, label, earned, weight, detail, isDeclared, isContestable }`. `detail` is what
the check saw, quoting the bullet where it can — `States "40%"`, `Shared credit: "we"`,
`Not enriched yet`.

`isContestable` is true only for `ownership`, the one signal the text cannot settle: a resume elides
its subject, so the check presumes the author and looks only for wording that gives credit away.
`isDeclared` marks a signal earned because the author disputed the check rather than because the
wording showed it — a disputed signal scores and stops producing a diagnostic. It is intended for
bullets whose collaboration is a fact of the work, not a hedge in the sentence.

#### POST `/api/bullets/assess`
Scores wording that has not been saved. Persists nothing.

```json
{ "bulletText": "Cut Azure spend by 30%.", "acknowledgedSignals": ["ownership"] }
```

Returns `BulletAssessmentDto` — `quality`, plus the `tagging` the assessment used
(`{ forText, tags, skills, technologies, jobCategories, impact }`). Two of the six signals come from
enrichment rather than the text, so this runs the tagger; passing `tagging` back on the create or
update that follows skips a second call. The API re-tags from scratch if `forText` no longer matches
the text being saved.

#### PUT `/api/bullets/{id}/quality-acknowledgements`
Records which quality signals the author has disputed. `{ "signals": ["ownership"] }` — the full set,
so an empty array hands them all back to the check. Returns the updated `BulletDto`, or `404`. Signals that are
not contestable are stored but have no effect on the score.

#### GET `/api/bullets/{id}/revisions`
Returns the bullet's versions (`200`), or `404` when the bullet does not exist. An existing bullet
with no versions returns `[]`.

#### POST `/api/bullets/{id}/revisions`
Writes a new version. `201` with the created `BulletRevisionDto`, `404` for an unknown bullet, `400`
for a mode outside the enum.

```json
{
  "mode": "Ats",
  "deepReview": false,
  "guidance": "the platform was internal, not a product"
}
```

`mode` is one of `Grammar`, `StrongerWording`, `Star`, `Executive`, `Technical`, `Ats`. `guidance` is
the candidate's own clarification and is treated as fact by the writer. `deepReview` runs the
critique-and-revise pass over the draft.

The response carries `sourceText` (the wording it was written from), `version` (numbered per mode),
`rationale` (one sentence on what changed, null when the heuristic fallback wrote it),
`isAiGenerated`, and `isStale` (the bullet has changed since, so this rewords text that no longer
exists — a version that has itself been promoted is not stale).

#### POST `/api/bullets/{id}/revisions/{revisionId}/promote`
Makes the version the bullet's canonical text and returns `PromoteBulletRevisionResponse` —
`bullet` plus the refreshed `revisions`. `404` when either id is unknown. The replaced wording is not
lost: it is the promoted version's own `sourceText`. Promotion writes through the same path as an
ordinary edit, so the bullet is re-tagged and re-indexed.

#### DELETE `/api/bullets/{id}/revisions/{revisionId}`
`204`, or `404` when the version does not belong to that bullet. Deleting a bullet deletes its
versions.

## Generations

### POST `/api/generations/analyze`
Analyzes a job description and returns structured metadata used for ranking and tailoring, the
evidence coverage report for the bullet library, and the occupational benchmark.

Request:
```json
{
  "jobDescription": "Senior .NET Engineer role requiring C#, ASP.NET Core, and Azure.",
  "jobTitle": "Senior Software Engineer"
}
```

`jobTitle` is optional. It maps the role to an occupation for the benchmark; when omitted, the
title inferred from the job description is used instead.

Response: `JobEvidenceAnalysisDto` — `job` (`JobAnalysisDto`), `coverage`
(`EvidenceCoverageReportDto`), and `benchmark` (`OccupationBenchmarkDto`, nullable).

`coverage` is always present. It reports how much of the posting's stated requirements the bullet
library evidences — a measure of the resume, not of the candidate — and every part of it is
traceable:

- `coverageScore` is derived, not asserted. It is always
  `round(100 * earnedWeight / totalWeight)`, and both operands are on the payload so a client can
  check the arithmetic. Each requirement contributes `weight * 2` to `totalWeight` and earns
  `weight *` 2 for `"Strong"` evidence, 1 for `"Weak"`, 0 for `"Missing"`.
- `requirements[]` links each extracted requirement to the bullets cited as evidence for it, under
  `why.supportingEvidence`. A citation's `isExactTermMatch: false` means an AI reviewer read the
  bullet as related without the bullet naming the requirement.
- `diagnostics[]` carries severities `"Warning"`, `"Suggestion"`, and `"Info"`, and codes
  `missing-skill`, `weak-evidence`, `duplicate-bullet`, `bullet-ordering`, `overused-wording`,
  `no-measurable-impact`, `unsupported-claim`, and `analysis-limitation`.
- Every requirement and every diagnostic carries a `why` object (the requirement at stake, the
  supporting evidence, what evidence is missing, and the reasoning).
- `source` is `"Deterministic"` or `"AiReviewed"`. With no AI configured the report is complete and
  `source` says so; an AI review may adjust per-requirement strengths but never returns a score, and
  may raise a strength by at most one step and only while citing a bullet from the library.

```json
{
  "coverageScore": 50,
  "earnedWeight": 6,
  "totalWeight": 12,
  "summary": "Of 3 extracted requirements, 1 is evidenced by a bullet that shows a result ...",
  "strongCount": 1,
  "weakCount": 1,
  "missingCount": 1,
  "requirements": [
    {
      "requirement": "Azure",
      "kind": "Technology",
      "weight": 2,
      "strength": "Missing",
      "why": {
        "requirement": "Azure",
        "supportingEvidence": [],
        "missingEvidence": ["A bullet describing hands-on Azure work and what it achieved."],
        "reasoning": "This posting names \"Azure\" among its technologies. No bullet in your library mentions it."
      }
    }
  ],
  "diagnostics": [
    {
      "severity": "Warning",
      "code": "missing-skill",
      "message": "\"Azure\" is named among the technologies in this posting, but no bullet in your library mentions it.",
      "why": { "requirement": "Azure", "supportingEvidence": [], "missingEvidence": ["..."], "reasoning": "..." },
      "bulletIds": []
    }
  ],
  "source": "Deterministic",
  "disclaimer": "Evidence coverage measures how much of this posting's stated requirements ..."
}
```

`benchmark` compares the bullet library against aggregate O\*NET occupational data for the mapped
occupation. It is null only when the bundled dataset could not be loaded. `matchConfidence` is
`"Exact"`, `"Fuzzy"`, or `"None"`; on `"None"` the title mapped to no occupation, `socCode` and
`occupationTitle` are null, and `summary` explains why.

```json
{
  "socCode": "15-1252.00",
  "occupationTitle": "Software Developers",
  "matchConfidence": "Exact",
  "matchedOn": "Software Engineer",
  "coverageScore": 46,
  "summary": "Your bullets evidence 9 of the 28 requirements typical of this occupation ...",
  "covered": [{ "name": "Programming", "kind": "Skill", "importance": 75 }],
  "missing": [{ "name": "Troubleshooting", "kind": "Skill", "importance": 60 }],
  "sourceAttribution": "This product uses information from O*NET OnLine ..."
}
```

### POST `/api/generations`
Runs the A2 pipeline: analyze job description, rank bullets, rewrite selected bullets, generate resume markdown, generate cover letter markdown, and persist a generation artifact.

Request:
```json
{
  "jobDescription": "Senior .NET Engineer role requiring C#, ASP.NET Core, and Azure.",
  "jobTitle": "Senior .NET Engineer",
  "company": "Contoso",
  "maxBullets": 8,
  "deepReview": false,
  "guidance": "\"systems\" in my bullets means HVAC controls, not software."
}
```

`guidance` is optional and reaches every AI stage including the first draft, so it applies with or
without `deepReview`.

`deepReview` adds the critique-and-revise pass over both the bullet set and the cover letter — six
extra AI calls, roughly 3.6x the wall clock (see the README's
[Deep review](../README.md#deep-review-critique-and-revise) section). It also lets the refinement
stages reorder bullets and swap in runner-up bullets the ranker did not select.

Response: `GenerationResultDto`. With `deepReview`, `resumeRefinement` and `coverLetterRefinement`
carry the labelled versions (same shape as `refinement` above; each resume version's `text` is a
whole rendered resume). `resumeMarkdown` and `coverLetterMarkdown` are always the recommended
versions, and are what the persisted artifact holds.

`coverage` (`EvidenceCoverageReportDto`, same shape as `/analyze`) reports on the bullets that made
it into this resume, in the order the resume prints them — so unlike `/analyze` it can diagnose
`bullet-ordering`. Its `source` is always `"Deterministic"`: a generation already chains several AI
calls, and this reports on a decision rather than making one.

`explanation` (`GenerationExplanationDto`) accounts for **every** candidate the ranker produced,
including the ones left off. Each `decisions[]` entry carries `bulletId`, `originalText`,
`finalText` (null when omitted), `kind`, `rankerPosition`, `resumePosition` (null when omitted), and
a `why` object of the same shape used everywhere else.

`kind` is a **combinable flag set**, serialized as comma-separated names — a bullet can be both moved
and reworded, and saying so beats picking whichever single label looked most notable:

| Value | Meaning |
|---|---|
| `"Omitted"` | Left off this resume. Never combined with anything else. |
| `"Selected"` | Kept, in the ranker's position, in the candidate's words. |
| `"Selected, Revised"` | Kept in place, reworded for the posting. |
| `"Selected, Reordered"` | Moved from the ranker's position, wording untouched. |
| `"Selected, Revised, Reordered"` | Both. |

Exactly one of `Selected` and `Omitted` is always present. Decisions are ordered by
`resumePosition`, with the omitted ones last.

> Note: `kind` is the only enum in these contracts sent as names. The rest — `enrichmentState`,
> `strength`, `severity`, `requirementKind`, `source`, `mode` — serialize as **integers** under the
> default web JSON options. Examples elsewhere in this document that show them as strings are
> inaccurate on that point.

For an omitted bullet that speaks to a requirement the resume does not fully evidence,
`why.missingEvidence` says what leaving it off cost. Like `coverage`, this is computed
deterministically and needs no AI call.

## Artifacts

### GET `/api/artifacts`
Returns generation artifact summaries in descending `createdDate` order.

### GET `/api/artifacts/{id}`
Returns a full generation artifact (`200`) or `404`. Artifacts generated with `deepReview` also
carry `resumeRefinement` and `coverLetterRefinement`, so the version picker still works when the
generation is reopened from history.

`coverage` is the evidence coverage report as of the moment the resume was generated, frozen rather
than recomputed — the library moves on, and a report that re-scored itself against later bullets
would no longer explain the resume beside it. Null for artifacts generated before it was recorded.

`explanation` is frozen for the same reason: it describes decisions taken over a candidate set that
has since changed. Null for artifacts generated before it was recorded.

### PUT `/api/artifacts/{id}/selection`
Promotes a stored deep-review version to be the artifact's resume and/or cover letter, so history
keeps the version the user chose rather than the one the arbiter recommended.

```json
{ "resumeVersionLabel": "v1a", "coverLetterVersionLabel": null }
```

A null or omitted label leaves that document alone; labels match case-insensitively. Returns `200`
with the updated `GenerationArtifactDto`, `404` if the artifact does not exist, or `400` if a label
is not among that artifact's stored versions — in which case nothing is changed at all, including
the other document.

### DELETE `/api/artifacts/{id}`
Deletes a generation artifact. Returns `204` or `404`.
