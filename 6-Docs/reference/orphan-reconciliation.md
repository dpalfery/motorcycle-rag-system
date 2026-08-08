---
id: reference/orphan-reconciliation
title: Orphan Reconciliation System
doc-type: reference
status: current
component: MotorcycleRAG Ingestion
owner: API maintainers
last-reviewed: 2026-08-03
code-refs:
  - ProcessorArtifactService
  - OrphanedArtifactSweepService
  - OrphanedArtifactSweepBackgroundService
  - SearchChunkIndexingCoordinator
  - IngestionJobsController
api-endpoints:
  - GET /api/ingestion/artifacts/orphaned
  - POST /api/ingestion/artifacts/orphaned/sweep
  - POST /api/ingestion/artifacts/orphaned/{uploadId}/adopt
decided-by:
  - plans/2026-08-03-processor-artifact-skip-observability
supersedes: []
---
# Orphan Reconciliation System

## Overview

The orphan reconciliation system handles search-chunks artifacts that are stored but not indexed due to missing ingestion jobs. When the local processor uploads search-chunks and the correlated ingestion job does not exist, the artifact is marked as orphaned with durable blob metadata and tracked for reconciliation.

The system reconciles orphans through automatic periodic sweeps and on-demand admin operations, healing race conditions (job committed after artifact upload) while routing permanent failures to a terminal state an operator can see and resolve.

## Concepts

### Orphan States

Artifacts in blob metadata can carry one of three ingestion-related states:

| State | Meaning | Recoverable |
|-------|---------|------------|
| `(no metadata)` | Artifact has not been processed yet or is being processed | N/A |
| `Orphaned` | Stored but not indexed due to missing ingestion job; reconciliation in progress | Yes—if job eventually appears or operator resolves via adopt |
| `OrphanedTerminal` | Stored but not indexed; retry limit (attempts or retention window) exceeded | Yes—only via operator adopt action |

### Metadata Keys

Each orphan blob carries the following metadata keys:

| Key | Type | Example | Purpose |
|-----|------|---------|---------|
| `state` | string | `"Orphaned"` or `"OrphanedTerminal"` | Current orphan state |
| `orphanReason` | string | `"NoIngestionJob"` | Root cause (currently always this value) |
| `orphanAttempts` | string | `"0"` | Number of reconciliation attempts for this orphan |
| `orphanFirstDetectedUtc` | string (ISO 8601) | `"2026-08-03T14:22:35.1234567Z"` | When the orphan was first detected |
| `dateLastProcessed` | string (ISO 8601) | `"2026-08-03T14:25:10.5678901Z"` | When the orphan was last touched by a sweep cycle |

## Reconciliation Flow

### Detection (Upload Time)

When `ProcessorArtifactService.UploadArtifactAsync` processes a search-chunks upload:

1. Resolves the ingestion job for the upload via `FindLatestSearchChunkJobAsync`.
2. If no job is found, returns `ProcessorArtifactOperationStatus.IndexingSkipped` and response status `"stored-not-indexed"` (HTTP 202).
3. Stamps the artifact's blob metadata with the orphan marker and initializes `orphanAttempts=0`.
4. Logs an error with stable `EventId` 1001 (the alert binding point).

The Python local processor receives the 202 and logs a warning when the response body's status is not `"stored"`.

### Automatic Reconciliation (Background Sweep)

`OrphanedArtifactSweepBackgroundService` runs a periodic sweep at the interval configured via `IngestionOptions.OrphanSweepInterval` (default: 5 minutes).

Each sweep cycle:

1. Lists all blobs in the `search-chunks` container with metadata.
2. Filters to blobs where `state == "Orphaned"` (skips `OrphanedTerminal`).
3. For each orphaned blob:
   - Parses the upload ID from the blob path.
   - Re-resolves the ingestion job (the race may have resolved).
   - **If job is found:** Downloads the blob and invokes `SearchChunkIndexingCoordinator.IndexAsync` with the real job ID. The coordinator re-indexes the artifact and overwrites the orphan metadata with the happy-path stamp (clearing orphan keys). The artifact is healed.
   - **If job is still absent:** Checks terminal conditions:
     - If `now - orphanFirstDetectedUtc >= IngestionOptions.OrphanRetentionWindow` (default: 24 hours), transitions to `OrphanedTerminal` with reason `"NoIngestionJobWithinRetentionWindow"`.
     - Else if `orphanAttempts + 1 >= IngestionOptions.MaxOrphanRetryAttempts` (default: 5), transitions to `OrphanedTerminal` with reason `"NoIngestionJobAfterMaxAttempts"`.
     - Else increments `orphanAttempts` and re-stamps the full metadata set.

Each terminal transition logs an error with stable `EventId` (1003 or 1004) and is excluded from subsequent sweeps.

Terminal blobs are never deleted automatically; an operator must explicitly resolve them via the adopt endpoint.

### Manual Operations (Admin Surface)

#### List Orphans

```http
GET /api/ingestion/artifacts/orphaned
Authorization: Bearer <admin-token>
```

Returns a 200 OK response with an array of orphan objects:

```json
[
  {
    "uploadId": "550e8400-e29b-41d4-a716-446655440000",
    "container": "search-chunks",
    "blobPath": "550e8400-e29b-41d4-a716-446655440000/chunks.jsonl",
    "state": "Orphaned",
    "orphanReason": "NoIngestionJob",
    "orphanAttempts": 3,
    "orphanFirstDetectedUtc": "2026-08-03T14:22:35.1234567Z"
  },
  {
    "uploadId": "660e8400-e29b-41d4-a716-446655440000",
    "container": "search-chunks",
    "blobPath": "660e8400-e29b-41d4-a716-446655440000/chunks.jsonl",
    "state": "OrphanedTerminal",
    "orphanReason": "NoIngestionJobAfterMaxAttempts",
    "orphanAttempts": 5,
    "orphanFirstDetectedUtc": "2026-08-02T14:22:35.1234567Z"
  }
]
```

#### Trigger Immediate Sweep

```http
POST /api/ingestion/artifacts/orphaned/sweep
Authorization: Bearer <admin-token>
```

Returns a 202 Accepted response with sweep results:

```json
{
  "orphansFound": 2,
  "healed": 1,
  "attemptsIncremented": 0,
  "terminalTransitions": 1,
  "errored": 0
}
```

The sweep runs synchronously and is serialized via a semaphore to prevent concurrent execution with the background service.

#### Adopt Orphaned Artifact

```http
POST /api/ingestion/artifacts/orphaned/{uploadId}/adopt
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "ingestionJobId": "770e8400-e29b-41d4-a716-446655440000"
}
```

Binds the orphaned artifact (identified by `uploadId`) to the specified ingestion job and re-indexes it through `SearchChunkIndexingCoordinator`. The operator must supply a valid, existing job ID; the endpoint returns 400 if the job does not exist or 404 if the orphan is not found.

Returns a 202 Accepted response with the indexing outcome once re-indexing completes.

## Configuration

Configure orphan reconciliation behavior via `IngestionOptions` in app configuration:

| Setting | Type | Default | Purpose |
|---------|------|---------|---------|
| `MaxOrphanRetryAttempts` | integer | 5 | Maximum number of reconciliation attempts before transition to terminal state |
| `OrphanRetentionWindow` | TimeSpan | 24 hours | Maximum age of an orphan before terminal transition, independent of attempt count |
| `OrphanSweepInterval` | TimeSpan | 5 minutes | Interval between automatic background sweep cycles |

Example Azure App Configuration:

```
Ingestion:MaxOrphanRetryAttempts=5
Ingestion:OrphanRetentionWindow=1.00:00:00
Ingestion:OrphanSweepInterval=00:05:00
```

## Failure Modes and Guarantees

### Fail-Closed Anchor Contract

The system **never** passes `Guid.Empty` or `string.Empty` anchors to the search indexing service, even during reconciliation. Every indexing call carries a real `indexedArtifactId`, `ingestionJobId`, and `sourceContentHash` (or null). This guarantee is enforced at three call sites:

1. Upload path (`ProcessorArtifactService.ProcessSearchChunksAsync`).
2. Sweep healing path (`OrphanedArtifactSweepService.HealArtifactAsync`).
3. Admin adopt endpoint (`IngestionJobsController.AdoptOrphanedArtifactAsync`).

### Idempotency

Orphan reconciliation is idempotent:

- Re-indexing uses the same upsert sequence (`ChunkReprocessService` reuses it) so repeated healing of the same artifact is safe.
- Job state transitions check the current state before transitioning, so a blob re-processed after becoming terminal is skipped.
- Metadata writes are last-write-wins (no ETag precondition), so concurrent sweeps on multi-instance deployments degrade safely (terminal reached sooner; indexing is idempotent).

### Observable Errors

- Every anchor-contract violation logs at Error with a stable `EventId` (1001 for "no job found", 1002 for "empty job ID").
- Every terminal transition logs at Error with its stable `EventId` (1003 for "max attempts", 1004 for "retention window").
- These `EventId` values are the binding points for Azure Monitor alert rules.

### Best-Effort Metadata

Orphan metadata writes are wrapped in best-effort try/catch and do not surface failures to the caller:

- Failures during the upload skip path do not change the returned status or response.
- Failures during the sweep do not abort the cycle; they are logged per-blob and the cycle continues.

## Monitoring and Alerts

Configure Azure Monitor alert rules to bind to the stable `EventId` values:

- `EventId = 1001` — "No ingestion job found" (upload path)
- `EventId = 1002` — "Ingestion job ID is empty" (upload path, data corruption indicator)
- `EventId = 1003` — "No ingestion job after max attempts" (terminal transition)
- `EventId = 1004` — "No ingestion job within retention window" (terminal transition)

These logs contain the affected upload ID (sanitized for log injection) and sufficient context for the operator to investigate.

## Related Components

- **SearchChunkIndexingCoordinator** — Shared indexing logic invoked by the upload path, the sweep, and the adopt endpoint; ensures the fail-closed anchor contract is enforced in exactly one place.
- **IngestionOptions** — Configuration container for orphan reconciliation parameters.
- **BlobObjectDescriptor** — Carries blob metadata (name, size, last modified, and metadata dictionary) for listing operations.
