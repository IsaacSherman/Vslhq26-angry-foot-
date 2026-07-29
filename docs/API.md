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

Returns `201 Created` with `BulletDto` and `Location: /api/bullets/{id}`.

### PUT `/api/bullets/{id}`
Updates bullet text and re-enriches metadata. Returns `200` with `BulletDto` or `404`.

### DELETE `/api/bullets/{id}`
Deletes bullet. Returns `204` or `404`.

### POST `/api/bullets/{id}/enrich`
Force re-enriches metadata for an existing bullet. Returns `200` with `BulletDto` or `404`.

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

## Artifacts

### GET `/api/artifacts`
Returns generation artifact summaries in descending `createdDate` order.

### GET `/api/artifacts/{id}`
Returns a full generation artifact (`200`) or `404`.

### DELETE `/api/artifacts/{id}`
Deletes a generation artifact. Returns `204` or `404`.
