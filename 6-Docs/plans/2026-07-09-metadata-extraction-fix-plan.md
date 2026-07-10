# Metadata Extraction Fix — Implementation Plan

**Status:** Final  
**Date:** 2026-07-09  
**Goal:** Fix the three symptoms (LM Studio errors, dead-end UI, no metadata visibility) and finish the last missing pieces of the metadata extraction pipeline.

---

## 1. Problem / Motivation

### Symptoms

1. **LM Studio `/chat/completions` errors** — Logs show requests reaching LM Studio with the wrong model name (`microsoft/phi-4-reasoning-plus` instead of the configured `qwen3.5-0.8b`) and sometimes with a `/chat/completions` path missing the `/v1` prefix. No useful response comes back, metadata extraction silently returns 0% fill rate.

2. **"Needs manual metadata" dead-end** — When extraction fails, the job enters `AwaitingMetadata`. The failure panel (with failure-reason text and the prominent "Enter Metadata →" button) never renders because `JobsScreen.tsx` condition on line 405 only shows it for `["failed", "error", "cancelled"]` statuses. The admin sees only a small pencil icon in the table actions which is easy to miss.

3. **No metadata visibility** — The job list response (`IngestionJobStatusResponse`) has a `RequiresManualMetadata` boolean but no parsed metadata fields. The admin must open the modal (which fetches `GET /metadata`) to see what was extracted. There is no inline display of make/model/year/category in the job row.

### Current Implementation State

A large part of the 2026-07-08 plan *is already implemented*. Verifying against source:

| Component | Status | Key files |
|-----------|--------|-----------|
| `MetadataExtractor` with robust JSON parsing, retry, page sampling | ✅ Done | `metadata_extractor.py` |
| Pipeline stage `extracting-metadata` inserted | ✅ Done | `pdf_processor.py` |
| Pause/resume logic (`_mark_paused_for_metadata`, resume path) | ✅ Done | `pdf_processor.py` |
| `main.py` wiring | ✅ Done | `main.py` |
| `AwaitingMetadata` enum value | ✅ Done | `IngestionJobStatus.cs` |
| `ManualMetadataSubmitRequest` DTO | ✅ Done | `Contracts.Models` |
| `IngestionJobMetadataResponse` DTO (GET endpoint) | ✅ Done | `Contracts.Models` |
| `SubmitManualMetadataAsync` / `GetJobMetadataAsync` services | ✅ Done | `IngestionJobService.cs` |
| POST/GET `/jobs/{jobId}/metadata` endpoints + admin auth | ✅ Done | `IngestionJobsController.cs` |
| `ManualMetadataModal` component | ✅ Done | `ManualMetadataModal.tsx` |
| `metadataApi.ts` helpers + validation | ✅ Done | `metadataApi.ts` |
| `isAwaitingMetadata` / `isResumingAfterMetadata` helpers | ✅ Done | `ingestionJob.ts` |
| `IngestionJobFailurePanel` with `onEnterMetadata` support | ✅ Done | `IngestionJobFailurePanel.tsx` |
| `UseResumeAfterMetadata` hook | ✅ Done | `useResumeAfterMetadata.ts` |
| Python unit tests (525 lines) | ✅ Done | `test_metadata_extractor.py` |
| Admin Desktop unit tests (modal, failure panel, jobs screen) | ✅ Done | Various `.test.tsx` files |
| `MetadataResult` Pydantic model | ✅ Done | `schemas.py` |

**What is STILL broken or missing** is tracked in the task list below.

---

## 2. Approved Decisions

| ID | Decision | Rationale |
|----|----------|-----------|
| D1 | **Graceful degradation:** The metadata extractor MUST NOT crash the processor if the LLM is unreachable or the model is not loaded. Instead, log a clear warning and fall back to manual metadata entry. | The pipeline must survive without the LLM. Manual metadata is the safety net. |
| D2 | **No separate model config for metadata.** Reuse `GRAPH_EXTRACTION_ENDPOINT` and `GRAPH_EXTRACTION_MODEL`. | Spec requirement ("no separate config"). |
| D3 | **Failure panel MUST render for AwaitingMetadata.** The existing `IngestionJobFailurePanel` component supports `onEnterMetadata` and `requiresManualMetadata` — it just needs the outer rendering condition fixed. | Minimal change, maximum UX impact. |
| D4 | **Metadata summary fields go into the existing `IngestionJobStatusResponse` DTO** rather than a new endpoint/view-model. | Eliminates an extra round-trip for the list view. The detail GET endpoint remains for pre-fill. |
| D5 | **Health check includes graph extraction endpoint/model** in the Python health response, and is displayed in the Processor screen. | Operators need to see at a glance whether the extraction LLM is configured. |
| D6 | **Model connectivity probe is BEST-EFFORT only.** Probe LM Studio `/v1/models` at `MetadataExtractor` init and log a warning if the configured model is not found. Never block startup or pipeline execution. | Consistent with graceful-degradation approach (D1). |
| D7 | **The `.env` file must include `GRAPH_EXTRACTION_ENDPOINT` and `GRAPH_EXTRACTION_MODEL`** for standalone processor operation. | Without these, `MetadataExtractor.__init__` raises `ValueError` and the processor crashes on startup when run outside the Admin Desktop. |

---

## 3. Investigation Findings

### 3.1 Model Mismatch Confirmation

**Configured model:** `qwen3.5-0.8b` at two locations:
- `config.ts:41` — `graphExtractionModel: "qwen3.5-0.8b"` (default in Admin Desktop)
- `.env.example:70` — `GRAPH_EXTRACTION_MODEL=qwen3.5-0.8b` (template)

**Error log shows:** `microsoft/phi-4-reasoning-plus` in the request body.

**Why the mismatch — root cause chain:**

1. The **`.env` file** (the actual one, not `.env.example`) does **NOT contain** `GRAPH_EXTRACTION_MODEL` at all. The env var is only set by the Admin Desktop's Rust launcher, which reads from the persisted `AppConfig`.
2. The `SettingsScreen.tsx` (lines 144-146) has an **editable text field** for `graphExtractionModel`. The user can type any model name, and it gets persisted to `config.json` via `tauri-plugin-store`.
3. Once saved, the Rust `processor_start()` inserts it as the `GRAPH_EXTRACTION_MODEL` env var when spawning the Python process.
4. The Python `MetadataExtractor.__init__` reads this env var and passes it to the OpenAI SDK.
5. **Either** the user changed the model name in Settings to `microsoft/phi-4-reasoning-plus` and that name does not match what LM Studio expects, **or** the user loaded a different model in LM Studio than what's configured.

**Also noted:** The LM Studio error `[ERROR] Unexpected endpoint or method. (POST /chat/completions)` — note the missing `/v1` prefix in the path shown. This happens when `base_url` does not end with `/v1`. The default (`http://localhost:1234/v1`) is correct, but if the user changed the endpoint in Settings or the `.env` has `EMBEDDING_PROVIDER_ENDPOINT=http://localhost:1234` (which it does, line 14, without `/v1`), the OpenAI SDK constructs `POST /chat/completions` (no `/v1`) and LM Studio rejects it.

**Resolution:** The user should verify in the Admin Desktop's Settings screen (or `config.json`) that:
- `graphExtractionEndpoint` ends with `/v1` (e.g., `http://localhost:1234/v1`)
- `graphExtractionModel` matches exactly the model loaded in LM Studio
- LM Studio's local server is running and the model is loaded

### 3.2 Settings Architecture Is Correct

The env-var pass-through chain is verified working:
```
Admin Settings → AppConfig → ProcessorStartConfig → Rust envs → Python os.getenv()
```

No architecture change needed.

### 3.3 Feature Completeness Gap

The 2026-07-08 plan's implementation is 90% complete. The remaining gaps are concentrated in:
1. **One line** in `JobsScreen.tsx` that excludes `AwaitingMetadata` from the failure panel
2. **DTO fields** missing from `IngestionJobStatusResponse` for inline metadata display
3. **Health visibility** — graph extraction status is absent from both Python and Admin Desktop health displays
4. **Connectivity probe** — `MetadataExtractor` never validates that the configured model exists
5. **`.env` file** — missing required graph extraction env vars

---

## 4. Task List

### T1: Fix Failure Panel Condition for AwaitingMetadata

| Field | Value |
|-------|-------|
| **Description** | Change `JobsScreen.tsx` line 405 from `{isIngestionFailed(j.status) && (` to `{(isIngestionFailed(j.status) \|\| isAwaitingMetadata(j)) && (` so the `IngestionJobFailurePanel` renders for awaiting-metadata jobs. The panel already has `onEnterMetadata` and `requiresManualMetadata` wiring — this is the one condition that blocks it. |
| **Exact files** | `1-Presentation/MotorcycleRAG.AdminDesktop/src/screens/JobsScreen.tsx` |
| **Skill** | `maui-dev` |
| **Dependencies** | None |
| **Parallelism** | Yes — disjoint from all other files, BUT see T3 (same file, must be sequential) |
| **Acceptance criteria** | 1. A job with `status === "AwaitingMetadata"` renders the `IngestionJobFailurePanel` with the failure reason text. 2. The "Enter Metadata →" button appears when `requiresManualMetadata === true`. 3. Clicking the button opens the `ManualMetadataModal`. 4. Existing tests pass. |

---

### T2: Add Parsed Metadata Fields to IngestionJobStatusResponse

| Field | Value |
|-------|-------|
| **Description** | Add `Make`, `Model`, `Year`, `Category`, `Tags`, `FillRate`, `IsComplete` to `IngestionJobStatusResponse` DTO. Populate them in `IngestionJobStatusMapper` by parsing `job.MetadataJson` when it is non-null. This lets the UI display metadata inline without an extra `GET /metadata` round-trip. |
| **Exact files** | `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/IngestionJobStatusResponse.cs` (add fields), `2-Application/MotorcycleRAG.Application/Features/Ingestion/Mappers/IngestionJobStatusMapper.cs` (populate from `job.MetadataJson`) |
| **Skill** | `dotnet-dev` |
| **Dependencies** | None |
| **Parallelism** | Yes — disjoint from all other files |
| **Acceptance criteria** | 1. DTO compiles with 7 new nullable/optional fields. 2. Mapper populates fields from JSON when `job.MetadataJson` is not null/empty. 3. Mapper returns defaults (null, 0, empty list) when `job.MetadataJson` is null. 4. Existing `dotnet test` pass. |

---

### T3: Display Metadata Inline in Job Row

| Field | Value |
|-------|-------|
| **Description** | In `JobsScreen.tsx`, when a job has metadata (from the new DTO fields), show an inline preview. Use an `InlineMetadata` sub-component or a compact text line: e.g., "Honda CBR600RR (2023) — sport" with a fill-rate badge. Add a helper `formatMetadataDisplay()` in `ingestionJob.ts`. The pencil button for AwaitingMetadata remains. |
| **Exact files** | `1-Presentation/MotorcycleRAG.AdminDesktop/src/screens/JobsScreen.tsx` (must be after T1 to avoid merge conflicts), `1-Presentation/MotorcycleRAG.AdminDesktop/src/lib/ingestionJob.ts` (helper function) |
| **Skill** | `maui-dev` |
| **Dependencies** | **T1** (same file, must merge after T1), **T2** (DTO fields must exist in the type — the Typescript `IngestionJobStatus` interface must be extended) |
| **Parallelism** | No — conflicts with T1 on `JobsScreen.tsx`. Must run after T1. |
| **Acceptance criteria** | 1. A job with metadata shows "Honda CBR600RR (2023)" or similar inline. 2. Fill rate < 100% shows a warning-styled fill-rate badge. 3. A job without metadata shows nothing extra. 4. The display is compact (one line, no extra row). 5. `npx tsc --noEmit` passes. 6. Existing tests pass. |

---

### T4: Add Graph Extraction Endpoint/Model to Python Health Check

| Field | Value |
|-------|-------|
| **Description** | In `_build_health_response()` (`main.py`), add `graph_extraction_endpoint` and `graph_extraction_model` to the `services` dict in all three response paths (unhealthy, degraded, healthy). The values come from the module-level `metadata_extractor` instance's `_endpoint` and `_model` attributes. |
| **Exact files** | `2-Application/local-processing-service/src/main.py` |
| **Skill** | `python-dev` |
| **Dependencies** | None |
| **Parallelism** | Yes — disjoint from all other files |
| **Acceptance criteria** | 1. `GET /health` returns `services.graph_extraction_endpoint` and `services.graph_extraction_model`. 2. Values match what was set via env vars. 3. Tests pass (update health-response assertion if needed). |

---

### T5: Add Model Connectivity Probe to MetadataExtractor

| Field | Value |
|-------|-------|
| **Description** | Add an async `check_connectivity()` method to `MetadataExtractor`. It makes a `GET` request to `{base_url}/models` (using `httpx` or the OpenAI SDK's model listing) and checks if the configured model is in the returned list. Log a **warning** (not error) if the model is not found. Log an **info** if it is found. Never raise an exception — connectivity check failures are swallowed and logged. Call during `MetadataExtractor.__init__` via `asyncio.create_task` or lazy on first `extract()` call. |
| **Exact files** | `2-Application/local-processing-service/src/extraction/metadata_extractor.py` |
| **Skill** | `python-dev` |
| **Dependencies** | None |
| **Parallelism** | Yes — disjoint from all other files |
| **Acceptance criteria** | 1. When LM Studio is running and the model is loaded, log says `"Graph extraction model '{model}' confirmed on {endpoint}"`. 2. When LM Studio is running but model is not loaded, log says `"Graph extraction model '{model}' NOT FOUND on {endpoint}. Available: [list]"`. 3. When LM Studio is unreachable, log says `"Could not probe graph extraction endpoint {endpoint}: {reason}"`. 4. `MetadataExtractor` instantiation never blocks or raises due to probe. |

---

### T6: Add Graph Extraction Status to Admin Desktop Processor Screen

| Field | Value |
|-------|-------|
| **Description** | Update the `HealthResponse` interface in `processor.ts` to include `graph_extraction_endpoint` and `graph_extraction_model` in the `services` sub-object. Display the graph extraction health info in the Processor screen's status section (alongside embedding provider status). |
| **Exact files** | `1-Presentation/MotorcycleRAG.AdminDesktop/src/lib/processor.ts` (interface), `1-Presentation/MotorcycleRAG.AdminDesktop/src/screens/ProcessorScreen.tsx` or equivalent status component |
| **Skill** | `maui-dev` |
| **Dependencies** | **T4** (the Python health endpoint must report the fields) |
| **Parallelism** | Yes — disjoint from all other files |
| **Acceptance criteria** | 1. `HealthResponse` interface has `graph_extraction_endpoint` and `graph_extraction_model` in `services`. 2. Processor screen displays graph extraction endpoint and model when available. 3. Shows "Not configured" or similar when absent. 4. `npx tsc --noEmit` passes. |

---

### T7: Add Missing Graph Extraction Vars to .env

| Field | Value |
|-------|-------|
| **Description** | Add `GRAPH_EXTRACTION_ENDPOINT` and `GRAPH_EXTRACTION_MODEL` to the `.env` file (not just `.env.example`). Use the same values as `.env.example` lines 68-70. This ensures the processor works standalone without the Admin Desktop. |
| **Exact files** | `2-Application/local-processing-service/.env` |
| **Skill** | `python-dev` |
| **Dependencies** | None |
| **Parallelism** | Yes — disjoint from all other files |
| **Acceptance criteria** | 1. `.env` contains `GRAPH_EXTRACTION_ENDPOINT=http://localhost:1234/v1`. 2. `.env` contains `GRAPH_EXTRACTION_MODEL=qwen3.5-0.8b`. 3. Standalone `python src/main.py` no longer raises `ValueError: GRAPH_EXTRACTION_MODEL environment variable must be set`. |

---

## 5. Sequencing / Dependency Graph

```
                      ┌─────────────────┐
                      │  WAVE 1 (no deps) │
                      └────────┬─────────┘
               ┌───────────────┼───────────────┬───────────────┬───────────────┐
               ▼               ▼               ▼               ▼               ▼
              T1              T2              T4              T5              T7
        (JobsScreen.tsx)  (DTO + mapper) (health check)  (model probe)    (.env fix)
               │               │               │               │
               │               │               │               │
               ▼               ▼               ▼               ▼
          ┌──────────────────────────────────────────────────────────┐
          │                      WAVE 2                              │
          │  T3 (JobsScreen.tsx) ← after T1 (same file) + T2 (deps) │
          │  T6 (Processor UI)   ← after T4 (health deps)           │
          │  T8 (Python tests)   ← after T5 (probe deps)            │
          └──────────────────────────────────────────────────────────┘

Wave 1: T1, T2, T4, T5, T7  (all parallel)
Wave 2: T3, T6              (parallel with each other)
Wave 3: Manual verification, integration / smoke test
```

### Execution Waves

| Wave | Tasks | Description |
|------|-------|-------------|
| **Wave 1** | T1, T2, T4, T5, T7 | All independent — run in parallel across skill agents |
| **Wave 2** | T3, T6 | T3 needs T2's DTO fields and must merge after T1's same-file change. T6 needs T4's health endpoint extension. Parallel with each other. |
| **Wave 3** | Smoke test | Manual end-to-end verification |

---

## 6. Residual Decisions / Risks

| # | Risk / Decision | Owner | Condition to resolve |
|---|-----------------|-------|----------------------|
| R1 | **Model name in Settings may not match LM Studio.** The user may have changed the model in the Admin Desktop's Settings screen to something LM Studio doesn't have. | User | Verify in SettingsScreen → graphExtractionModel matches the model loaded in LM Studio. |
| R2 | **Endpoint missing `/v1` suffix.** If the user changed `graphExtractionEndpoint` to `http://localhost:1234` without `/v1`, the OpenAI SDK constructs `POST /chat/completions` (no prefix) and LM Studio rejects it. | User | Verify endpoint ends with `/v1`. Task T5's connectivity probe will log the exact URL being used. |
| R3 | **Standalone vs Admin Desktop operation.** If the processor is started independently (not via Admin Desktop), the `.env` must have the graph extraction vars. Task T7 fixes this. | python-dev | Task T7 completed and verified. |
| R4 | **T1 and T3 touch the same file.** They must be applied sequentially to avoid merge conflicts. | maui-dev | Use T1 → commit → T3 ordering. |
| R5 | **The user may have multiple LM Studio models and switch between them.** The connectivity probe logs available models, which helps the admin choose the right model name. | python-dev | Task T5 logs available models when the configured one is not found. |

---

## 7. Out of Scope

| Item | Why |
|------|-----|
| Auto-retry metadata extraction when model becomes available | Complex state machine change; the manual fallback exists for this case |
| Multiple model fallback (try different models) | Over-engineering for the current single-model setup |
| Azure/cloud metadata extraction | Not part of the local-processing-service scope |
| Persisting parsed Docling document across pause/resume | Re-parsing on resume is fast enough (decision R2 from original plan) |
| Allowing admin to edit metadata after job completion | Would require re-indexing chunks — separate feature |
| MAUI admin app (deprecated) | Replaced by Admin Desktop |

---

## 8. Required Skills

| Skill | Owns tasks |
|-------|------------|
| `maui-dev` (React/TypeScript/Tauri) | T1, T3, T6 |
| `dotnet-dev` (C# .NET) | T2 |
| `python-dev` (Python) | T4, T5, T7 |

Note: No `test-dev` tasks are listed because the unit tests already exist and cover the path; the changes in T1-T7 are small enough that existing test suites plus manual verification suffice. If the implementation agent identifies test gaps, they should add inline assertions.

---

## 9. Verification Harness

### Code compilation gates
- **C#:** `dotnet build` must succeed (verifies DTO changes)
- **TypeScript:** `npx tsc --noEmit` must pass (verifies UI changes)
- **Python:** `python -m py_compile src/extraction/metadata_extractor.py` must succeed

### Test gates
- **C#:** `dotnet test` — existing ingestion-job-service and controller tests must pass
- **Python:** `pytest tests/test_metadata_extractor.py -v` — all 20+ existing tests pass
- **Admin Desktop:** `npm test` — existing modal, failure panel, and jobs screen tests pass

### Manual verification checklist
1. Start LM Studio with **any model loaded** (not necessarily qwen3.5-0.8b)
2. Start the Admin Desktop and navigate to the Processor screen
3. Verify the graph extraction endpoint and model are displayed in the health section
4. Verify the `.env` file has `GRAPH_EXTRACTION_ENDPOINT` and `GRAPH_EXTRACTION_MODEL`
5. Upload a PDF with clear metadata (e.g., "2023 Honda CBR600RR Service Manual") via Admin Desktop
6. **If LM Studio is running and the model is loaded correctly:** Verify the job completes with metadata
7. **If LM Studio is NOT running or model is wrong:** Verify the job enters `AwaitingMetadata`, the failure panel renders with the "Enter Metadata →" button, the admin can submit metadata, and the job resumes
8. Verify inline metadata display shows extracted/submitted fields in the job row
9. Check the Python service logs for connectivity probe messages at startup
