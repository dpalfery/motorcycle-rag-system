# PDF Metadata Extraction with Iterative LLM + Manual Fallback

**Status:** Draft  
**Date:** 2026-07-08  
**Goal:** Insert an `extracting-metadata` stage into the Python PDF pipeline that iteratively queries an LLM for `make`, `model`, `year`, `category`, and `tags`, pausing for manual admin entry if extraction fails after 10 pages, then resumes.

---

## 1. Problem / Motivation

The current PDF pipeline (`pdf_processor.py`) skips structured metadata extraction. It relies on pre-upload metadata (often incomplete) and proceeds directly from parsing to chunking. This means:
- Search chunks are tagged with stale or missing `make`/`model`/`year`/`category`.
- There is no automated attempt to infer metadata from the document content itself.
- There is no graceful pause/resume when automated extraction is insufficient.

The gap is a metadata extraction stage that:
1. Reads the parsed PDF pages iteratively (3 → 6 → 9 → 10 pages).
2. Uses the same LM Studio endpoint already configured for graph extraction.
3. Stops when all 4 required fields are filled (100% fill rate).
4. If 100% is not reached after 10 pages, pauses the pipeline and signals the C# API.
5. Allows an admin to manually enter metadata via the Admin Desktop UI.
6. Resumes the pipeline from the paused point after manual metadata is submitted.

---

## 2. Approved decisions

| ID | Decision | Rationale |
|----|----------|-----------|
| D1 | New pipeline stage `extracting-metadata` inserted between `parsing` and `chunking`. | Required by spec. |
| D2 | Reuse `GraphExtractor`'s LM Studio endpoint and model config (`GRAPH_EXTRACTION_ENDPOINT`, `GRAPH_EXTRACTION_MODEL`). | Spec says "no separate config." |
| D3 | Iterative page sampling: 3 → 6 → 9 → 10 pages max. | Spec requirement. |
| D4 | Fill rate = count of non-empty required fields / 4. Stop at 100%. | Spec requirement. |
| D5 | If fill rate < 100% after 10 pages, set job stage to `needs-manual-metadata` and report to API. | Spec requirement. |
| D6 | Add `AwaitingMetadata` to `IngestionJobStatus` enum. | Cleanest way to represent the paused state in the C# state machine. |
| D7 | New API endpoint: `POST /api/ingestion/jobs/{jobId}/metadata` (admin auth) to submit manual metadata JSON. | Dedicated endpoint is clearer than overloading the generic status PATCH. |
| D8 | New API endpoint: `GET /api/ingestion/jobs/{jobId}/metadata` to retrieve the current metadata (for the popup pre-fill). | Helps the admin see what was already extracted. |
| D9 | Resume triggered by the C# API calling the Python service's existing `POST /process/pdf` with `job_id` and `metadata` override. | Reuses the existing start mechanism; `PDFProcessor` will detect the existing `job_id` and resume from `chunking`. |
| D10 | `MetadataExtractor` is a new Python class in `src/extraction/metadata_extractor.py`. | Keeps extraction logic separate from pipeline orchestration. |
| D11 | Admin Desktop popup is a modal in `JobsScreen.tsx` that appears when `status === "AwaitingMetadata"` or `currentStage === "needs-manual-metadata"`. | Minimal UI change, reuses existing polling infrastructure. |
| D12 | JSON schema validation on both Python and C# sides. | OWASP ASVS L2 input validation. |

---

## 3. Investigation findings

### 3.1 Python pipeline structure
- `pdf_processor.py` has a monolithic `_process_pdf()` coroutine with inline stage logic.
- `PIPELINE_STAGES` is a hardcoded list used for progress calculation.
- `_jobs` is a module-level `dict[str, dict]` holding in-memory job state.
- `_set_stage()` reports to the C# API via `api_client.report_stage()`.
- `api_client.report_stage()` calls `PATCH /api/ingestion/jobs/by-run/{processorJobId}/status`.

### 3.2 C# job state machine
- `IngestionJobStatus` enum (Domain): `Queued, Processing, Indexing, Completed, Failed, Cancelled, PartiallyCompleted, Deleting`.
- `IngestionJobService.TransitionStageAsync()` handles stage transitions.
- `IngestionJobRepository.UpdateStageAsync()` updates `CurrentStage`, `StageSetAtUtc`, `ExpectedChunkCount`, `IndexedChunkCount`, `FailureReason`.
- `ProcessorArtifactsController.ReportJobStageByRunIdAsync()` is the entry point for processor stage reports.

### 3.3 Admin Desktop
- `JobsScreen.tsx` polls `/api/ingestion/jobs` every 15s via TanStack Query.
- `IngestionJobStatus` interface is in `src/lib/ingestionJob.ts`.
- Uses `axios` via `api` instance for cloud API calls.
- No existing modal/popup component for metadata entry.

### 3.4 LM Studio configuration
- `GraphExtractor` reads `GRAPH_EXTRACTION_ENDPOINT` (default `http://localhost:1234/v1`) and `GRAPH_EXTRACTION_MODEL` (default `qwen3.5-0.8b`).
- `MetadataExtractor` will read the same env vars.

### 3.5 Docling page access
- `DocumentConverter().convert()` returns a `result.document` object.
- Docling documents expose pages via `document.pages` or text can be exported per-page.
- We need to verify the exact API for per-page text extraction (see Task 1.1).

---

## 4. Task list

### Phase 1: Python — Metadata Extractor Core

| # | Phase | Component | Description | Skills | Files |
|---|-------|-----------|-------------|--------|-------|
| 1.1 | 1 | Python | Verify Docling per-page text extraction API and document the exact method for sampling N pages. | python-dev | `2-Application/local-processing-service/src/processors/pdf_processor.py` (read-only) |
| 1.2 | 1 | Python | Create `MetadataExtractor` class in `src/extraction/metadata_extractor.py` with LLM prompt, JSON parsing, fill-rate calculation, and iterative sampling (3→6→9→10). | python-dev | `2-Application/local-processing-service/src/extraction/metadata_extractor.py` |
| 1.3 | 1 | Python | Add `MetadataExtractor` unit tests: happy path, partial fill, empty response, invalid JSON, max pages reached. | test-dev | `2-Application/local-processing-service/tests/test_metadata_extractor.py` |
| 1.4 | 1 | Python | Add `MetadataResult` Pydantic model to `src/models/schemas.py` with `make`, `model`, `year`, `category`, `tags`, `fill_rate`. | python-dev | `2-Application/local-processing-service/src/models/schemas.py` |

### Phase 2: Python — Pipeline Integration

| # | Phase | Component | Description | Skills | Files |
|---|-------|-----------|-------------|--------|-------|
| 2.1 | 2 | Python | Update `PIPELINE_STAGES` in `pdf_processor.py` to insert `extracting-metadata` between `parsing` and `chunking`. Adjust progress mapping. | python-dev | `2-Application/local-processing-service/src/processors/pdf_processor.py` |
| 2.2 | 2 | Python | Add metadata extraction call in `_process_pdf()` after parsing, before chunking. Inject `MetadataExtractor` into `PDFProcessor.__init__`. | python-dev | `2-Application/local-processing-service/src/processors/pdf_processor.py` |
| 2.3 | 2 | Python | Implement pause/resume logic: if fill rate < 100% after 10 pages, set stage `needs-manual-metadata`, report to API, and return from `_process_pdf()` without error. On resume (existing `job_id` + new metadata), skip to chunking. | python-dev | `2-Application/local-processing-service/src/processors/pdf_processor.py` |
| 2.4 | 2 | Python | Update `main.py` to instantiate `MetadataExtractor` and pass to `PDFProcessor`. | python-dev | `2-Application/local-processing-service/src/main.py` |
| 2.5 | 2 | Python | Update `test_pdf_processor.py` to cover: (a) successful metadata extraction, (b) pause at `needs-manual-metadata`, (c) resume with manual metadata. | test-dev | `2-Application/local-processing-service/tests/test_pdf_processor.py` |

### Phase 3: C# API — State Machine & Endpoints

| # | Phase | Component | Description | Skills | Files |
|---|-------|-----------|-------------|--------|-------|
| 3.1 | 3 | C# Domain | Add `AwaitingMetadata` to `IngestionJobStatus` enum. | dotnet-dev | `3-Domain/MotorcycleRAG.Domain/Enums/IngestionJobStatus.cs` |
| 3.2 | 3 | C# Contracts.Models | Add `ManualMetadataSubmitRequest` DTO with JSON string property (validated). | dotnet-dev | `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/ManualMetadataSubmitRequest.cs` |
| 3.3 | 3 | C# Contracts.Models | Add `IngestionJobMetadataResponse` DTO for GET endpoint. | dotnet-dev | `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/IngestionJobMetadataResponse.cs` |
| 3.4 | 3 | C# Domain | Add `MetadataJson` column usage clarification: reuse existing `IngestionJob.MetadataJson` to store the extracted/submitted metadata blob. | dotnet-dev | `3-Domain/MotorcycleRAG.Domain/Entities/IngestionJob.cs` |
| 3.5 | 3 | C# Persistence | Add `UpdateMetadataAsync` method to `IIngestionJobRepository` and `IngestionJobRepository`. | dal-dev | `3-Domain/MotorcycleRAG.Contracts/Interfaces/IIngestionJobRepository.cs`, `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/IngestionJobRepository.cs` |
| 3.6 | 3 | C# Application | Extend `IIngestionJobService` with `SubmitManualMetadataAsync` and `GetJobMetadataAsync`. | dotnet-dev | `3-Domain/MotorcycleRAG.Contracts/Interfaces/IIngestionJobService.cs` |
| 3.7 | 3 | C# Application | Implement `SubmitManualMetadataAsync` in `IngestionJobService`: validate JSON, parse into metadata fields, persist to `MetadataJson`, transition status from `AwaitingMetadata` to `Processing`, trigger processor resume. | dotnet-dev | `2-Application/MotorcycleRAG.Application/Services/Ingestion/IngestionJobService.cs` |
| 3.8 | 3 | C# Application | Implement `GetJobMetadataAsync` in `IngestionJobService`: return current metadata from `MetadataJson`. | dotnet-dev | `2-Application/MotorcycleRAG.Application/Services/Ingestion/IngestionJobService.cs` |
| 3.9 | 3 | C# Application | Update `TransitionStageAsync` to handle `needs-manual-metadata` stage: transition job status to `AwaitingMetadata`. | dotnet-dev | `2-Application/MotorcycleRAG.Application/Services/Ingestion/IngestionJobService.cs` |
| 3.10 | 3 | C# Presentation | Add `POST /api/ingestion/jobs/{jobId}/metadata` and `GET /api/ingestion/jobs/{jobId}/metadata` endpoints to `IngestionJobsController`. | dotnet-dev | `1-Presentation/MotorcycleRAG.API/Controllers/IngestionJobsController.cs` |
| 3.11 | 3 | C# Presentation | Add admin authorization policy to new endpoints (`mcr-api-admin`). | dotnet-dev | `1-Presentation/MotorcycleRAG.API/Controllers/IngestionJobsController.cs` |
| 3.12 | 3 | C# Tests | Add unit tests for `SubmitManualMetadataAsync`, `GetJobMetadataAsync`, and `TransitionStageAsync` with `needs-manual-metadata`. | test-dev | `5-Test/tests/MotorcycleRAG.UnitTests/Pipeline/IngestionJobServiceDualModeTests.cs` |
| 3.13 | 3 | C# Tests | Add controller tests for new endpoints. | test-dev | `5-Test/tests/MotorcycleRAG.UnitTests/Presentation/API/Controllers/IngestionJobsControllerTests.cs` |

### Phase 4: Admin Desktop UI

| # | Phase | Component | Description | Skills | Files |
|---|-------|-----------|-------------|--------|-------|
| 4.1 | 4 | TypeScript | Extend `IngestionJobStatus` interface with `metadata?: Record<string, unknown>` (optional, for display). | maui-dev | `1-Presentation/MotorcycleRAG.AdminDesktop/src/lib/ingestionJob.ts` |
| 4.2 | 4 | TypeScript | Add `submitManualMetadata` and `getJobMetadata` API helpers in `lib/apiClient.ts` or a new `lib/metadataApi.ts`. | maui-dev | `1-Presentation/MotorcycleRAG.AdminDesktop/src/lib/metadataApi.ts` |
| 4.3 | 4 | React | Create `ManualMetadataModal` component: textarea for JSON input, validate JSON shape, submit button, cancel button. Pre-fill with GET result if available. | maui-dev | `1-Presentation/MotorcycleRAG.AdminDesktop/src/components/ManualMetadataModal.tsx` |
| 4.4 | 4 | React | Integrate modal into `JobsScreen.tsx`: show modal when a job has `status === "AwaitingMetadata"` or `currentStage === "needs-manual-metadata"`. Poll for this condition. | maui-dev | `1-Presentation/MotorcycleRAG.AdminDesktop/src/screens/JobsScreen.tsx` |
| 4.5 | 4 | React | On successful metadata submit, invalidate queries and show success toast. | maui-dev | `1-Presentation/MotorcycleRAG.AdminDesktop/src/screens/JobsScreen.tsx` |
| 4.6 | 4 | TypeScript | Add unit tests for `ManualMetadataModal` and metadata API helpers. | test-dev | `1-Presentation/MotorcycleRAG.AdminDesktop/src/components/ManualMetadataModal.test.tsx` |

### Phase 5: Integration & Verification

| # | Phase | Component | Description | Skills | Files |
|---|-------|-----------|-------------|--------|-------|
| 5.1 | 5 | Integration | End-to-end test: upload PDF → pipeline pauses → admin submits metadata → pipeline completes. | test-dev | New integration test file |
| 5.2 | 5 | Security | Review: ensure no PII in metadata JSON, validate input lengths, sanitize logged metadata fields. | csp-security | All changed files |
| 5.3 | 5 | Code Review | Run `code-review` skill on all branches. | code-review | N/A |

---

## 5. Sequencing / dependency graph

```
Phase 1 (Python core)
  1.1 → 1.2 → 1.3 → 1.4

Phase 2 (Python integration)
  Depends on Phase 1
  2.1 → 2.2 → 2.3 → 2.4 → 2.5

Phase 3 (C# API)
  3.1 → 3.2 → 3.3 → 3.4 → 3.5 → 3.6 → 3.7 → 3.8 → 3.9 → 3.10 → 3.11
  3.12 and 3.13 can run in parallel after 3.11

Phase 4 (Admin UI)
  Depends on Phase 3 (API contracts must exist)
  4.1 → 4.2 → 4.3 → 4.4 → 4.5 → 4.6

Phase 5 (Integration)
  Depends on Phase 2, 3, 4
  5.1 → 5.2 → 5.3
```

---

## 6. Residual decisions / risks

| # | Risk / Decision | Owner | Condition to resolve |
|---|-----------------|-------|----------------------|
| R1 | Docling per-page text extraction API may not expose clean page-level text. Need to verify if `document.export_to_text()` or `document.pages[n].text` works. | python-dev | Task 1.1 |
| R2 | Resume mechanism: the Python `_process_pdf` coroutine exits when paused. On resume, `process_pdf_async` is called again with the same `job_id`. We must detect the existing job and skip completed stages. This requires persisting parsed document/chunks across the pause, or re-parsing. **Decision:** Re-parse on resume (simpler, idempotent). The metadata is now known, so the re-parse is fast. | python-dev | Task 2.3 |
| R3 | LM Studio may not be running when metadata extraction is attempted. `MetadataExtractor` should handle connection errors gracefully and treat them as "extraction failed" (proceed to manual fallback). | python-dev | Task 1.2 |
| R4 | The `MetadataJson` field on `IngestionJob` already exists but is unused. We will store the extracted/submitted metadata there. Need to ensure SQL column length is sufficient (it is `nvarchar(max)`). | dal-dev | Task 3.5 |
| R5 | Admin Desktop modal JSON validation: should we validate against the schema client-side? **Decision:** Yes, basic client-side validation (required fields present, year is number) before submitting. | maui-dev | Task 4.3 |
| R6 | Race condition: admin submits metadata while processor is still reporting `needs-manual-metadata`. The API must handle this gracefully (idempotent metadata update). | dotnet-dev | Task 3.7 |

---

## 7. Out of scope

| Item | Why out of scope | Where it belongs |
|------|------------------|------------------|
| Persisting parsed Docling document across pause/resume | Adds complexity (blob storage or local disk). Re-parsing is fast enough. | Future optimization |
| Automatic metadata extraction for CSV files | CSVs have structured headers; different approach needed. | Separate feature |
| LLM-based category inference from chunk embeddings | Over-engineering for current need. | Future feature |
| Allowing admin to edit metadata after job completion | Would require re-indexing chunks. | Separate feature |
| Multi-language metadata extraction | Current prompt is English-only. | Future i18n work |
| Storing per-page extraction attempts in the database | Only the final metadata matters. | Out of scope |

---

## 8. Skill → agent mapping table

| Skill | Agent | Owns tasks |
|-------|-------|------------|
| `python-dev` | Python implementation agent | 1.1–1.4, 2.1–2.5 |
| `dotnet-dev` | C# implementation agent | 3.1–3.11 |
| `dal-dev` | DAL / persistence agent | 3.5 |
| `maui-dev` | Admin Desktop UI agent | 4.1–4.6 |
| `test-dev` | Testing agent | 1.3, 2.5, 3.12, 3.13, 4.6, 5.1 |
| `csp-security` | Security review agent | 5.2 |
| `code-review` | Code review agent | 5.3 |

---

## 9. Verification harness

### Unit tests (Python)
- `test_metadata_extractor.py`: ≥ 90% coverage on `MetadataExtractor`.
- `test_pdf_processor.py`: Covers pause and resume paths.

### Unit tests (C#)
- `IngestionJobServiceDualModeTests`: New tests for `AwaitingMetadata` transition and metadata submission.
- `IngestionJobsControllerTests`: New tests for `POST /metadata` and `GET /metadata`.

### Integration tests
- End-to-end: Upload PDF → verify job reaches `AwaitingMetadata` → submit metadata → verify job completes.

### Security gates
- Input validation on `ManualMetadataSubmitRequest` (max length, JSON schema).
- No raw metadata logged; only opaque job IDs.
- Admin-only authorization on metadata endpoints.

### Manual verification
1. Start LM Studio with a model.
2. Upload a PDF with no metadata via Admin Desktop.
3. Verify pipeline pauses at `needs-manual-metadata`.
4. Verify modal appears in Admin Desktop.
5. Submit metadata JSON.
6. Verify pipeline resumes and completes.
7. Verify search chunks have correct metadata fields.

---

## Appendix A: Detailed Design Specifications

### A.1 `MetadataExtractor` Python class

```python
# src/extraction/metadata_extractor.py

import json
import logging
import os
from typing import Any

import openai

logger = logging.getLogger(__name__)

METADATA_SYSTEM_PROMPT = """You are a motorcycle document metadata extractor.
Given pages from a motorcycle manual or specification document, extract the following metadata:

Required fields:
- make: The manufacturer (e.g., "Honda", "Yamaha", "Kawasaki")
- model: The specific model name (e.g., "CBR600RR", "YZF-R1")
- year: The model year as an integer (e.g., 2023)
- category: The motorcycle category (e.g., "sport", "cruiser", "touring", "naked", "adventure", "off-road")

Optional field:
- tags: A list of relevant tags (e.g., ["sport", "inline-4", "600cc"])

Return ONLY a JSON object with this exact structure:
{
  "make": "Honda",
  "model": "CBR600RR",
  "year": 2023,
  "category": "sport",
  "tags": ["sport", "inline-4", "600cc"]
}

If a field cannot be determined, use null or an empty string for strings, and 0 for year.
Do not include markdown formatting or explanations."""


class MetadataExtractor:
    """Extracts motorcycle metadata from PDF text using an OpenAI-compatible LLM."""

    PAGE_SAMPLE_SIZES = [3, 6, 9, 10]
    REQUIRED_FIELDS = ["make", "model", "year", "category"]

    def __init__(self) -> None:
        self._endpoint = os.getenv("GRAPH_EXTRACTION_ENDPOINT", "http://localhost:1234/v1")
        self._model = os.getenv("GRAPH_EXTRACTION_MODEL", "qwen3.5-0.8b")

    def _compute_fill_rate(self, metadata: dict[str, Any]) -> float:
        filled = sum(1 for f in self.REQUIRED_FIELDS if metadata.get(f))
        return filled / len(self.REQUIRED_FIELDS)

    async def extract(self, pages: list[str]) -> dict[str, Any]:
        """Iteratively sample pages until fill rate is 100% or max pages reached.

        Args:
            pages: List of page text strings (0-indexed).

        Returns:
            dict with keys: make, model, year, category, tags, fill_rate, pages_sampled.
        """
        best_result: dict[str, Any] = {
            "make": None, "model": None, "year": 0,
            "category": None, "tags": [],
            "fill_rate": 0.0, "pages_sampled": 0,
        }

        for sample_size in self.PAGE_SAMPLE_SIZES:
            if sample_size > len(pages):
                sample_size = len(pages)
            if sample_size <= best_result["pages_sampled"]:
                continue

            sample_text = "\n\n".join(pages[:sample_size])
            try:
                client = openai.AsyncOpenAI(base_url=self._endpoint, api_key="local")
                response = await client.chat.completions.create(
                    model=self._model,
                    messages=[
                        {"role": "system", "content": METADATA_SYSTEM_PROMPT},
                        {"role": "user", "content": sample_text},
                    ],
                    temperature=0.1,
                )
                content = response.choices[0].message.content or "{}"
                parsed = json.loads(content)
            except Exception as exc:
                logger.warning("Metadata extraction failed for %d pages: %s", sample_size, exc)
                parsed = {}

            # Merge best fields
            for field in self.REQUIRED_FIELDS:
                if not best_result.get(field) and parsed.get(field):
                    best_result[field] = parsed[field]
            if parsed.get("tags") and not best_result.get("tags"):
                best_result["tags"] = parsed["tags"]

            best_result["pages_sampled"] = sample_size
            best_result["fill_rate"] = self._compute_fill_rate(best_result)

            if best_result["fill_rate"] >= 1.0:
                logger.info("Metadata extraction reached 100%% fill rate at %d pages", sample_size)
                break

        return best_result
```

### A.2 `PDFProcessor` integration points

**Constructor change:**
```python
class PDFProcessor:
    def __init__(
        self,
        blob_writer: BlobWriter,
        embedder: Any,
        graph_extractor: GraphExtractor,
        metadata_extractor: MetadataExtractor,  # NEW
        api_client: ApiClient,
    ):
        ...
        self._metadata_extractor = metadata_extractor
```

**Pipeline stage insertion:**
```python
PIPELINE_STAGES = [
    "copying",
    "parsing",
    "extracting-metadata",  # NEW
    "chunking",
    "embedding",
    "uploading-chunks",
    "extracting-graph",
    "uploading-graph",
    "completed",
]
```

**Pause/resume logic in `_process_pdf()`:**
```python
# After parsing:
self._set_stage(job_id, "extracting-metadata", "Extracting metadata from PDF pages", 0.08)

# Extract page texts from Docling document
pages = [...]  # Docling page texts
metadata_result = await self._metadata_extractor.extract(pages)

# If 100% fill rate, update metadata and continue
if metadata_result["fill_rate"] >= 1.0:
    metadata = _merge_metadata(metadata, metadata_result)
    self._set_stage(job_id, "chunking", "Metadata extracted, starting chunking", 0.12)
else:
    # Pause for manual metadata
    self._set_stage(job_id, "needs-manual-metadata",
        f"Metadata incomplete (fill rate {metadata_result['fill_rate']:.0%}). Waiting for manual entry.",
        0.10,
        metadata_extracted=metadata_result,
    )
    await self._api_client.report_stage(
        job_id, "needs-manual-metadata",
        failure_reason=f"Metadata extraction incomplete after {metadata_result['pages_sampled']} pages. "
                       f"Fill rate: {metadata_result['fill_rate']:.0%}. Manual entry required."
    )
    # Store partial result for resume
    _jobs[job_id]["paused_for_metadata"] = True
    _jobs[job_id]["extracted_metadata"] = metadata_result
    return  # Coroutine ends here; job remains in memory

# Resume path: when process_pdf_async is called again with same job_id and manual metadata
if job_id in _jobs and _jobs[job_id].get("paused_for_metadata"):
    # Skip to chunking
    metadata = _merge_metadata(metadata, manual_metadata_override)
    self._set_stage(job_id, "chunking", "Resuming: manual metadata received", 0.12)
```

### A.3 C# API endpoint signatures

```csharp
// IngestionJobsController.cs

/// <summary>
/// Submit manual metadata for a job that is paused awaiting metadata.
/// Route: POST /api/ingestion/jobs/{jobId}/metadata
/// </summary>
[HttpPost("jobs/{jobId:guid}/metadata")]
[Authorize(Policy = "mcr-api-admin")]
[ProducesResponseType(typeof(IngestionJobStatusResponse), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
public async Task<IActionResult> SubmitManualMetadataAsync(
    Guid jobId,
    [FromBody] ManualMetadataSubmitRequest? request,
    CancellationToken ct)

/// <summary>
/// Get the current metadata for an ingestion job.
/// Route: GET /api/ingestion/jobs/{jobId}/metadata
/// </summary>
[HttpGet("jobs/{jobId:guid}/metadata")]
[Authorize(Policy = "mcr-api-admin")]
[ProducesResponseType(typeof(IngestionJobMetadataResponse), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
public async Task<IActionResult> GetJobMetadataAsync(
    Guid jobId,
    CancellationToken ct)
```

### A.4 DTO shapes

```csharp
// ManualMetadataSubmitRequest.cs
public sealed record ManualMetadataSubmitRequest
{
    /// <summary>JSON string containing the metadata object.</summary>
    [JsonPropertyName("metadataJson")]
    public string MetadataJson { get; init; } = string.Empty;
}

// IngestionJobMetadataResponse.cs
public sealed record IngestionJobMetadataResponse
{
    public Guid JobId { get; init; }
    public string? Make { get; init; }
    public string? Model { get; init; }
    public int? Year { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public double FillRate { get; init; }
    public bool IsComplete { get; init; }
    public string? RawJson { get; init; }
}
```

### A.5 Admin Desktop modal flow

1. **Polling detection:** `JobsScreen` polls `/api/ingestion/jobs` every 15s. When a job has `status === "AwaitingMetadata"` (or `currentStage === "needs-manual-metadata"`), a modal is triggered.
2. **Modal content:**
   - Title: "Manual Metadata Required"
   - Subtitle: "Automatic metadata extraction could not determine all required fields. Please enter the metadata manually."
   - Textarea pre-filled with existing partial metadata (from `GET /api/ingestion/jobs/{jobId}/metadata`).
   - JSON schema hint displayed below textarea.
   - "Submit" and "Cancel" buttons.
3. **Validation:** Client-side JSON parse + required field check (`make`, `model`, `year`, `category`).
4. **Submit:** `POST /api/ingestion/jobs/{jobId}/metadata` with JSON string.
5. **Success:** Close modal, invalidate queries, show toast "Metadata submitted. Pipeline resuming."
6. **Error:** Show error in modal, keep open.

### A.6 Database changes

No schema migration is required. The existing `IngestionJobs.MetadataJson` column (`nvarchar(max)`) will store the metadata blob. The existing `IngestionJobs.CurrentStage` and `IngestionJobs.Status` columns will track the pause/resume state.

However, if `MetadataJson` is not present in the `UPDATE` path of `IngestionJobRepository.UpdateAsync()`, a small change is needed to ensure it is persisted.

---

*Plan produced by Architect agent. Ready for review and finalization.*
