# Phase 0 Research: Fabric Ingestion Pipeline

This document captures design-impacting findings and open questions for implementing the fabric ingestion pipeline described in `specs/001-fabric-ingestion-pipeline/spec.md`.

## Existing Code Baseline (Observed)

- `1-Presentation/MotorcycleRAG.API/Controllers/DataPipelineUploadController.cs` provides `POST /api/DataPipeline/upload` (legacy) and is already restricted by `[Authorize(Policy = "mcr-api-admin")]`.
- `1-Presentation/MotorcycleRAG.API/Controllers/DataPipelineProcessingController.cs` provides `POST /api/DataPipeline/process` and `GET /api/DataPipeline/status/{executionId}` (legacy) and is also admin-restricted.
- `2-Application/MotorcycleRAG.Application/Pipeline/DataPipelineOrchestrator.cs` is currently a placeholder implementation (returns `Completed` immediately).
- `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/DataPipelineRequest.cs` includes `FilePath` as a required field.
- `4-Persistence/MotorcycleRAG.Persistence/DataProcessing/MotorcyclePDFProcessor.cs` exists and includes page-level locator metadata in chunk metadata, but it does not currently create or persist per-page viewable assets.

## Key Risks / Design Constraints

### 1) Avoid accepting arbitrary server paths from clients

The current `DataPipelineRequest` requires a `FilePath` string. If a client can submit arbitrary values, this becomes a path traversal / local file read risk.

Design direction:
- Prefer that upload returns an opaque `uploadId` / blob key. Subsequent processing requests should reference that opaque ID, not a filesystem path.
- If `FilePath` must remain for legacy reasons, restrict it to a safe, server-controlled storage root and validate it strictly (normalize path; reject absolute paths; reject `..`; reject path separators not allowed; only allow ids).

### 2) Per-page viewable assets: storage + access model

The spec requires a per-page viewable representation for all manuals and the ability for a user to request a specific page.

Candidate storage approach:
- Store page assets (PNG/JPEG) in Azure Blob Storage in a deterministic layout:
  - `manuals/{manualId}/pages/{pageNumber}.png`
  - optionally also `manuals/{manualId}/pages/{pageNumber}.txt` for extracted text/OCR output
- Store metadata in SQL:
  - total pages, pages captured, pages with text, missing pages list, checksums, generated-at timestamps.

Access model for viewing pages (FR-005c):
- Do not make blobs public.
- Provide a page-view API that returns either:
  - a short-lived SAS URL issued server-side after entitlement checks, or
  - a streamed file response (API proxies blob stream) after entitlement checks.

Trade-offs:
- SAS URL: lower API bandwidth, easier client rendering; requires careful expiry/scoping.
- Proxy stream: simpler to reason about (no SAS leakage), but increases API bandwidth/cost.

### 3) Scaling from 500 to 2000 pages

The repo currently has a config concept for PDF processing (seen in `MotorcyclePDFProcessor.cs` via `PDFProcessingConfiguration`). Large manuals are normal (800 to 2000 pages) per FR-004a.

Design direction:
- Eliminate or raise any hard-coded page caps that would truncate ingestion.
- Ensure the ingestion pipeline is incremental/checkpointed:
  - page-level progress recorded
  - intermediate artifacts saved as each page is processed
  - restart does not duplicate already-complete pages/chunks

### 4) Coverage computation (FR-010a)

The spec requires coverage reporting:
- % pages captured as viewable pages
- % pages with searchable text
- list of pages not captured

Design direction:
- Define "viewable pages captured" as: page asset exists and is readable.
- Define "pages with searchable text" as: OCR/text extraction produced non-empty text above a minimum threshold (and optionally confidence).
- Maintain a per-page status table (or JSON blob) to support resuming and to produce accurate coverage reports.

### 5) Logging and sensitive content

`MotorcyclePDFProcessor.cs` currently logs `{FileName}` and other operational details; it also builds prompts for multimodal analysis.

Design direction:
- Do not log extracted text, prompts, or blob keys that may reveal sensitive file names/paths.
- Prefer jobId/manualId and counts/durations.

## Proposed Phase 1 Artifacts (Next)

1) `specs/001-fabric-ingestion-pipeline/data-model.md`
- Define `ManualDocument`, `ManualPageAsset`, `IngestionJob`, and page-level status tracking fields needed for FR-010a/FR-015.

2) `specs/001-fabric-ingestion-pipeline/contracts/`
- Add API contracts (request/response shapes) for:
  - requesting a manual page
  - reading job status including coverage metrics and missing pages

3) `specs/001-fabric-ingestion-pipeline/quickstart.md`
- Local run/validation steps without secrets (env var placeholders only).

## Fabric Capacity Model (Verify)

Fabric workloads run on capacity (measured in capacity units / CUs). Trial capacity exists for 60 days and provides a fixed pool of compute (Microsoft documentation indicates Trial is equivalent to 64 CUs).

Implications:
- The dominant cost driver during the trial is "time on capacity" rather than per-job billing, so bulk manual processing is feasible if it stays within trial constraints.
- External metered dependencies are still metered (for example: any LLM/token-based API calls), so we need per-step toggles and guardrails.

Scope decision for this feature:
- Use Fabric as the primary compute plane for bulk ingestion during the trial/capacity period.
- Keep the architecture extensible for a future local-laptop (RTX 5090) worker, but do not implement that execution path in this feature.

## Open Questions (Need Decisions)

- Should manual page viewing return a SAS URL or proxy the bytes from the API?
  - This changes security posture, caching strategy, and API bandwidth.
- Where is the manuals entitlement represented today (claim, role, subscription flag)?
  - We need a concrete authorization check to map to FR-005c.

## Authorization Baseline (Observed)

- The API currently defines authorization policies in `1-Presentation/MotorcycleRAG.API/Program.cs`:
  - `mcr-api-admin` (admin scope + admin role + azp client isolation)
  - `User` and `Viewer` (app roles)
  - `Read` and `Chat` (scope-based)
- There is no existing policy/claim specifically named for manuals viewing.

Recommended direction for FR-005c:

- Add a dedicated policy `mcr-api-manuals-view`.
- Map it to an existing, stable entitlement signal to avoid introducing new identity configuration in this feature.
- Default mapping recommendation: allow roles `User` and `Viewer` (and optionally `mcr-api-admin` for support).
