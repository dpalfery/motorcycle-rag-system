# Metadata Extraction & Manual Metadata UI Fixes

**Status:** Draft
**Date:** 2026-07-09
**Goal:** Fix the broken LLM metadata extraction pipeline, ensure the "needs manual metadata" fallback path works end-to-end, and make the extracted/manual metadata visible in the Admin Desktop UI.

---

## 1. Problem / Motivation

### Symptom 1 — LM Studio `/chat/completions` errors
Logs show repeated:
```
POST /chat/completions with body { ... "model": "microsoft/phi-4-reasoning-plus", ... }
[ERROR] Unexpected endpoint or method. (POST /chat/completions). Returning 200 anyway
```
LM Studio returns HTTP 200 but with no useful response. The metadata extraction silently fails, causing the job to pause for manual metadata entry every time.

### Symptom 2 — "Needs manual metadata" dead-end
When extraction fails, the job enters `AwaitingMetadata` status. The user reports there is no visible button or way to enter metadata.

### Symptom 3 — No metadata visibility
The user cannot see what metadata was extracted (or not) for a document.

### Why this matters
Page 1 of a motorcycle manual typically contains make, model, and year. If the LLM call succeeds, extraction should reach 100% fill rate on the first 3 pages. The entire pipeline is blocked by what appears to be a configuration/connection issue with LM Studio.

---

## 2. Investigation Findings

### Architecture Overview

The metadata extraction pipeline has two independent paths sharing the same LM Studio endpoint:

| Component | Language | Endpoint Config | Model Config |
|-----------|----------|----------------|--------------|
| `MetadataExtractor` | Python (OpenAI SDK) | `GRAPH_EXTRACTION_ENDPOINT` env var | `GRAPH_EXTRACTION_MODEL` env var |
| `GraphExtractor` | Python (OpenAI SDK) | `GRAPH_EXTRACTION_ENDPOINT` env var | `GRAPH_EXTRACTION_MODEL` env var |
| `OpenAiCompatibleChatClient` | C# (HttpClient) | `Classifier:Endpoint` config section | `Classifier:Model` config section |

The Python extractors use the OpenAI Python SDK (`openai.AsyncOpenAI(base_url=endpoint)`). The C# client constructs `POST {Endpoint}/chat/completions` manually.

### Data Flow

```
Admin Desktop (Tauri/Rust)
  → Sets env vars: GRAPH_EXTRACTION_ENDPOINT, GRAPH_EXTRACTION_MODEL
  → Spawns Python process (local-processing-service)
    → MetadataExtractor reads env vars, creates openai.AsyncOpenAI(base_url=endpoint)
    → Calls client.chat.completions.create(model=model, ...)
    → OpenAI SDK sends POST {base_url}/chat/completions
    → If fill_rate < 1.0 after max pages → job pauses (AwaitingMetadata)
    → Admin Desktop detects pause → opens ManualMetadataModal
    → Admin submits metadata → POST /api/ingestion/jobs/{id}/metadata
    → C# API transitions job to Processing/resuming
    → Admin Desktop detects "resuming" stage → calls Python POST /process/pdf
    → Python processor resumes from chunking stage
```

### Root Cause Analysis

#### Bug 1: Endpoint URL mismatch (Primary)

**Files involved:**
- `2-Application/local-processing-service/src/extraction/metadata_extractor.py` (line 83)
- `2-Application/local-processing-service/src/extraction/graph_extractor.py` (line 57)
- `1-Presentation/MotorcycleRAG.AdminDesktop/src-tauri/src/lib.rs` (lines 73-74, 578-585)

**Analysis:**
The Rust side sets `GRAPH_EXTRACTION_ENDPOINT` to `http://localhost:1234/v1` (line 74 default). The Python OpenAI SDK's `AsyncOpenAI(base_url=endpoint)` constructs the request URL as `{base_url}/chat/completions`.

With `base_url = "http://localhost:1234/v1"`, the request URL becomes:
`http://localhost:1234/v1/chat/completions`

This is the correct OpenAI-compatible path for LM Studio. However, the error log shows:
- Model in request: `microsoft/phi-4-reasoning-plus` (the Settings-configured model at the time of the failure)
- Path in error: `/chat/completions` (appears to lack `/v1` prefix)

**Possible explanations:**
1. LM Studio may not have the Settings-configured model loaded, or the endpoint/port is wrong
2. The endpoint might be hitting a different LM Studio server or a different port
3. Designs must treat the model as operator-configurable via Settings — never hard-code or fall back to a model name in code

**Evidence:**
- `.env` file in `local-processing-service/` does NOT contain `GRAPH_EXTRACTION_ENDPOINT` or `GRAPH_EXTRACTION_MODEL` — it's a copy of `.env.example` with placeholder values
- The Rust code correctly passes the env vars from Admin Desktop Settings; the request model name should match Settings, not a plan hard-code
- The observed request model (`microsoft/phi-4-reasoning-plus`) is the Settings value and is the correct source of truth; it may change when the operator updates Settings

#### Bug 2: Metadata extractor doesn't validate model availability

**File:** `2-Application/local-processing-service/src/extraction/metadata_extractor.py`

The `MetadataExtractor` creates an `AsyncOpenAI` client at init time but never validates that the configured model is actually loaded in LM Studio. The `_query_llm` method catches exceptions but relies on LM Studio returning HTTP 200 with no choices (which it does even on errors).

#### Bug 3: Silent failure mode — no retry/visibility for metadata extraction

**File:** `2-Application/local-processing-service/src/extraction/metadata_extractor.py` (lines 143-152)

When the LLM call fails (all retries exhausted), the extractor returns `best_result` with `fill_rate = 0.0`. The PDF processor then pauses for manual metadata. There is no mechanism to:
- Retry the extraction later (e.g., after model is loaded)
- Surface the LLM error to the admin UI (only logged as warning)
- Auto-resume if the model becomes available

#### Bug 4: UI shows "Enter Metadata" button but condition may not always fire

**Files:**
- `1-Presentation/MotorcycleRAG.AdminDesktop/src/screens/JobsScreen.tsx` (lines 405-414, 427-435)
- `1-Presentation/MotorcycleRAG.AdminDesktop/src/lib/ingestionJob.ts` (lines 43-63)
- `1-Presentation/MotorcycleRAG.AdminDesktop/src/components/IngestionJobFailurePanel.tsx` (lines 20-31)

**Analysis:**
The `IngestionJobFailurePanel` only renders when `isIngestionFailed(j.status)` returns true. `isIngestionFailed` checks against `["failed", "error", "cancelled"]`. The `AwaitingMetadata` status is NOT in this list.

However, the panel is rendered inside the condition `isIngestionFailed(j.status)` (line 405), so it would NOT render for `AwaitingMetadata` jobs.

BUT there IS a separate "Enter Metadata" pencil button in the table actions (lines 427-435) that checks `isAwaitingMetadata(j)`, which DOES work for `AwaitingMetadata` status.

The user may be confused because:
- The failure panel (which shows the failure reason text) doesn't render for `AwaitingMetadata`
- The pencil button exists but is small and easy to miss
- The auto-open modal logic depends on `awaitingMetadataJob` being found, which requires the status to be exactly `awaitingmetadata` (case-insensitive)

**Potential issue:** If the status string from the API is `"AwaitingMetadata"` (PascalCase from C# enum), the `isAwaitingMetadata` check uses `.toLowerCase()` comparison, which should work. But the `requiresManualMetadata` flag depends on the mapper correctly setting it.

#### Bug 5: Metadata visibility — no metadata display on job details

The `metadata` field on `IngestionJobStatus` is typed as `Record<string, unknown>` but the C# `IngestionJobStatusResponse` DTO does NOT include the raw metadata blob in the list response. The metadata is only available via the separate `GET /api/ingestion/jobs/{jobId}/metadata` endpoint.

This means:
- The jobs list shows no metadata info
- To see metadata, you must open the modal (which fetches via GET)
- There's no inline display of extracted metadata in the job row

---

## 3. Task List

| # | Phase | Component | Description | Skills | Files |
|---|-------|-----------|-------------|--------|-------|
| 1 | Investigation | Python | Verify LM Studio model availability and endpoint reachability. Check that the Settings-configured model (currently `microsoft/phi-4-reasoning-plus`) is loaded and responding on `http://localhost:1234/v1/chat/completions`. | python-dev | (runtime check) |
| 2 | Fix | Python | Add model validation on `MetadataExtractor` init — probe LM Studio `/v1/models` endpoint to verify the configured model is loaded. Log clear error if not. | python-dev | `src/extraction/metadata_extractor.py` |
| 3 | Fix | Python | Improve `_query_llm` error handling — log the actual HTTP status, response body, and model name when LM Studio returns an error. Include endpoint URL in error messages. | python-dev | `src/extraction/metadata_extractor.py` |
| 4 | Fix | Python | Add startup health check for the graph extraction endpoint — when `MetadataExtractor` initializes, make a lightweight GET to `/v1/models` to verify connectivity and model availability. Log the result. | python-dev | `src/extraction/metadata_extractor.py`, `src/main.py` |
| 5 | Fix | Rust/Admin Desktop | Add model validation in `processor.ts` health check — verify the graph extraction model is listed in the processor's health response services. | dotnet-dev | `src-tauri/src/lib.rs`, `src/lib/processor.ts` |
| 6 | UI | Admin Desktop | Add metadata status summary to job row — show extracted metadata fields (make, model, year, category, fill rate) inline when a job has metadata. | dotnet-dev | `src/screens/JobsScreen.tsx`, `src/lib/ingestionJob.ts` |
| 7 | UI | Admin Desktop | Ensure failure panel renders for `AwaitingMetadata` status — add `AwaitingMetadata` to the condition that shows the failure panel, or create a dedicated metadata-pending panel. | dotnet-dev | `src/components/IngestionJobFailurePanel.tsx`, `src/screens/JobsScreen.tsx` |
| 8 | UI | Admin Desktop | Add prominent "Enter Metadata" CTA when job is awaiting metadata — make the button more visible (e.g., status badge color, inline action button). | dotnet-dev | `src/screens/JobsScreen.tsx` |
| 9 | API | C# | Include metadata summary in list response — add `metadata` fields (make, model, year, category, fillRate, isComplete) to `IngestionJobStatusResponse` so the UI can display them without a separate GET call. | dotnet-dev | `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/IngestionJobStatusResponse.cs`, `2-Application/MotorcycleRAG.Application/Features/Ingestion/Mappers/IngestionJobStatusMapper.cs` |
| 10 | Test | Python | Add unit tests for metadata extractor error handling — test LM Studio returning HTTP 200 with no choices, model not found, connection refused, timeout. | python-dev | `tests/test_metadata_extractor.py` |
| 11 | Test | Admin Desktop | Add tests for metadata status display and "Enter Metadata" button visibility. | dotnet-dev | `src/screens/JobsScreen.test.tsx` |

---

## 4. Sequencing / Dependency Graph

```
Task 1 (investigate LM Studio)
  ↓
Task 2 (model validation) + Task 3 (error handling) + Task 4 (startup health check)
  ↓
Task 5 (Rust health check)
  ↓
Task 6 (metadata in job row) ← depends on Task 9 (API metadata in list response)
  ↓
Task 7 (failure panel for AwaitingMetadata) + Task 8 (prominent CTA)
  ↓
Task 9 (API metadata in list response) — can be parallelized with Tasks 6-8
  ↓
Tasks 10-11 (tests)
```

**Critical path:** Task 1 → Tasks 2-4 → Task 5 → Task 9 → Task 6 → Tasks 10-11

**Parallelizable:**
- Tasks 2, 3, 4 can run in parallel (all Python fixes, no file conflicts)
- Tasks 7, 8 can run in parallel (different UI files)
- Task 9 (C# API) can run parallel to Tasks 6-8 (TypeScript UI)

---

## 5. Residual Decisions / Risks

| Decision | Owner | Status |
|----------|-------|--------|
| Should the metadata extractor probe LM Studio at startup and fail fast, or degrade gracefully? | User | **Recommendation:** Probe at startup, log warning, don't fail fast — the model may be loaded later |
| Should the API include metadata in the list response (Task 9) or keep it as a separate GET? | User | **Recommendation:** Include summary in list response for better UX; keep full detail in separate GET |
| What LM Studio model should be the default? | User | **Answer:** Use whatever is configured in Admin Desktop Settings (`graphExtractionModel` → `GRAPH_EXTRACTION_MODEL`). Currently `microsoft/phi-4-reasoning-plus`; designs must not hard-code a model name because it can change. Validate at startup that the configured model is loaded. |
| Should there be an auto-retry mechanism when the model becomes available? | User | **Recommendation:** Not in this plan — adds significant complexity |

---

## 6. Out of Scope

| Item | Reason |
|------|--------|
| Auto-resume metadata extraction when model becomes available | Complex state machine change; defer to separate plan |
| Multiple model fallback (try different models) | Over-engineering for current need |
| LM Studio model auto-loading | LM Studio feature, not our code |
| Azure/cloud metadata extraction | Different architecture, separate plan |
| MAUI admin app (deprecated) | Replaced by Admin Desktop |

---

## 7. Required Skills

- `python-dev` — Python metadata extractor, error handling, health checks
- `dotnet-dev` — C# API DTOs, mappers, TypeScript UI components
- `test-dev` — Unit tests for Python and TypeScript

---

## 8. Verification Harness

- **Python tests:** `pytest tests/test_metadata_extractor.py -v` — new tests for error handling and model validation
- **C# build:** `dotnet build` — verify DTO changes compile
- **C# tests:** `dotnet test` — verify mapper changes don't break existing tests
- **TypeScript type-check:** `npx tsc --noEmit` — verify UI changes compile
- **TypeScript tests:** `npm test` — verify new UI tests pass
- **Manual verification:** Start LM Studio with the Settings-configured model loaded (currently `microsoft/phi-4-reasoning-plus`), process a PDF, verify metadata extraction succeeds and displays in UI
