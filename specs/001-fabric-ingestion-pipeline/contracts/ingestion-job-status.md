# API Contract: Ingestion Job Status (Coverage)

This contract supports FR-010 and FR-010a: job status/progress plus manual extraction coverage reporting.

## Endpoint

`GET /api/ingestion/jobs/{jobId}`

## Authorization

- Admin/operator only.

## Response

`200 OK`

```json
{
  "jobId": "00000000-0000-0000-0000-000000000000",
  "status": "queued",
  "createdAtUtc": "2026-02-08T00:00:00Z",
  "startedAtUtc": null,
  "completedAtUtc": null,
  "inputType": "manual-pdf",
  "manualDocumentId": "00000000-0000-0000-0000-000000000000",
  "totalPages": 1200,
  "pagesCapturedViewableCount": 1188,
  "pagesWithSearchableTextCount": 1130,
  "pagesWithOcrTextCount": 420,
  "pagesWithNativeTextCount": 710,
  "missingPages": [42, 43],
  "coverage": {
    "viewablePagesPercent": 99.0,
    "searchableTextPagesPercent": 94.17,
    "missingPagesCount": 2
  },
  "workloadLimits": {
    "maxPages": 2000,
    "maxInputBytes": 2000000000,
    "maxRuntimeMinutes": 360
  },
  "message": "running",
  "failureReason": null
}
```

## Errors

- `401 Unauthorized`
- `403 Forbidden`
- `404 Not Found`
