# Local-First Ingestion: Development Plan

**Status:** Draft  
**Date:** 2026-07-04  
**Goal:** Remove the broken Azure → localhost push model. Ingestion flows from Admin Desktop → local processor → Azure API, all outbound.

---

## Architecture Change

```
BEFORE (broken):  Admin → Azure API → ❌ localhost:8100 (unreachable from cloud)
AFTER (working):  Admin → localhost:8100 (processor) → Azure API (all outbound)
```

---

## Phase 1 — .NET API: New Endpoints

### 1.1 `PUT /api/ingestion/jobs/{jobId}/progress`

**Purpose:** Python processor reports per-chunk progress so the admin app can poll for real-time status.

**Auth:** `mcr-api-local-processor` (M2M)

**Request:**
```json
{ "chunksProcessed": 23, "totalChunks": 50 }
```

**Add to `IngestionJob` (`MotorcycleRAG.Domain/Entities/IngestionJob.cs`):**
```csharp
public int? ChunksProcessedByProcessor { get; set; }
public int? TotalChunksReported { get; set; }
```

**Add to `IngestionJobStatusResponse` (`MotorcycleRAG.Contracts.Models/DTOs/IngestionJobStatusResponse.cs`):**
```csharp
public int? ChunksProcessedByProcessor { get; init; }
public int? TotalChunksReported { get; init; }
```

**New endpoint in `IngestionJobsController`:**
```csharp
[HttpPut("jobs/{jobId:guid}/progress")]
[Authorize(Policy = "mcr-api-local-processor")]
public async Task<IActionResult> ReportProgressAsync(
    Guid jobId, ChunkProgressRequest request, CancellationToken ct)
```

**Implementation:** Simple DB update on `ChunksProcessedByProcessor` and `TotalChunksReported`. Extends existing `IIngestionJobRepository` with `UpdateProgressAsync()`.

### 1.2 `PATCH /api/ingestion/jobs/{jobId}/status`

**Purpose:** Python processor transitions job state (Pending → Processing → ...). Replaces the need for the API to poll the processor.

**Auth:** `mcr-api-local-processor` (M2M)

**Request:**
```json
{ "status": "Processing", "message": "Started chunking 50 chunks" }
```

**New endpoint in `IngestionJobsController`:**
```csharp
[HttpPatch("jobs/{jobId:guid}/status")]
[Authorize(Policy = "mcr-api-local-processor")]
public async Task<IActionResult> TransitionStatusAsync(
    Guid jobId, JobStatusTransitionRequest request, CancellationToken ct)
```

**New DTO (`MotorcycleRAG.Contracts.Models/DTOs/JobStatusTransitionRequest.cs`):**
```csharp
public sealed record JobStatusTransitionRequest
{
    public string Status { get; init; } = string.Empty;
    public string? Message { get; init; }
}
```

**Implementation:** Validates that the transition is legal (e.g. Pending → Processing → Indexing → Completed). Calls `IIngestionJobRepository.UpdateStatusAsync()`.

### 1.3 `PUT /api/ingestion/jobs/{jobId}/source`

**Purpose:** Python processor uploads the full manual PDF to blob storage after chunking is done, so the app can serve it for reference.

**Auth:** `mcr-api-local-processor` (M2M)

**Request:** `multipart/form-data` — the PDF file

**New endpoint in `IngestionJobsController`:**
```csharp
[HttpPut("jobs/{jobId:guid}/source")]
[Authorize(Policy = "mcr-api-local-processor")]
[RequestSizeLimit(500L * 1024 * 1024)]
public async Task<IActionResult> UploadFullSourceAsync(
    Guid jobId, IFormFile file, CancellationToken ct)
```

**Implementation:** Stores in `raw-uploads/{uploadId}/source.pdf` via `IBlobStorageService.UploadAsync()`. Records blob path on the job.

### 1.4 Modify `POST /api/ingestion/jobs` — StartJobAsync

**File:** `MotorcycleRAG.Application/Services/Ingestion/IngestionJobService.cs`

**Changes:**
- Add new `StartJobPendingAsync()` method (or modify `StartJobAsync`) that creates a job with `Status = Pending` and does **NOT** trigger the local pipeline
- Remove the `_pipelineService.TriggerPipelineAsync()` call (lines 269–280)
- Returns `jobId` for the admin app to pass to the Python processor
- The `StartedAtUtc` field remains null until the processor calls `PATCH /jobs/{id}/status` with "Processing"

**New enum value** in `IngestionJobStatus`:
```csharp
// Add: Pending  // Waiting for local processor to pick up
```
The existing `Queued` can be repurposed instead of adding a new value. Decision: use existing `Queued` status, or add `Pending` if `Queued` implies "already dispatched to pipeline."

---

## Phase 2 — Python Processor Changes

### 2.1 Watch Folder

**File:** `src/watcher.py` (new)

```python
# Env vars: WATCH_FOLDER (default: ./input)
# Monitors for new PDF/CSV files
# When detected: read file, start processing, report progress
```

**Library:** `watchdog` (add to `pyproject.toml`)

**Behavior:**
1. Watches `WATCH_FOLDER` for new files matching `*.pdf`, `*.csv`
2. When a file appears, extracts `uploadId` from filename (`{uploadId}.pdf`)
3. Reads local file directly (no API download needed)
4. Calls `PDFProcessor.process_pdf_from_path(file_path, upload_id, doc_type, job_id)`

### 2.2 PDFProcessor — Progress Heartbeat

**File:** `src/processors/pdf_processor.py`

**Changes:**
- Remove: `api_client.download_source()` call — reads from local path instead
- Add: After every 10 chunks embedded, call `PUT /api/ingestion/jobs/{jobId}/progress`
- Add: At start of processing, call `PATCH /api/ingestion/jobs/{jobId}/status` with `"Processing"`
- Add: At end (after chunks upload + full source upload), transition to completed

**New method:**
```python
def _should_heartbeat(self, chunk_index: int, total: int) -> bool:
    return chunk_index == 0 or chunk_index == total or chunk_index % 10 == 0
```

### 2.3 ApiClient — New Methods

**File:** `src/api/api_client.py`

```python
async def report_progress(self, job_id: str, chunks_processed: int, total_chunks: int) -> None:
    """PUT /api/ingestion/jobs/{jobId}/progress"""

async def transition_job_status(self, job_id: str, status: str, message: str | None = None) -> None:
    """PATCH /api/ingestion/jobs/{jobId}/status"""

async def upload_full_source(self, job_id: str, file_path: str) -> None:
    """PUT /api/ingestion/jobs/{jobId}/source"""
```

### 2.4 CSVProcessor — Same Changes

**File:** `src/processors/csv_processor.py`

Apply the same pattern: read local file, progress heartbeats, status transitions.

---

## Phase 3 — Admin Desktop Changes

### 3.1 File Picker + Watch Folder Copy

**File:** `src-tauri/src/lib.rs` — new Tauri command

```rust
#[tauri::command]
async fn copy_to_watch_folder(source_path: String) -> Result<String, String> {
    // Copy file to WATCH_FOLDER / {uuid}.{ext}
    // Returns upload_id (basename without extension)
}
```

**Frontend component:** `src/screens/IngestionScreen.tsx`

**New flow:** Replace the upload+start-job flow with:
1. User selects file via `<input type="file">`
2. Frontend calls `copy_to_watch_folder` (Rust command)
3. Gets back a `{uploadId, jobId}` from the API after creating the pending job

### 3.2 Modified IngestionScreen Flow

```
1. User selects PDF/CSV file
2. Admin copies file to watch folder (Tauri Rust command)
3. Admin calls POST /api/ingestion/jobs to create pending job
   Request: { uploadId, documentType, sourceFileName, totalBytes }
   Response: { jobId, status: "Pending" }
4. Admin polls GET /api/ingestion/jobs/{jobId} every 2s
   Response includes: { status, chunksProcessedByProcessor, totalChunksReported }
5. Processor picks up file, processes, uploads results
6. Status transitions: Pending → Processing → Indexing → Completed
```

### 3.3 Progress Display

**File:** `src/screens/IngestionScreen.tsx` — update job card

Add progress bar when `chunksProcessedByProcessor` and `totalChunksReported` are populated:
```tsx
{job.chunksProcessedByProcessor != null && job.totalChunksReported != null && (
  <ProgressBar 
    value={job.chunksProcessedByProcessor} 
    max={job.totalChunksReported}
    label={`Embedding chunks: ${job.chunksProcessedByProcessor}/${job.totalChunksReported}`}
  />
)}
```

### 3.4 Remove Processor Start from Ingestion Flow

Currently the Admin Desktop starts the Python processor before triggering ingestion. With the watch folder, the processor should already be running (started via `ProcessorScreen`). Remove auto-start logic from `ensureProcessorReady()` in the ingestion flow.

---

## Phase 4 — Remove Dead Code

### 4.1 Remove `LocalPipelineService` integration from `IngestionJobService`

**File:** `MotorcycleRAG.Application/Services/Ingestion/IngestionJobService.cs`

- Remove `_pipelineService` field and constructor parameter
- Remove `TriggerPipelineAsync()` call in `StartJobAsync` (already done in Phase 1.4)
- Remove `GetRunStatusAsync()` call in `RefreshJobStatusAsync` — replace with local DB-only refresh (status and progress fields are set by the processor via API calls, no need to poll the processor)
- Remove `EnrichFailedJobAsync` pipeline polling
- Remove `DocIngestionRunId` population (no longer needed — the job ID is the primary key)

### 4.2 Remove `LocalPipelineService` class

**File:** `MotorcycleRAG.Persistence/ExternalServices/LocalPipelineService.cs` — delete

- Remove service registration from DI
- Remove `LocalEndpoint` from `IngestionOptions` (or keep for backward compat, mark deprecated)

### 4.3 Remove `ILocalPipelineService` interface (optional)

**File:** `3-Domain/MotorcycleRAG.Contracts/Interfaces/ILocalPipelineService.cs`

Can be removed or marked `[Obsolete]`. The interface is no longer used by any application service.

### 4.4 Remove source download endpoints (if no other consumers)

**File:** `ProcessorArtifactsController.cs`

- `GET /api/ingestion/artifacts/source` — remove if only used by local processor
- `GET /api/ingestion/artifacts/source/access` — remove if only used by local processor
- `IIngestionSourceAccessTokenService` — remove if no other consumers

### 4.5 Remove `DocIngestionRunId` from `IngestionJob`

**File:** `3-Domain/MotorcycleRAG.Domain/Entities/IngestionJob.cs`

Remove `DocIngestionRunId` property — jobs are identified by `IngestionJobId` directly. Remove from DTO and SQL schema (nullable migration).

---

## Phase 5 — Configuration Changes

### 5.1 New Environment Variables

| Variable | Component | Default | Purpose |
|----------|-----------|---------|---------|
| `WATCH_FOLDER` | Python processor | `./input` | Directory to watch for incoming files |
| `LOCAL_SOURCE_DIR` | Admin Desktop (Tauri) | `../input` | Where to copy selected files for the processor |

### 5.2 Remove Configuration

| Variable | Component | Action |
|----------|-----------|--------|
| `IngestionOptions.LocalEndpoint` | .NET API | Remove (or deprecate) — no longer pushes to local processor |

### 5.3 Admin Desktop Config Store

**File:** `src/lib/config.ts`

Add `localSourceDir` and `watchFolder` fields to `ConfigState`.

---

## Phase 6 — Testing

### 6.1 .NET API Tests

- `ProcessorProgressControllerTests` — `PUT /jobs/{id}/progress` succeeds, fails on invalid job ID
- `ProcessorProgressControllerTests` — status transition validates legal state changes
- `IngestionJobServiceTests` — `StartJobAsync` creates job in Pending without pipeline trigger
- Remove tests that mock `ILocalPipelineService` from `IngestionJobServiceTests`

### 6.2 Python Processor Tests

- `test_watch_folder_detects_new_file` — watchdog picks up file, triggers processing
- `test_progress_heartbeat` — `_should_heartbeat` returns true every 10 chunks
- `test_api_client_report_progress` — verifies `PUT` call with correct body
- `test_api_client_transition_status` — verifies `PATCH` call with correct body
- Update existing PDF processor tests to use local file path instead of API download mock

### 6.3 Admin Desktop Tests

- `test_copy_to_watch_folder` — Rust command copies file correctly
- `IngestionScreen` — updated integration test for new upload-less flow

---

## Implementation Order

| # | Phase | Component | Description |
|---|-------|-----------|-------------|
| 1 | 1.1 | .NET API | `PUT /jobs/{id}/progress` endpoint |
| 2 | 1.2 | .NET API | `PATCH /jobs/{id}/status` endpoint |
| 3 | 1.3 | .NET API | `PUT /jobs/{id}/source` endpoint |
| 4 | 1.4 | .NET API | Modify `StartJobAsync` — no pipeline trigger, status = Pending |
| 5 | 1.2 | Contracts.Models | `JobStatusTransitionRequest` DTO |
| 6 | 1.1 | Contracts.Models / Domain | Add progress fields to `IngestionJob` + `IngestionJobStatusResponse` |
| 7 | 2.3 | Python | `ApiClient` — `report_progress`, `transition_job_status`, `upload_full_source` |
| 8 | 2.2 | Python | `PDFProcessor` — progress heartbeat + status transitions + full source upload |
| 9 | 2.1 | Python | Watch folder (`watchdog`) |
| 10 | 3.1 | Admin Desktop | Tauri `copy_to_watch_folder` command |
| 11 | 3.2 | Admin Desktop | IngestionScreen — new flow, progress bar |
| 12 | 4.1 | .NET API | Remove `LocalPipelineService` from `IngestionJobService` |
| 13 | 4.2 | .NET API | Delete `LocalPipelineService.cs` |
| 14 | 4.3 | Contracts | Remove/obsolete `ILocalPipelineService` |
| 15 | 4.4 | .NET API | Remove source download endpoints (if safe) |
| 16 | 6 | All | Tests |

---

## Rollback Risk

- The existing `upload → trigger` flow can coexist with the new flow during transition. The `LocalPipelineService` is guarded by `ProcessingMode.Local`. Keep it until the new flow is fully validated, then remove.
- The `DocIngestionRunId` field on `IngestionJob` should be made nullable and not populated in the new flow. Existing data retains the old values for backward compat.
