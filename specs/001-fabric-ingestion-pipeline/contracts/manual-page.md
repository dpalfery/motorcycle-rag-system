# API Contract: Manual Page Retrieval

This contract supports FR-005b and FR-005c: users can request a specific manual page and receive that page, restricted to authenticated end users with a manuals entitlement.

## Endpoint

`GET /api/manuals/{manualId}/pages/{pageNumber}`

## Authorization

- Requires authentication.
- Requires manuals entitlement (single shared entitlement that applies to all manuals).

Recommended policy name: `mcr-api-manuals-view`.

## Path Parameters

- `manualId`: GUID
- `pageNumber`: int (1-based)

## Response

Two acceptable response modes (implementation choice):

### Mode A: Stream bytes (recommended for simplicity)

- `200 OK`
- `Content-Type: image/png` (or `image/jpeg`)
- Body: raw image bytes for the page

Response headers (recommended):

- `Cache-Control: private, max-age=600` (or `no-store` if you prefer stricter privacy)
- `ETag`: stable per (manualId, pageNumber, assetVersion)
- `Content-Disposition: inline; filename="{manualId}-p{pageNumber}.png"`

### Mode B: Return a short-lived signed URL

- `200 OK`
- `Content-Type: application/json`

```json
{
  "manualId": "00000000-0000-0000-0000-000000000000",
  "pageNumber": 1,
  "contentType": "image/png",
  "expiresAtUtc": "2026-02-08T00:00:00Z",
  "url": "https://..."
}
```

## Errors

- `400 Bad Request`: invalid page number (<= 0) or invalid ids
- `401 Unauthorized`: not authenticated
- `403 Forbidden`: no entitlement
- `404 Not Found`: manual or page does not exist

## Notes

- Page numbering is logical/document order (1-based). If the PDF contains non-content pages (covers, inserts), those still count as pages.
- The API MUST NOT reveal whether a manual/page exists to unauthorized callers beyond standard `401/403` behavior.
