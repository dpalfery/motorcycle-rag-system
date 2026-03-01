# Local Docling Ingestion Pipeline (Replacing Fabric)

## TL;DR

> **Quick Summary**: Replace the never-completed Microsoft Fabric ingestion pipeline with a local Python FastAPI service running on an RTX 5090 GPU. Uses Docling for context-aware PDF chunking, Qwen3-Embedding-4B via Ollama for embeddings, and a local LLM for graph entity extraction. Results written to Azure Blob Storage and indexed by Azure AI Search.
> 
> **Deliverables**:
> - Complete Python FastAPI service with PDF/CSV processing, embedding generation, graph extraction, and blob writing
> - .NET integration layer (ILocalPipelineService, LocalPipelineService, updated IngestionJobService)
> - GraphEntityIngestionService for SQL Graph population
> - Azure AI Search indexer setup script
> - Fabric code marked as [Obsolete]
> - Unit and integration tests
> 
> **Estimated Effort**: Large
> **Parallel Execution**: YES - 4 waves
> **Critical Path**: Task 1 (scaffold) → Task 2 (PDF processor) → Task 7 (blob writer) → Task 9 (.NET interface) → Task 10 (.NET impl) → Task 11 (IngestionJobService update) → Task 15 (integration tests)

---

## Context

### Original Request
Replace the never-completed Microsoft Fabric ingestion pipeline with a local Python FastAPI service that leverages the RTX 5090 GPU for free compute. The local service handles PDF chunking (Docling), embedding generation (Qwen3-Embedding-4B via Ollama), and graph entity extraction (local LLM via Ollama), writing results to Azure Blob Storage for pickup by an Azure AI Search indexer.

### Interview Summary
**Key Discussions**:
- Fabric pipeline had .NET trigger/status infrastructure built but processing notebooks were never created
- Local 5090 GPU provides free compute — no Fabric capacity costs needed
- Python FastAPI service replaces Fabric with same HTTP trigger/poll contract
- Docling HybridChunker chosen for context-aware chunking preserving header hierarchy and tables
- Qwen3-Embedding-4B via Ollama chosen for free local embeddings — MRL supports 1536 dims matching existing search index
- Graph entity extraction via local LLM — no API costs
- FabricIngestionOptions to be renamed to IngestionOptions with ProcessingMode enum (Local | Fabric)
- Fabric code marked [Obsolete] but not deleted — kept as fallback option

**Research Findings**:
- Python service scaffold already partially exists at `2-Application/local-processing-service/` with `pyproject.toml`, `main.py`, `schemas.py`, `Dockerfile`, `.env.example`
- `main.py` imports modules that don't exist yet: `processors/pdf_processor.py`, `processors/csv_processor.py`, `embeddings/ollama_embedder.py`, `extraction/graph_extractor.py`, `storage/blob_writer.py`
- Existing Azure AI Search index uses 1536-dim vectors (confirmed in `MotorcycleIndexingService.cs:225`)
- `IFabricPipelineService` has `TriggerPipelineAsync` and `GetRunStatusAsync` methods
- `FabricPipelineService` uses `IHttpClientFactory` + `DefaultAzureCredential`
- `IngestionJobService` directly depends on `IFabricPipelineService` and `FabricIngestionOptions`
- `GraphNode` entity: Id, Name, Type, Description, SourceDocumentId, CreatedAtUtc, UpdatedAtUtc
- `GraphEdge` entity exists at `3-Domain/MotorcycleRAG.Domain/Entities/GraphEdge.cs`
- `IGraphRepository` supports: UpsertNodeAsync, UpsertNodesAsync, UpsertEdgeAsync, UpsertEdgesAsync, GetNodesByDocumentAsync, DeleteByDocumentAsync
- Existing Pipeline folder has 18 files including commands, validators, audit logging, coverage calculator
- `.env.example` already defines: AZURE_STORAGE_CONNECTION_STRING, OLLAMA_HOST, OLLAMA_MODEL_EMBEDDING, OLLAMA_MODEL_LLM

### Metis Review
**Identified Gaps** (addressed):
- `.env.example` uses AZURE_STORAGE_CONNECTION_STRING which contains a secret — should use DefaultAzureCredential/managed identity pattern instead → Addressed: use `azure-identity` with `DefaultAzureCredential` in Python, env var only for local dev
- Scaffold imports modules that don't exist → Addressed: tasks create all missing modules
- No error handling strategy for Ollama being unavailable → Addressed: health check + retry logic in embedder
- No chunking dimension validation (must match 1536) → Addressed: explicit dim parameter in embedder config
- CORS `allow_origins=["*"]` is too permissive for production → Addressed: task includes tightening CORS to API origin only
- Dockerfile references `requirements.txt` but project uses `pyproject.toml` → Addressed: fix Dockerfile to use poetry or generate requirements.txt

---

## Work Objectives

### Core Objective
Build a complete local processing pipeline that replaces Microsoft Fabric, enabling PDF/CSV ingestion through a Python FastAPI service running on the local RTX 5090, with results flowing into Azure Blob Storage → Azure AI Search indexer → search index, and graph entities flowing into SQL Server Graph.

### Concrete Deliverables
- 6 Python modules: `pdf_processor.py`, `csv_processor.py`, `ollama_embedder.py`, `graph_extractor.py`, `blob_writer.py`, updated `main.py`
- 1 C# interface: `ILocalPipelineService.cs`
- 1 C# implementation: `LocalPipelineService.cs`
- 1 C# service: `GraphEntityIngestionService.cs`
- 1 C# options class update: `FabricIngestionOptions.cs` → `IngestionOptions.cs`
- 1 Azure CLI script: `setup-search-indexer.sh`
- Updated `IngestionJobService.cs` with local mode support
- Updated `Program.cs` with new DI registrations
- 3+ files marked `[Obsolete]`
- Unit and integration tests

### Definition of Done
- [ ] `curl http://localhost:8100/health` returns healthy with Ollama connected
- [ ] `curl -X POST http://localhost:8100/process/pdf` with test payload returns job_id and processes successfully
- [ ] Processed chunks appear in Azure Blob Storage as JSON Lines
- [ ] Graph entities appear in Azure Blob Storage as JSON
- [x] .NET API can trigger local service via `ILocalPipelineService`
- [x] `dotnet build MotorcycleRAG.sln` compiles with zero warnings
- [x] `dotnet test` passes all existing + new tests
- [ ] Azure AI Search indexer picks up chunks from blob and indexes them

### Must Have
- Docling HybridChunker with `max_tokens=512` for context-aware PDF chunking
- Qwen3-Embedding-4B via Ollama producing exactly 1536-dim vectors (MRL)
- JSON Lines output format compatible with Azure AI Search indexer `jsonLines` parsing mode
- Chunk schema matching existing Azure AI Search index fields (verified against `MotorcycleIndexingService.cs`)
- Graph entity extraction producing GraphNode/GraphEdge-compatible JSON
- `ProcessingMode` enum toggle (Local | Fabric) in IngestionOptions
- `DefaultAzureCredential` for Azure Blob Storage authentication (no connection string secrets in code)
- Health endpoint verifying Ollama connectivity

### Must NOT Have (Guardrails)
- **NO hardcoded secrets** — all credentials via environment variables or managed identity (AGENTS.md rule)
- **NO connection strings in source code** — use `DefaultAzureCredential` for blob storage
- **NO deletion of Fabric code** — mark `[Obsolete]` only, keep as fallback
- **NO changes to the existing Azure AI Search index schema** — 1536-dim vectors, same field names
- **NO new NuGet/npm dependencies without listing them explicitly** in the task
- **NO CORS `allow_origins=["*"]`** in production config — restrict to .NET API origin
- **NO string concatenation for SQL** — all SQL through `IGraphRepository` parameterized queries
- **NO touching MAUI app code** — CsvChunker/PdfChunker serve a different local preview purpose
- **NO over-abstraction** — Python service is simple HTTP endpoints, not a framework
- **NO excessive comments or docstrings** — keep Python code clean and self-documenting

---

## Verification Strategy

> **ZERO HUMAN INTERVENTION** — ALL verification is agent-executed. No exceptions.

### Test Decision
- **Infrastructure exists**: YES (.NET: xUnit + Moq; Python: pytest + pytest-asyncio)
- **Automated tests**: Tests-after (not TDD — existing codebase isn't TDD)
- **Framework**: xUnit for .NET, pytest for Python
- **Strategy**: Unit tests for .NET integration layer, pytest for Python processors, curl-based QA for API endpoints

### QA Policy
Every task MUST include agent-executed QA scenarios.
Evidence saved to `.sisyphus/evidence/task-{N}-{scenario-slug}.{ext}`.

- **Python API**: Use Bash (curl) — Send requests, assert status + response fields
- **Python modules**: Use Bash (python -c / pytest) — Import, call functions, compare output
- **.NET code**: Use Bash (dotnet build/test) — Compile, run tests, verify zero warnings
- **Integration**: Use Bash (curl + Azure CLI) — End-to-end flow verification

---

## Execution Strategy

### Parallel Execution Waves

```
Wave 1 (Start Immediately — Python module implementations, all independent):
├── Task 1: Fix Python scaffold (Dockerfile, CORS, poetry) [quick]
├── Task 2: Implement PDF processor with Docling HybridChunker [deep]
├── Task 3: Implement CSV processor [unspecified-high]
├── Task 4: Implement Ollama embedder [unspecified-high]
├── Task 5: Implement graph entity extractor [deep]
├── Task 6: Implement Azure Blob Storage writer [unspecified-high]
└── Task 7: Create ILocalPipelineService interface (.NET) [quick]

Wave 2 (After Wave 1 — integration, depends on Wave 1 outputs):
├── Task 8: Update main.py to wire all modules + integration test [unspecified-high]
├── Task 9: Implement LocalPipelineService (.NET) [unspecified-high]
├── Task 10: Rename FabricIngestionOptions → IngestionOptions [quick]
└── Task 11: Create Azure AI Search indexer setup script [quick]

Wave 3 (After Wave 2 — .NET orchestration changes):
├── Task 12: Update IngestionJobService for local mode [deep]
├── Task 13: Create GraphEntityIngestionService [deep]
├── Task 14: Update Program.cs DI registrations [quick]
└── Task 15: Mark Fabric code as [Obsolete] [quick]

Wave 4 (After Wave 3 — tests and verification):
├── Task 16: Python unit tests (pytest) [unspecified-high]
├── Task 17: .NET unit tests (xUnit) [unspecified-high]
└── Task 18: Integration test — end-to-end flow [deep]

Wave FINAL (After ALL tasks — independent review, 4 parallel):
├── Task F1: Plan compliance audit (oracle)
├── Task F2: Code quality review (unspecified-high)
├── Task F3: Real manual QA (unspecified-high)
└── Task F4: Scope fidelity check (deep)

Critical Path: Task 1 → Task 8 → Task 12 → Task 18 → F1-F4
Parallel Speedup: ~60% faster than sequential
Max Concurrent: 7 (Wave 1)
```

### Dependency Matrix

| Task | Depends On | Blocks | Wave |
|------|-----------|--------|------|
| 1 | — | 8 | 1 |
| 2 | — | 8 | 1 |
| 3 | — | 8 | 1 |
| 4 | — | 8 | 1 |
| 5 | — | 8 | 1 |
| 6 | — | 8 | 1 |
| 7 | — | 9, 12 | 1 |
| 8 | 1-6 | 16, 18 | 2 |
| 9 | 7 | 12, 14 | 2 |
| 10 | — | 12, 14 | 2 |
| 11 | — | 18 | 2 |
| 12 | 7, 9, 10 | 17, 18 | 3 |
| 13 | 7 | 14, 17 | 3 |
| 14 | 9, 10, 12, 13 | 17 | 3 |
| 15 | — | 17 | 3 |
| 16 | 8 | F1-F4 | 4 |
| 17 | 12, 13, 14, 15 | F1-F4 | 4 |
| 18 | 8, 11, 12, 13 | F1-F4 | 4 |

### Agent Dispatch Summary

- **Wave 1**: **7 tasks** — T1 → `quick`, T2 → `deep`, T3 → `unspecified-high`, T4 → `unspecified-high`, T5 → `deep`, T6 → `unspecified-high`, T7 → `quick`
- **Wave 2**: **4 tasks** — T8 → `unspecified-high`, T9 → `unspecified-high`, T10 → `quick`, T11 → `quick`
- **Wave 3**: **4 tasks** — T12 → `deep`, T13 → `deep`, T14 → `quick`, T15 → `quick`
- **Wave 4**: **3 tasks** — T16 → `unspecified-high`, T17 → `unspecified-high`, T18 → `deep`
- **FINAL**: **4 tasks** — F1 → `oracle`, F2 → `unspecified-high`, F3 → `unspecified-high`, F4 → `deep`

---

## TODOs

- [x] 1. Fix Python Service Scaffold (Dockerfile, CORS, Poetry)

  **What to do**:
  - Fix `Dockerfile` to use `poetry install` instead of `pip install -r requirements.txt` (project uses `pyproject.toml` / Poetry)
  - Or alternatively, add a `poetry export -f requirements.txt -o requirements.txt` step in Dockerfile
  - Tighten CORS in `main.py`: change `allow_origins=["*"]` to read from env var `ALLOWED_ORIGINS` defaulting to `http://localhost:5000` (the .NET API)
  - Update `.env.example`: replace `AZURE_STORAGE_CONNECTION_STRING` with `AZURE_STORAGE_ACCOUNT_URL` (DefaultAzureCredential pattern, no connection string secrets)
  - Add `ALLOWED_ORIGINS` to `.env.example`
  - Ensure `Dockerfile` base image supports GPU access via NVIDIA runtime (use `nvidia/cuda:12.x-runtime-ubuntu22.04` or similar)
  - Verify `pyproject.toml` dependency versions are current and installable

  **Must NOT do**:
  - Do NOT change endpoint signatures or Pydantic models
  - Do NOT add new endpoints
  - Do NOT hardcode any secrets or connection strings

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Config/scaffold fixes only — no business logic
  - **Skills**: [`build-commands`]
    - `build-commands`: Needed to verify Docker build and service startup
  - **Skills Evaluated but Omitted**:
    - `delegate-dotnet`: Not applicable — Python-only task

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 2, 3, 4, 5, 6, 7)
  - **Blocks**: Task 8
  - **Blocked By**: None (can start immediately)

  **References**:

  **Pattern References**:
  - `2-Application/local-processing-service/Dockerfile` — Current Dockerfile to fix (references requirements.txt but project uses poetry)
  - `2-Application/local-processing-service/pyproject.toml` — Poetry config with all dependencies
  - `2-Application/local-processing-service/src/main.py:34-40` — CORS middleware config to tighten
  - `2-Application/local-processing-service/.env.example` — Environment variable template to update

  **WHY Each Reference Matters**:
  - Dockerfile must match the build system (poetry) — current mismatch will cause build failure
  - `.env.example` documents the contract for environment variables — must not contain secret patterns
  - CORS config is a security guardrail from AGENTS.md — `*` is forbidden in production

  **Acceptance Criteria**:
  - [x] `docker build -t local-processing-service .` succeeds in `2-Application/local-processing-service/`
  - [x] `.env.example` contains NO connection string variables — uses `AZURE_STORAGE_ACCOUNT_URL` instead
  - [x] `main.py` CORS reads from `ALLOWED_ORIGINS` env var

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: Docker build succeeds with poetry
    Tool: Bash
    Preconditions: Docker installed, in 2-Application/local-processing-service/ directory
    Steps:
      1. Run `docker build -t local-processing-service .`
      2. Check exit code is 0
      3. Run `docker images local-processing-service` to confirm image exists
    Expected Result: Build completes successfully, image appears in docker images list
    Failure Indicators: Build error mentioning requirements.txt not found, pip install failure
    Evidence: .sisyphus/evidence/task-1-docker-build.txt

  Scenario: CORS config reads from environment
    Tool: Bash (grep)
    Preconditions: main.py has been updated
    Steps:
      1. Search main.py for `allow_origins=["*"]` — must NOT be found
      2. Search main.py for `ALLOWED_ORIGINS` — must be found
      3. Search .env.example for `AZURE_STORAGE_CONNECTION_STRING` — must NOT be found
      4. Search .env.example for `AZURE_STORAGE_ACCOUNT_URL` — must be found
    Expected Result: No wildcard CORS, no connection string secrets, env-driven config
    Failure Indicators: Wildcard CORS still present, connection string still in .env.example
    Evidence: .sisyphus/evidence/task-1-cors-check.txt
  ```

  **Commit**: YES (groups with Tasks 2-6)
  - Message: `feat(local-processing): fix scaffold — Dockerfile, CORS, env config`
  - Files: `2-Application/local-processing-service/Dockerfile`, `main.py`, `.env.example`
  - Pre-commit: `docker build -t local-processing-service 2-Application/local-processing-service/`

- [x] 2. Implement PDF Processor with Docling HybridChunker

  **What to do**:
  - Create `2-Application/local-processing-service/src/processors/pdf_processor.py`
  - Create `__init__.py` files for `processors/` package
  - Implement `PDFProcessor` class with:
    - `__init__(self, blob_writer, embedder, graph_extractor)` — accept dependencies
    - `async process_pdf_async(upload_id, document_type, blob_container, metadata)` → returns job_id string
    - `async get_job_status(job_id)` → returns status dict or None
  - Processing pipeline inside `process_pdf_async`:
    1. Download PDF from Azure Blob using `upload_id` via `blob_writer.download_blob()`
    2. Use Docling `DocumentConverter` to convert PDF → `DoclingDocument`
    3. Use `HybridChunker(tokenizer=..., max_tokens=512)` to produce context-aware chunks
    4. For each chunk: call `chunker.serialize(chunk)` to get text, call `embedder.generate_embedding(text)` for 1536-dim vector
    5. For each chunk: call `graph_extractor.extract_entities(text, metadata)` for GraphNode/GraphEdge candidates
    6. Build chunk dicts matching the schema in `models/schemas.py:Chunk`
    7. Write chunk JSON Lines to blob: `search-chunks/{upload_id}/chunks.jsonl`
    8. Write graph entity JSON to blob: `graph-entities/{upload_id}/entities.json`
    9. Track job status in-memory dict with progress updates
  - Use `asyncio` for background processing (don't block the endpoint)
  - Handle errors gracefully: catch Docling/Ollama failures, update job status to 'failed'

  **Must NOT do**:
  - Do NOT use Azure Document Intelligence — this is replaced by Docling
  - Do NOT change the Chunk schema in `schemas.py`
  - Do NOT hardcode blob container names
  - Do NOT skip the graph extraction step

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: Complex processing pipeline with Docling API, chunking, embedding, and graph extraction coordination
  - **Skills**: [`build-commands`]
    - `build-commands`: Needed to verify Python imports and test execution
  - **Skills Evaluated but Omitted**:
    - `delegate-dotnet`: Not applicable — Python-only task
    - `azure-ai-rag`: Docling is local, not Azure AI

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 3, 4, 5, 6, 7)
  - **Blocks**: Task 8
  - **Blocked By**: None (can start immediately)

  **References**:

  **Pattern References**:
  - `2-Application/local-processing-service/src/main.py:15,48-50` — PDFProcessor import and instantiation pattern (shows constructor signature expected)
  - `2-Application/local-processing-service/src/main.py:90-120` — PDF endpoint handler showing how process_pdf_async is called
  - `2-Application/local-processing-service/src/models/schemas.py:45-77` — `Chunk` Pydantic model — output schema for each chunk
  - `2-Application/local-processing-service/src/models/schemas.py:21-32` — `ProcessPDFRequest` — input schema
  - `2-Application/local-processing-service/src/models/schemas.py:80-94` — `GraphEntity` — graph extraction output schema

  **API/Type References**:
  - `3-Domain/MotorcycleRAG.Domain/Entities/GraphNode.cs` — .NET GraphNode entity shape (Id, Name, Type, Description, SourceDocumentId) — Python graph output must match
  - `4-Persistence/MotorcycleRAG.Persistence/Search/MotorcycleIndexingService.cs:176-253` — Azure AI Search index schema — chunk JSON must match these field names exactly

  **External References**:
  - Docling docs: `https://docling.dev/` — DocumentConverter, HybridChunker API
  - Docling chunking: `from docling.chunking import HybridChunker` — context-aware chunking API

  **WHY Each Reference Matters**:
  - `main.py` imports show the exact class name and constructor the endpoint expects — must match
  - `schemas.py:Chunk` defines the exact fields the JSON Lines output must contain — schema mismatch breaks AI Search indexer
  - `MotorcycleIndexingService.cs` index schema is the ground truth for field names and types — the chunk JSON must use camelCase field names matching this schema
  - `GraphNode.cs` shape must be mirrored in Python graph output for .NET deserialization

  **Acceptance Criteria**:
  - [x] File `2-Application/local-processing-service/src/processors/pdf_processor.py` exists
  - [x] `from processors.pdf_processor import PDFProcessor` succeeds in Python
  - [x] PDFProcessor class has `process_pdf_async` and `get_job_status` methods
  - [x] Chunk output dict contains all fields from `Chunk` schema: id, title, content, documentType, make, model, year, sourceFile, section, pageNumber, pageRange, primarySection, sectionLevel, sectionHeadings, tableCaption, chunkIndex, tags, contentVector, createdAt, updatedAt

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: PDFProcessor imports and instantiates
    Tool: Bash
    Preconditions: Python environment with docling installed
    Steps:
      1. cd 2-Application/local-processing-service/src
      2. Run `python -c "from processors.pdf_processor import PDFProcessor; print('OK')"`
    Expected Result: Prints 'OK' with no import errors
    Failure Indicators: ModuleNotFoundError, ImportError
    Evidence: .sisyphus/evidence/task-2-import-check.txt

  Scenario: Chunk output matches index schema fields
    Tool: Bash
    Preconditions: pdf_processor.py exists
    Steps:
      1. Read pdf_processor.py source
      2. Verify chunk dict construction includes keys: id, title, content, documentType, make, model, year, sourceFile, section, pageNumber, pageRange, primarySection, sectionLevel, sectionHeadings, tableCaption, chunkIndex, tags, contentVector, createdAt, updatedAt
      3. Verify contentVector is generated with 1536 dimensions
    Expected Result: All 20 required fields present in chunk output, vector dim = 1536
    Failure Indicators: Missing fields, wrong field names (snake_case vs camelCase), wrong vector dimensions
    Evidence: .sisyphus/evidence/task-2-schema-check.txt
  ```

  **Commit**: YES (groups with Tasks 1, 3-6)
  - Message: `feat(local-processing): implement PDF processor with Docling HybridChunker`
  - Files: `2-Application/local-processing-service/src/processors/pdf_processor.py`, `processors/__init__.py`
  - Pre-commit: `python -c "from processors.pdf_processor import PDFProcessor"`

- [x] 3. Implement CSV Processor

  **What to do**:
  - Create `2-Application/local-processing-service/src/processors/csv_processor.py`
  - Implement `CSVProcessor` class with:
    - `__init__(self, blob_writer, embedder)` — accept dependencies
    - `async process_csv_async(upload_id, blob_container, metadata)` → returns job_id string
    - `async get_job_status(job_id)` → returns status dict or None
  - Processing pipeline:
    1. Download CSV from Azure Blob using `upload_id`
    2. Parse CSV with `pandas.read_csv()`
    3. Group rows by motorcycle identity (make/model/year) for relational integrity
    4. Create chunks of grouped rows (configurable chunk size from `MAX_CHUNK_SIZE_TOKENS` env var)
    5. For each chunk: format as `"Field: Value | Field: Value"` text, call `embedder.generate_embedding(text)`
    6. Build chunk dicts matching `Chunk` schema
    7. Write chunk JSON Lines to blob: `search-chunks/{upload_id}/chunks.jsonl`
    8. Track job status in-memory dict
  - Handle edge cases: empty CSV, missing required columns (make/model/year), encoding issues

  **Must NOT do**:
  - Do NOT perform graph entity extraction for CSV data (only PDF)
  - Do NOT change the Chunk schema
  - Do NOT use CsvHelper (.NET) — this is Python, use pandas

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Moderate complexity — CSV parsing with grouping logic and embedding
  - **Skills**: [`build-commands`]
    - `build-commands`: Needed to verify Python execution
  - **Skills Evaluated but Omitted**:
    - `dapper-sql`: Not applicable — no SQL in Python service

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2, 4, 5, 6, 7)
  - **Blocks**: Task 8
  - **Blocked By**: None (can start immediately)

  **References**:

  **Pattern References**:
  - `2-Application/local-processing-service/src/main.py:16,52` — CSVProcessor import and instantiation pattern
  - `2-Application/local-processing-service/src/main.py:124-151` — CSV endpoint handler showing how process_csv_async is called
  - `2-Application/local-processing-service/src/models/schemas.py:35-42` — `ProcessCSVRequest` input schema
  - `2-Application/local-processing-service/src/models/schemas.py:45-77` — `Chunk` output schema

  **External References**:
  - pandas CSV: `https://pandas.pydata.org/docs/reference/api/pandas.read_csv.html`

  **WHY Each Reference Matters**:
  - `main.py` constructor shows CSVProcessor takes `blob_writer` and `embedder` (no graph_extractor) — must match
  - `ProcessCSVRequest` differs from ProcessPDFRequest (no document_type field) — constructor must handle both shapes

  **Acceptance Criteria**:
  - [x] File `2-Application/local-processing-service/src/processors/csv_processor.py` exists
  - [x] `from processors.csv_processor import CSVProcessor` succeeds
  - [x] CSVProcessor groups rows by make/model/year before chunking
  - [x] Empty CSV handled gracefully (returns error status, not crash)

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: CSVProcessor imports and instantiates
    Tool: Bash
    Preconditions: Python environment with pandas installed
    Steps:
      1. cd 2-Application/local-processing-service/src
      2. Run `python -c "from processors.csv_processor import CSVProcessor; print('OK')"`
    Expected Result: Prints 'OK' with no import errors
    Failure Indicators: ModuleNotFoundError, ImportError
    Evidence: .sisyphus/evidence/task-3-import-check.txt

  Scenario: CSV rows grouped by motorcycle identity
    Tool: Bash
    Preconditions: csv_processor.py exists
    Steps:
      1. Read csv_processor.py source
      2. Verify grouping logic uses make/model/year fields
      3. Verify chunks respect MAX_CHUNK_SIZE_TOKENS env var
    Expected Result: Grouping logic preserves relational integrity by motorcycle identity
    Failure Indicators: No grouping, chunks split mid-motorcycle
    Evidence: .sisyphus/evidence/task-3-grouping-check.txt
  ```

  **Commit**: YES (groups with Tasks 1, 2, 4-6)
  - Message: `feat(local-processing): implement CSV processor`
  - Files: `2-Application/local-processing-service/src/processors/csv_processor.py`
  - Pre-commit: `python -c "from processors.csv_processor import CSVProcessor"`

- [x] 4. Implement Ollama Embedder

  **What to do**:
  - Create `2-Application/local-processing-service/src/embeddings/ollama_embedder.py`
  - Create `__init__.py` for `embeddings/` package
  - Implement `OllamaEmbedder` class with:
    - `__init__(self)` — read config from env vars: `OLLAMA_HOST` (default `http://localhost:11434`), `OLLAMA_MODEL_EMBEDDING` (default `qwen3-embedding:4b`)
    - `async generate_embedding(text: str) -> list[float]` — call Ollama embeddings API, return exactly 1536-dim vector
    - `async generate_embeddings_batch(texts: list[str]) -> list[list[float]]` — batch embedding generation
    - `async check_ollama_status() -> str` — return 'connected' or 'disconnected' (used by health endpoint)
  - Use the `ollama` Python package (`import ollama`)
  - Pass `num_ctx` or equivalent parameter to request 1536 dimensions (MRL — Matryoshka Representation Learning)
  - Add retry logic: 3 retries with exponential backoff for transient Ollama failures
  - Validate that returned vectors are exactly 1536 dimensions — raise ValueError if not

  **Must NOT do**:
  - Do NOT use Azure OpenAI for embeddings — this is local-only via Ollama
  - Do NOT change the embedding dimension from 1536 — must match existing search index
  - Do NOT hardcode the Ollama host URL

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Ollama API integration with dimension validation and retry logic
  - **Skills**: [`build-commands`]
    - `build-commands`: Verify Python imports and module structure
  - **Skills Evaluated but Omitted**:
    - `azure-ai`: Not applicable — using local Ollama, not Azure AI

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2, 3, 5, 6, 7)
  - **Blocks**: Task 8
  - **Blocked By**: None (can start immediately)

  **References**:

  **Pattern References**:
  - `2-Application/local-processing-service/src/main.py:17,44` — OllamaEmbedder import and instantiation (no-arg constructor)
  - `2-Application/local-processing-service/src/main.py:61` — `embedder.check_ollama_status()` called from health endpoint
  - `2-Application/local-processing-service/.env.example:9-12` — Ollama config env vars: OLLAMA_HOST, OLLAMA_MODEL_EMBEDDING

  **API/Type References**:
  - `4-Persistence/MotorcycleRAG.Persistence/Search/MotorcycleIndexingService.cs:225` — `VectorSearchDimensions = 1536` — the Python embedder MUST produce exactly this dimension count

  **External References**:
  - Ollama Python SDK: `https://github.com/ollama/ollama-python` — `ollama.embed()` API
  - Qwen3-Embedding MRL: supports truncating to 1536 dims from native higher dim

  **WHY Each Reference Matters**:
  - `main.py:44` shows OllamaEmbedder is instantiated with no args — constructor must be parameterless (reads from env)
  - `MotorcycleIndexingService.cs:225` is the ground truth for vector dimensions — ANY mismatch breaks search
  - Ollama SDK docs needed for correct `embed()` API call with dimension parameter

  **Acceptance Criteria**:
  - [x] File `2-Application/local-processing-service/src/embeddings/ollama_embedder.py` exists
  - [x] `from embeddings.ollama_embedder import OllamaEmbedder` succeeds
  - [x] `generate_embedding()` returns a list of exactly 1536 floats
  - [x] `check_ollama_status()` returns 'connected' or 'disconnected' without crashing
  - [x] Retry logic present (3 retries with backoff)

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: OllamaEmbedder imports and reads config from env
    Tool: Bash
    Preconditions: Python environment
    Steps:
      1. cd 2-Application/local-processing-service/src
      2. Run `python -c "from embeddings.ollama_embedder import OllamaEmbedder; e = OllamaEmbedder(); print('OK')"`
    Expected Result: Prints 'OK' — instantiates without error even if Ollama is not running
    Failure Indicators: ImportError, crash on instantiation
    Evidence: .sisyphus/evidence/task-4-import-check.txt

  Scenario: Dimension validation rejects wrong vector size
    Tool: Bash
    Preconditions: ollama_embedder.py exists
    Steps:
      1. Read source code of ollama_embedder.py
      2. Verify there is a check that len(vector) == 1536
      3. Verify ValueError is raised if dimension mismatch
    Expected Result: Explicit 1536-dimension validation present in code
    Failure Indicators: No dimension check, hardcoded different dimension
    Evidence: .sisyphus/evidence/task-4-dim-validation.txt
  ```

  **Commit**: YES (groups with Tasks 1-3, 5-6)
  - Message: `feat(local-processing): implement Ollama embedder with 1536-dim validation`
  - Files: `2-Application/local-processing-service/src/embeddings/ollama_embedder.py`, `embeddings/__init__.py`
  - Pre-commit: `python -c "from embeddings.ollama_embedder import OllamaEmbedder"`

- [x] 5. Implement Graph Entity Extractor

  **What to do**:
  - Create `2-Application/local-processing-service/src/extraction/graph_extractor.py`
  - Create `__init__.py` for `extraction/` package
  - Implement `GraphExtractor` class with:
    - `__init__(self)` — read config from env: `OLLAMA_HOST`, `OLLAMA_MODEL_LLM` (default `qwen3:4b`)
    - `async extract_entities(text: str, metadata: dict) -> list[dict]` — call local LLM via Ollama to extract entities
  - Prompt template for entity extraction:
    - System prompt instructing LLM to extract structured entities from motorcycle manual text
    - Entity types to extract: Procedure, Component, Motorcycle, Specification, Warning
    - Relationship types: REQUIRES (procedure→part), PART_OF, PRECEDES (step ordering), REFERENCES
    - Output format: JSON with nodes (matching GraphNode shape: id, name, type, description, sourceDocumentId) and edges (matching GraphEdge shape)
  - Parse LLM JSON response with error handling for malformed output
  - Assign UUIDs to entities, set confidence scores based on extraction quality
  - Handle edge cases: LLM returns non-JSON, LLM refuses, timeout

  **Must NOT do**:
  - Do NOT use Azure OpenAI for extraction — local LLM only via Ollama
  - Do NOT skip error handling for malformed LLM output — must gracefully degrade
  - Do NOT hardcode entity types — use configurable list

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: Complex LLM prompt engineering + JSON parsing + entity relationship modeling
  - **Skills**: [`build-commands`]
    - `build-commands`: Verify Python module structure
  - **Skills Evaluated but Omitted**:
    - `azure-ai`: Not applicable — local LLM

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2, 3, 4, 6, 7)
  - **Blocks**: Task 8
  - **Blocked By**: None (can start immediately)

  **References**:

  **Pattern References**:
  - `2-Application/local-processing-service/src/main.py:18,45` — GraphExtractor import and instantiation
  - `2-Application/local-processing-service/src/models/schemas.py:80-94` — `GraphEntity` Pydantic model (output shape)
  - `2-Application/local-processing-service/.env.example:12` — `OLLAMA_MODEL_LLM=qwen3:4b`

  **API/Type References**:
  - `3-Domain/MotorcycleRAG.Domain/Entities/GraphNode.cs` — GraphNode shape: Id (Guid), Name, Type, Description, SourceDocumentId, CreatedAtUtc, UpdatedAtUtc
  - `3-Domain/MotorcycleRAG.Domain/Entities/GraphEdge.cs` — GraphEdge shape (the .NET entity this JSON must deserialize into)
  - `3-Domain/MotorcycleRAG.Contracts/Repositories/IGraphRepository.cs` — UpsertNodesAsync/UpsertEdgesAsync signatures show what the .NET side expects

  **External References**:
  - Ollama chat API: `https://github.com/ollama/ollama-python` — `ollama.chat()` for structured generation

  **WHY Each Reference Matters**:
  - `GraphNode.cs` and `GraphEdge.cs` define the exact shape the .NET GraphEntityIngestionService will deserialize — field names must match
  - `IGraphRepository` methods show batch upsert capability — Python output should be list-structured for efficient batch inserts
  - `GraphEntity` Pydantic model in schemas.py may need alignment with GraphNode/GraphEdge .NET shapes

  **Acceptance Criteria**:
  - [x] File `2-Application/local-processing-service/src/extraction/graph_extractor.py` exists
  - [x] `from extraction.graph_extractor import GraphExtractor` succeeds
  - [x] Entity output includes fields matching GraphNode shape: id, name, type, description, sourceDocumentId
  - [x] Relationship output includes edge fields matching GraphEdge shape
  - [x] Malformed LLM output handled gracefully (returns empty list, not crash)

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: GraphExtractor imports and instantiates
    Tool: Bash
    Preconditions: Python environment
    Steps:
      1. cd 2-Application/local-processing-service/src
      2. Run `python -c "from extraction.graph_extractor import GraphExtractor; g = GraphExtractor(); print('OK')"`
    Expected Result: Prints 'OK'
    Failure Indicators: ImportError
    Evidence: .sisyphus/evidence/task-5-import-check.txt

  Scenario: Entity output schema matches .NET GraphNode
    Tool: Bash
    Preconditions: graph_extractor.py exists
    Steps:
      1. Read source code
      2. Verify entity dicts contain keys: id, name, type, description, sourceDocumentId
      3. Verify relationship dicts contain edge fields
    Expected Result: Schema alignment with .NET domain entities
    Failure Indicators: Missing fields, wrong field names
    Evidence: .sisyphus/evidence/task-5-schema-check.txt
  ```

  **Commit**: YES (groups with Tasks 1-4, 6)
  - Message: `feat(local-processing): implement graph entity extractor with LLM prompt`
  - Files: `2-Application/local-processing-service/src/extraction/graph_extractor.py`, `extraction/__init__.py`
  - Pre-commit: `python -c "from extraction.graph_extractor import GraphExtractor"`

- [x] 6. Implement Azure Blob Storage Writer

  **What to do**:
  - Create `2-Application/local-processing-service/src/storage/blob_writer.py`
  - Create `__init__.py` for `storage/` package
  - Implement `BlobWriter` class with:
    - `__init__(self)` — initialize `BlobServiceClient` using `DefaultAzureCredential` with `AZURE_STORAGE_ACCOUNT_URL` env var
    - `is_connected() -> bool` — test connectivity to blob storage
    - `async download_blob(container: str, blob_name: str) -> bytes` — download a blob (for PDF/CSV input)
    - `async upload_jsonl(container: str, blob_path: str, records: list[dict])` — write JSON Lines to blob (one JSON object per line)
    - `async upload_json(container: str, blob_path: str, data: dict | list)` — write JSON to blob
  - Use `azure-storage-blob` with `azure-identity` (`DefaultAzureCredential`) — NO connection strings
  - For local dev: support `AZURE_STORAGE_CONNECTION_STRING` as fallback ONLY if `AZURE_STORAGE_ACCOUNT_URL` is not set
  - JSON Lines format: each line is a complete JSON object, newline-delimited (compatible with AI Search `jsonLines` parsing mode)
  - Handle blob container creation if not exists

  **Must NOT do**:
  - Do NOT hardcode storage account URLs or connection strings
  - Do NOT use connection strings as primary auth — DefaultAzureCredential first
  - Do NOT write non-JSONL format to search-chunks container

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Azure SDK integration with auth pattern and JSONL formatting
  - **Skills**: [`build-commands`, `azure-cli`]
    - `build-commands`: Verify Python module
    - `azure-cli`: May need to verify blob storage connectivity patterns
  - **Skills Evaluated but Omitted**:
    - `azure-storage`: Would be useful but blob writer is straightforward

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1, 2, 3, 4, 5, 7)
  - **Blocks**: Task 8
  - **Blocked By**: None (can start immediately)

  **References**:

  **Pattern References**:
  - `2-Application/local-processing-service/src/main.py:19,43` — BlobWriter import and instantiation (no-arg constructor)
  - `2-Application/local-processing-service/src/main.py:68` — `blob_writer.is_connected()` called from health endpoint
  - `2-Application/local-processing-service/.env.example:3-7` — Blob storage env vars (to be updated by Task 1)

  **API/Type References**:
  - `4-Persistence/MotorcycleRAG.Persistence/ExternalServices/FabricPipelineService.cs:5,34` — Shows `DefaultAzureCredential` pattern used in .NET — Python should mirror this auth approach

  **External References**:
  - Azure Blob Python SDK: `https://learn.microsoft.com/en-us/python/api/azure-storage-blob/` — BlobServiceClient with DefaultAzureCredential
  - Azure Identity Python: `https://learn.microsoft.com/en-us/python/api/azure-identity/` — DefaultAzureCredential

  **WHY Each Reference Matters**:
  - `main.py:43` shows BlobWriter is instantiated with no args — must read config from env
  - `FabricPipelineService.cs` shows the project's established pattern of using DefaultAzureCredential — Python service must follow same pattern
  - Blob container names (`search-chunks`, `graph-entities`) defined in `.env.example` are the conventions the AI Search indexer will target

  **Acceptance Criteria**:
  - [x] File `2-Application/local-processing-service/src/storage/blob_writer.py` exists
  - [x] `from storage.blob_writer import BlobWriter` succeeds
  - [x] Uses `DefaultAzureCredential` as primary auth (not connection string)
  - [x] `upload_jsonl()` writes newline-delimited JSON (one object per line)
  - [x] `is_connected()` returns bool without crashing

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: BlobWriter imports and uses DefaultAzureCredential
    Tool: Bash
    Preconditions: Python environment with azure-storage-blob, azure-identity
    Steps:
      1. cd 2-Application/local-processing-service/src
      2. Run `python -c "from storage.blob_writer import BlobWriter; print('OK')"`
      3. Grep blob_writer.py for 'DefaultAzureCredential' — must be present
      4. Grep blob_writer.py for 'connection_string' — must be fallback only, not primary
    Expected Result: Import succeeds, DefaultAzureCredential is primary auth method
    Failure Indicators: ImportError, connection string as primary auth
    Evidence: .sisyphus/evidence/task-6-auth-check.txt

  Scenario: JSONL output format is correct
    Tool: Bash
    Preconditions: blob_writer.py exists
    Steps:
      1. Read upload_jsonl method source
      2. Verify each record is json.dumps() + newline
      3. Verify no array wrapper (must be newline-delimited, not JSON array)
    Expected Result: Each line is a standalone JSON object, newline-separated
    Failure Indicators: JSON array format, missing newlines between records
    Evidence: .sisyphus/evidence/task-6-jsonl-format.txt
  ```

  **Commit**: YES (groups with Tasks 1-5)
  - Message: `feat(local-processing): implement Azure Blob Storage writer with DefaultAzureCredential`
  - Files: `2-Application/local-processing-service/src/storage/blob_writer.py`, `storage/__init__.py`
  - Pre-commit: `python -c "from storage.blob_writer import BlobWriter"`

- [x] 7. Create ILocalPipelineService Interface (.NET)

  **What to do**:
  - Create `3-Domain/MotorcycleRAG.Contracts/Interfaces/ILocalPipelineService.cs`
  - Define interface with same shape as `IFabricPipelineService` but named for local processing:
    - `Task<string> TriggerPipelineAsync(string uploadId, string documentType, string pipelineId, CancellationToken cancellationToken = default)` — trigger local processing
    - `Task<string> GetRunStatusAsync(string runId, string pipelineId, CancellationToken cancellationToken = default)` — poll job status
  - The `pipelineId` parameter is kept for interface compatibility but will be ignored in local mode (the local service doesn't have pipeline IDs)
  - Follow existing code conventions: XML doc comments, namespace `MotorcycleRAG.Contracts.Interfaces`
  - One interface per file (AGENTS.md rule)

  **Must NOT do**:
  - Do NOT modify `IFabricPipelineService.cs` — it stays as-is
  - Do NOT add methods beyond what IFabricPipelineService has
  - Do NOT add implementation in this file — interfaces only in Contracts

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Single file, interface definition only, follows existing pattern exactly
  - **Skills**: [`delegate-dotnet`, `clean-architecture`]
    - `delegate-dotnet`: C# interface creation
    - `clean-architecture`: Ensure correct layer placement
  - **Skills Evaluated but Omitted**:
    - `dapper-sql`: No SQL involved

  **Parallelization**:
  - **Can Run In Parallel**: YES
  - **Parallel Group**: Wave 1 (with Tasks 1-6)
  - **Blocks**: Tasks 9, 12
  - **Blocked By**: None (can start immediately)

  **References**:

  **Pattern References**:
  - `3-Domain/MotorcycleRAG.Contracts/Interfaces/IFabricPipelineService.cs` — EXACT pattern to follow: same method signatures, XML doc style, namespace

  **API/Type References**:
  - `3-Domain/MotorcycleRAG.Contracts/AGENTS.md` — "Interfaces only (repositories, service abstractions, factories)" — confirms this is the right location

  **WHY Each Reference Matters**:
  - `IFabricPipelineService.cs` is the template — ILocalPipelineService must have the same method signatures so IngestionJobService can swap between them

  **Acceptance Criteria**:
  - [x] File `3-Domain/MotorcycleRAG.Contracts/Interfaces/ILocalPipelineService.cs` exists
  - [x] Interface has `TriggerPipelineAsync` and `GetRunStatusAsync` with same signatures as IFabricPipelineService
  - [x] `dotnet build MotorcycleRAG.sln` compiles with zero warnings
  - [x] File is in correct namespace: `MotorcycleRAG.Contracts.Interfaces`

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: .NET solution builds with new interface
    Tool: Bash
    Preconditions: .NET SDK installed
    Steps:
      1. Run `dotnet build MotorcycleRAG.sln --warnaserror`
      2. Check exit code is 0
    Expected Result: Build succeeded. 0 Warning(s). 0 Error(s).
    Failure Indicators: Build error, namespace mismatch, missing using
    Evidence: .sisyphus/evidence/task-7-dotnet-build.txt

  Scenario: Interface matches IFabricPipelineService shape
    Tool: Bash
    Preconditions: ILocalPipelineService.cs exists
    Steps:
      1. Read ILocalPipelineService.cs
      2. Verify TriggerPipelineAsync signature matches IFabricPipelineService
      3. Verify GetRunStatusAsync signature matches IFabricPipelineService
    Expected Result: Method signatures are identical (same params, same return types)
    Failure Indicators: Different parameter names, missing methods, extra methods
    Evidence: .sisyphus/evidence/task-7-interface-check.txt
  ```

  **Commit**: YES
  - Message: `feat(contracts): add ILocalPipelineService interface`
  - Files: `3-Domain/MotorcycleRAG.Contracts/Interfaces/ILocalPipelineService.cs`
  - Pre-commit: `dotnet build MotorcycleRAG.sln --warnaserror`

- [x] 8. Wire All Python Modules in main.py + Smoke Test

  **What to do**:
  - Update `2-Application/local-processing-service/src/main.py` to ensure all imports resolve correctly against the modules created in Tasks 2-6
  - Verify the module instantiation at lines 43-52 works with the actual implementations
  - Add proper `config.py` module that centralizes env var loading (used by all modules)
  - Add structured logging configuration (replace basic logging with JSON-structured logs)
  - Ensure background task processing works correctly with FastAPI `BackgroundTasks`
  - Add a `/process/pdf` integration test: mock blob download, verify Docling → chunk → embed → blob write pipeline
  - Verify JSON Lines output format: each line is valid JSON matching Azure AI Search index schema
  - Verify graph entity JSON output format matches GraphNode/GraphEdge shapes

  **Must NOT do**:
  - Do NOT change endpoint signatures or paths
  - Do NOT add new endpoints beyond what's already defined
  - Do NOT add authentication to the Python service (the .NET API handles auth)

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Integration wiring across all modules + smoke testing
  - **Skills**: [`build-commands`]
    - `build-commands`: Run service and verify endpoints
  - **Skills Evaluated but Omitted**:
    - `delegate-dotnet`: Python-only task

  **Parallelization**:
  - **Can Run In Parallel**: NO
  - **Parallel Group**: Wave 2 (with Tasks 9, 10, 11 — but 8 depends on all Wave 1 Python tasks)
  - **Blocks**: Tasks 16, 18
  - **Blocked By**: Tasks 1, 2, 3, 4, 5, 6

  **References**:

  **Pattern References**:
  - `2-Application/local-processing-service/src/main.py` — Full file: all imports (lines 15-24), instantiation (lines 43-52), endpoints (lines 56-174)
  - `2-Application/local-processing-service/src/models/schemas.py` — All Pydantic models used by endpoints
  - `2-Application/local-processing-service/.env.example` — All env vars that config.py must load

  **WHY Each Reference Matters**:
  - main.py is the integration point — every module created in Tasks 2-6 must be importable and instantiable from here
  - schemas.py defines the API contract — responses must serialize correctly

  **Acceptance Criteria**:
  - [x] `cd 2-Application/local-processing-service/src && python -c "import main"` succeeds with no ImportError
  - [ ] `uvicorn main:app --host 0.0.0.0 --port 8100` starts without crash
  - [ ] `curl http://localhost:8100/health` returns JSON with status field
  - [ ] `curl http://localhost:8100/docs` returns Swagger UI

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: FastAPI service starts and serves health endpoint
    Tool: Bash
    Preconditions: All Python modules from Tasks 2-6 exist, Ollama running
    Steps:
      1. cd 2-Application/local-processing-service/src
      2. Start service: `uvicorn main:app --host 0.0.0.0 --port 8100 &`
      3. Wait 3 seconds for startup
      4. Run `curl -s http://localhost:8100/health`
      5. Parse JSON response, check "status" field exists
      6. Run `curl -s http://localhost:8100/docs` — check HTTP 200
      7. Kill background uvicorn process
    Expected Result: Health endpoint returns JSON, Swagger docs accessible
    Failure Indicators: Import errors on startup, 500 error on health, no Swagger
    Evidence: .sisyphus/evidence/task-8-service-startup.txt

  Scenario: All module imports resolve
    Tool: Bash
    Preconditions: All Python modules exist
    Steps:
      1. cd 2-Application/local-processing-service/src
      2. Run `python -c "from processors.pdf_processor import PDFProcessor; from processors.csv_processor import CSVProcessor; from embeddings.ollama_embedder import OllamaEmbedder; from extraction.graph_extractor import GraphExtractor; from storage.blob_writer import BlobWriter; print('ALL OK')"`
    Expected Result: Prints 'ALL OK' — all 5 modules importable
    Failure Indicators: Any ImportError or ModuleNotFoundError
    Evidence: .sisyphus/evidence/task-8-import-all.txt
  ```

  **Commit**: YES
  - Message: `feat(local-processing): wire all modules and add config centralization`
  - Files: `2-Application/local-processing-service/src/main.py`, `src/config.py`
  - Pre-commit: `cd 2-Application/local-processing-service/src && python -c "import main"`

- [x] 9. Implement LocalPipelineService (.NET)

  **What to do**:
  - Create `4-Persistence/MotorcycleRAG.Persistence/ExternalServices/LocalPipelineService.cs`
  - Implement `ILocalPipelineService` interface
  - Use `IHttpClientFactory` to call the Python FastAPI service (same pattern as `FabricPipelineService`)
  - `TriggerPipelineAsync`: POST to `{localEndpoint}/process/{documentType}` with JSON body `{upload_id, document_type, blob_container, metadata}`
    - Map `"manual-pdf"` → `/process/pdf`, `"spec-dataset"` → `/process/csv`
    - Return the `job_id` from the response as the "run ID"
  - `GetRunStatusAsync`: GET `{localEndpoint}/jobs/{runId}` and return the `status` field
  - Read endpoint URL from `IngestionOptions.LocalProcessingEndpoint` (env var: `MCR_API_LOCAL_PROCESSING_ENDPOINT`)
  - Add timeout handling: configurable via `IngestionOptions.LocalProcessingTimeoutMinutes`
  - Follow `FabricPipelineService` patterns: structured logging, null checks, XML docs, one class per file

  **Must NOT do**:
  - Do NOT modify `FabricPipelineService.cs` (that's Task 15)
  - Do NOT add authentication headers to local service calls (Python service is local, no auth needed)
  - Do NOT use `DefaultAzureCredential` for local HTTP calls

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: HTTP client implementation following existing patterns
  - **Skills**: [`delegate-dotnet`, `clean-architecture`]
    - `delegate-dotnet`: C# implementation
    - `clean-architecture`: Correct layer placement in Persistence
  - **Skills Evaluated but Omitted**:
    - `dapper-sql`: No SQL in this service

  **Parallelization**:
  - **Can Run In Parallel**: YES (within Wave 2)
  - **Parallel Group**: Wave 2 (with Tasks 8, 10, 11)
  - **Blocks**: Tasks 12, 14
  - **Blocked By**: Task 7 (needs ILocalPipelineService interface)

  **References**:

  **Pattern References**:
  - `4-Persistence/MotorcycleRAG.Persistence/ExternalServices/FabricPipelineService.cs` — EXACT pattern to follow: IHttpClientFactory, structured logging, error handling, constructor validation
  - `3-Domain/MotorcycleRAG.Contracts/Interfaces/ILocalPipelineService.cs` — Interface to implement (created in Task 7)
  - `4-Persistence/MotorcycleRAG.Persistence/AGENTS.md` — Layer constraints: "Implement repository/service interfaces from Contracts"

  **API/Type References**:
  - `2-Application/local-processing-service/src/main.py:90-120` — PDF endpoint: POST /process/pdf with ProcessPDFRequest body, returns ProcessingStatusResponse with job_id
  - `2-Application/local-processing-service/src/main.py:155-156` — Job status: GET /jobs/{job_id}
  - `2-Application/local-processing-service/src/models/schemas.py:97-125` — ProcessingStatusResponse shape (the JSON this service will deserialize)

  **WHY Each Reference Matters**:
  - `FabricPipelineService.cs` is the reference implementation — LocalPipelineService must follow same patterns for consistency
  - Python endpoint signatures define the HTTP contract — request/response shapes must match exactly

  **Acceptance Criteria**:
  - [x] File `4-Persistence/MotorcycleRAG.Persistence/ExternalServices/LocalPipelineService.cs` exists
  - [x] Implements `ILocalPipelineService`
  - [x] Uses `IHttpClientFactory` (not raw `HttpClient`)
  - [x] `dotnet build MotorcycleRAG.sln --warnaserror` succeeds
  - [x] Reads endpoint from `IngestionOptions.LocalProcessingEndpoint`

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: .NET solution builds with LocalPipelineService
    Tool: Bash
    Preconditions: ILocalPipelineService.cs exists from Task 7
    Steps:
      1. Run `dotnet build MotorcycleRAG.sln --warnaserror`
      2. Check exit code is 0
    Expected Result: Build succeeded. 0 Warning(s). 0 Error(s).
    Failure Indicators: Missing interface reference, namespace errors
    Evidence: .sisyphus/evidence/task-9-dotnet-build.txt

  Scenario: LocalPipelineService follows FabricPipelineService patterns
    Tool: Bash
    Preconditions: LocalPipelineService.cs exists
    Steps:
      1. Read LocalPipelineService.cs
      2. Verify IHttpClientFactory injected via constructor
      3. Verify structured logging (ILogger<LocalPipelineService>)
      4. Verify ArgumentNullException.ThrowIfNull on constructor params
    Expected Result: Same patterns as FabricPipelineService
    Failure Indicators: Raw HttpClient, no logging, missing null checks
    Evidence: .sisyphus/evidence/task-9-pattern-check.txt
  ```

  **Commit**: YES (groups with Task 10)
  - Message: `feat(persistence): implement LocalPipelineService for Python FastAPI integration`
  - Files: `4-Persistence/MotorcycleRAG.Persistence/ExternalServices/LocalPipelineService.cs`
  - Pre-commit: `dotnet build MotorcycleRAG.sln --warnaserror`

- [x] 10. Rename FabricIngestionOptions → IngestionOptions + Add Local Config

  **What to do**:
  - Rename `0-Base/MotorcycleRAG.Core/Options/FabricIngestionOptions.cs` to `IngestionOptions.cs`
  - Rename the class from `FabricIngestionOptions` to `IngestionOptions`
  - Add `ProcessingMode` enum: `Local`, `Fabric` (in same file or separate `ProcessingMode.cs`)
  - Add new properties:
    - `ProcessingMode ProcessingMode { get; set; } = ProcessingMode.Local;` (default to Local)
    - `string LocalProcessingEndpoint { get; set; } = "http://localhost:8100";` — doc: set via `MCR_API_LOCAL_PROCESSING_ENDPOINT`
    - `int LocalProcessingTimeoutMinutes { get; set; } = 360;` — doc: set via `MCR_API_LOCAL_PROCESSING_TIMEOUT_MINUTES`
  - Keep ALL existing Fabric properties (WorkspaceEndpoint, PdfPipelineId, etc.) for backward compatibility
  - Update configuration section name from `"FabricIngestion"` to `"Ingestion"` in XML docs
  - Use `lsp_rename` to safely rename across the entire solution

  **Must NOT do**:
  - Do NOT delete any existing Fabric-related properties
  - Do NOT change the default values of existing properties
  - Do NOT manually find-and-replace — use LSP rename for safety

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Rename + add properties — straightforward refactoring
  - **Skills**: [`delegate-dotnet`, `clean-architecture`]
    - `delegate-dotnet`: C# refactoring with LSP rename
    - `clean-architecture`: Ensure rename propagates correctly across layers
  - **Skills Evaluated but Omitted**:
    - `code-review`: Not a review task

  **Parallelization**:
  - **Can Run In Parallel**: YES (within Wave 2)
  - **Parallel Group**: Wave 2 (with Tasks 8, 9, 11)
  - **Blocks**: Tasks 12, 14
  - **Blocked By**: None (can start in Wave 2)

  **References**:

  **Pattern References**:
  - `0-Base/MotorcycleRAG.Core/Options/FabricIngestionOptions.cs` — File to rename, class to rename, all 7 existing properties to preserve
  - `2-Application/MotorcycleRAG.Application/Pipeline/IngestionJobService.cs:6,20,26` — Uses `FabricIngestionOptions` (will be renamed)
  - `4-Persistence/MotorcycleRAG.Persistence/ExternalServices/FabricPipelineService.cs:9,26,32,41` — Uses `FabricIngestionOptions` (will be renamed)
  - `1-Presentation/MotorcycleRAG.API/Program.cs` — DI registration of options (binding section name may need update)

  **WHY Each Reference Matters**:
  - LSP rename will catch all usages, but the agent must verify the configuration section binding in Program.cs matches the new name
  - Existing properties must be preserved to keep Fabric mode as a fallback option

  **Acceptance Criteria**:
  - [x] File renamed to `IngestionOptions.cs`, class renamed to `IngestionOptions`
  - [x] `ProcessingMode` enum exists with `Local` and `Fabric` values
  - [x] `LocalProcessingEndpoint` and `LocalProcessingTimeoutMinutes` properties added
  - [x] All existing Fabric properties preserved
  - [x] `dotnet build MotorcycleRAG.sln --warnaserror` succeeds (zero warnings)
  - [x] No references to `FabricIngestionOptions` remain in codebase

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: No references to old class name remain
    Tool: Bash (grep)
    Preconditions: Rename completed
    Steps:
      1. Search entire solution for `FabricIngestionOptions` (excluding .git, bin, obj)
      2. Expect zero matches
      3. Search for `IngestionOptions` — expect matches in all files that previously referenced FabricIngestionOptions
    Expected Result: Zero references to FabricIngestionOptions, all converted to IngestionOptions
    Failure Indicators: Any remaining FabricIngestionOptions reference
    Evidence: .sisyphus/evidence/task-10-rename-check.txt

  Scenario: New properties exist with correct defaults
    Tool: Bash
    Preconditions: IngestionOptions.cs updated
    Steps:
      1. Read IngestionOptions.cs
      2. Verify ProcessingMode property with default Local
      3. Verify LocalProcessingEndpoint with default "http://localhost:8100"
      4. Verify LocalProcessingTimeoutMinutes with default 360
      5. Run `dotnet build MotorcycleRAG.sln --warnaserror`
    Expected Result: All new properties present with correct defaults, build succeeds
    Failure Indicators: Missing properties, wrong defaults, build failures
    Evidence: .sisyphus/evidence/task-10-properties-check.txt
  ```

  **Commit**: YES (groups with Task 9)
  - Message: `refactor(core): rename FabricIngestionOptions to IngestionOptions, add local processing config`
  - Files: `0-Base/MotorcycleRAG.Core/Options/IngestionOptions.cs`, all files updated by LSP rename
  - Pre-commit: `dotnet build MotorcycleRAG.sln --warnaserror`

- [x] 11. Create Azure AI Search Indexer Setup Script

  **What to do**:
  - Create `7-Deployment/scripts/setup-search-indexer.sh`
  - Azure CLI script that:
    1. Creates a blob data source connection pointing to `search-chunks` container
    2. Creates an indexer with `jsonLines` parsing mode
    3. Maps JSON fields to existing index fields (field names match 1:1 with camelCase)
    4. Sets schedule to every 2 hours (PT2H)
    5. Verifies index compatibility (1536-dim contentVector field exists)
  - Use Azure CLI `az search` commands
  - Script must accept parameters: search service name, resource group, storage account name
  - Include `--help` usage text
  - No skillset needed (embeddings are pre-computed by the Python service)

  **Must NOT do**:
  - Do NOT create or modify the search index schema — only create the indexer + data source
  - Do NOT include secrets in the script — use Azure CLI auth context
  - Do NOT make the script Windows-only — use bash for WSL compatibility

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Shell script with Azure CLI commands — no complex logic
  - **Skills**: [`azure-cli`]
    - `azure-cli`: Azure CLI commands for search service management
  - **Skills Evaluated but Omitted**:
    - `delegate-dotnet`: Not applicable — bash script

  **Parallelization**:
  - **Can Run In Parallel**: YES (within Wave 2)
  - **Parallel Group**: Wave 2 (with Tasks 8, 9, 10)
  - **Blocks**: Task 18
  - **Blocked By**: None

  **References**:

  **Pattern References**:
  - `4-Persistence/MotorcycleRAG.Persistence/Search/MotorcycleIndexingService.cs:176-253` — Index schema definition: all field names and types the indexer must map to
  - `6-Docs/agent-notes/plan-local-docling-ingestion.md:143-156` — Indexer requirements: jsonLines, PT2H schedule, no skillset

  **External References**:
  - Azure CLI search indexer: `https://learn.microsoft.com/en-us/cli/azure/search/indexer`
  - Blob indexer JSON Lines mode: `https://learn.microsoft.com/en-us/azure/search/search-howto-index-json-blobs`

  **WHY Each Reference Matters**:
  - Index schema is the ground truth for field mappings — indexer must map JSON keys to these exact field names
  - jsonLines parsing mode is critical — wrong mode will fail to parse the chunk files

  **Acceptance Criteria**:
  - [x] File `7-Deployment/scripts/setup-search-indexer.sh` exists
  - [x] Script is valid bash (`bash -n setup-search-indexer.sh` passes)
  - [x] Uses `jsonLines` parsing mode
  - [x] Accepts search service name, resource group, storage account as parameters
  - [x] Includes help text

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: Script has valid bash syntax
    Tool: Bash
    Preconditions: Script file exists
    Steps:
      1. Run `bash -n 7-Deployment/scripts/setup-search-indexer.sh`
      2. Check exit code is 0
    Expected Result: No syntax errors
    Failure Indicators: Syntax error at line N
    Evidence: .sisyphus/evidence/task-11-syntax-check.txt

  Scenario: Script uses correct parsing mode and schedule
    Tool: Bash (grep)
    Preconditions: Script exists
    Steps:
      1. Grep for `jsonLines` in script — must be present
      2. Grep for `PT2H` in script — must be present
      3. Grep for parameter handling ($1, $2, $3 or getopts)
    Expected Result: jsonLines mode, 2-hour schedule, parameterized inputs
    Failure Indicators: Wrong parsing mode, hardcoded values
    Evidence: .sisyphus/evidence/task-11-config-check.txt
  ```

  **Commit**: YES
  - Message: `feat(deployment): add Azure AI Search indexer setup script for blob data source`
  - Files: `7-Deployment/scripts/setup-search-indexer.sh`
  - Pre-commit: `bash -n 7-Deployment/scripts/setup-search-indexer.sh`

- [x] 12. Update IngestionJobService for Local Processing Mode

  **What to do**:
  - Modify `2-Application/MotorcycleRAG.Application/Pipeline/IngestionJobService.cs`
  - Add `ILocalPipelineService` as a constructor dependency (alongside existing `IFabricPipelineService`)
  - Add `IngestionOptions` (renamed from `FabricIngestionOptions`) to constructor
  - In `StartJobAsync`: branch on `_options.ProcessingMode`:
    - `ProcessingMode.Local`: call `_localPipeline.TriggerPipelineAsync(...)` instead of `_fabricPipeline.TriggerPipelineAsync(...)`
    - `ProcessingMode.Fabric`: existing Fabric logic (unchanged)
  - In status mapping: handle local run IDs (the Python service returns a UUID job_id, not a Fabric run ID)
  - Update the `FabricRunId` property usage — consider renaming to `ExternalRunId` or keeping `FabricRunId` with a comment noting it's used for both modes
  - Update XML doc comments to reflect dual-mode support
  - Update the `TODO` comment at line 181-184 about `MaxInputBytes`/`MaxRuntimeMinutes` — wire them from IngestionOptions

  **Must NOT do**:
  - Do NOT remove Fabric pipeline logic — keep it as the `ProcessingMode.Fabric` branch
  - Do NOT change the `IIngestionJobService` interface signature
  - Do NOT change the `IngestionJobStatusResponse` DTO shape
  - Do NOT add new dependencies beyond ILocalPipelineService and the renamed options

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: Complex branching logic in existing orchestration service with multiple dependencies
  - **Skills**: [`delegate-dotnet`, `clean-architecture`]
    - `delegate-dotnet`: C# modification of existing service
    - `clean-architecture`: Ensure Application layer stays clean (no infrastructure concerns)
  - **Skills Evaluated but Omitted**:
    - `dapper-sql`: No SQL changes

  **Parallelization**:
  - **Can Run In Parallel**: YES (within Wave 3, with Tasks 13, 14, 15)
  - **Parallel Group**: Wave 3
  - **Blocks**: Tasks 17, 18
  - **Blocked By**: Tasks 7, 9, 10

  **References**:

  **Pattern References**:
  - `2-Application/MotorcycleRAG.Application/Pipeline/IngestionJobService.cs` — FULL FILE: constructor (lines 23-32), StartJobAsync (lines 35-104), pipeline selection (lines 48-52, 68-74), status mapping (lines 92-93, 153-189)
  - `2-Application/MotorcycleRAG.Application/Pipeline/IngestionJobService.cs:181-184` — TODO comment about wiring MaxInputBytes/MaxRuntimeMinutes from options
  - `3-Domain/MotorcycleRAG.Contracts/Interfaces/ILocalPipelineService.cs` — Interface created in Task 7
  - `3-Domain/MotorcycleRAG.Contracts/Interfaces/IFabricPipelineService.cs` — Existing interface (kept alongside)

  **API/Type References**:
  - `0-Base/MotorcycleRAG.Core/Options/IngestionOptions.cs` — Renamed options with ProcessingMode, LocalProcessingEndpoint (from Task 10)
  - `MotorcycleRAG.Domain.Enums.IngestionJobType` — PDFManual, StructuredSpecification enum values
  - `MotorcycleRAG.Domain.Enums.IngestionJobStatus` — Queued, Processing, Failed, Completed statuses

  **WHY Each Reference Matters**:
  - IngestionJobService.cs is the orchestrator — understanding its full flow is essential before modifying branching logic
  - The TODO at line 181 is an existing known gap that should be fixed in this task
  - Both pipeline interfaces must coexist — the service selects based on ProcessingMode

  **Acceptance Criteria**:
  - [x] `IngestionJobService` constructor accepts both `IFabricPipelineService` and `ILocalPipelineService`
  - [x] `StartJobAsync` branches on `ProcessingMode.Local` vs `ProcessingMode.Fabric`
  - [x] Fabric path unchanged — existing behavior preserved
  - [x] Local path calls `ILocalPipelineService.TriggerPipelineAsync`
  - [x] TODO at line 181-184 resolved — MaxInputBytes/MaxRuntimeMinutes wired from options
  - [x] `dotnet build MotorcycleRAG.sln --warnaserror` succeeds

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: Solution builds with dual-mode IngestionJobService
    Tool: Bash
    Preconditions: Tasks 7, 9, 10 completed
    Steps:
      1. Run `dotnet build MotorcycleRAG.sln --warnaserror`
      2. Check exit code is 0
    Expected Result: Build succeeded. 0 Warning(s). 0 Error(s).
    Failure Indicators: Missing references, ambiguous calls, type mismatches
    Evidence: .sisyphus/evidence/task-12-dotnet-build.txt

  Scenario: ProcessingMode branching logic exists
    Tool: Bash (grep)
    Preconditions: IngestionJobService.cs updated
    Steps:
      1. Search IngestionJobService.cs for `ProcessingMode.Local` — must be found
      2. Search IngestionJobService.cs for `ProcessingMode.Fabric` — must be found
      3. Search for `ILocalPipelineService` in constructor — must be found
      4. Search for `IFabricPipelineService` in constructor — must still be present
    Expected Result: Both modes present, both pipeline services injected
    Failure Indicators: Missing mode branch, removed Fabric pipeline
    Evidence: .sisyphus/evidence/task-12-branching-check.txt
  ```

  **Commit**: YES (groups with Tasks 13-15)
  - Message: `feat(application): update IngestionJobService with dual-mode local/Fabric processing`
  - Files: `2-Application/MotorcycleRAG.Application/Pipeline/IngestionJobService.cs`
  - Pre-commit: `dotnet build MotorcycleRAG.sln --warnaserror`

- [x] 13. Create GraphEntityIngestionService

  **What to do**:
  - Create `2-Application/MotorcycleRAG.Application/Pipeline/GraphEntityIngestionService.cs`
  - Implement service that polls Azure Blob Storage for graph entity JSON after an ingestion job completes:
    - `async Task IngestGraphEntitiesAsync(string uploadId, CancellationToken ct)` — main method
    - Download `graph-entities/{uploadId}/entities.json` from blob storage
    - Deserialize JSON into list of `GraphNode` and list of `GraphEdge` domain entities
    - Call `IGraphRepository.DeleteByDocumentAsync(sourceDocumentId)` first (clean re-ingestion)
    - Call `IGraphRepository.UpsertNodesAsync(nodes)` to insert nodes
    - Call `IGraphRepository.UpsertEdgesAsync(edges)` to insert edges
    - Structured logging for each step
  - Constructor dependencies: `IGraphRepository`, `BlobServiceClient` (or a blob reader abstraction), `ILogger<GraphEntityIngestionService>`
  - Create `IGraphEntityIngestionService` interface in `3-Domain/MotorcycleRAG.Contracts/Interfaces/`
  - Handle edge cases: entities.json doesn't exist (job had no graph output), malformed JSON, empty entity list

  **Must NOT do**:
  - Do NOT put blob storage SDK calls directly in this service — use an abstraction or the existing blob service pattern
  - Do NOT modify `IGraphRepository` — it already has all needed methods
  - Do NOT put this in Persistence layer — it's Application-layer orchestration

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: Orchestration service with blob download, JSON deserialization, and repository batch operations
  - **Skills**: [`delegate-dotnet`, `clean-architecture`]
    - `delegate-dotnet`: C# service implementation
    - `clean-architecture`: Application layer placement, dependency on Contracts interfaces
  - **Skills Evaluated but Omitted**:
    - `dapper-sql`: SQL is handled by SqlGraphRepository, not this service

  **Parallelization**:
  - **Can Run In Parallel**: YES (within Wave 3)
  - **Parallel Group**: Wave 3 (with Tasks 12, 14, 15)
  - **Blocks**: Tasks 14, 17
  - **Blocked By**: Task 7 (needs interface pattern)

  **References**:

  **Pattern References**:
  - `2-Application/MotorcycleRAG.Application/Pipeline/IngestionJobService.cs` — Similar orchestration service pattern: constructor injection, structured logging, async methods
  - `2-Application/MotorcycleRAG.Application/AGENTS.md` — "Use-case orchestration" is what belongs here

  **API/Type References**:
  - `3-Domain/MotorcycleRAG.Contracts/Repositories/IGraphRepository.cs` — UpsertNodesAsync, UpsertEdgesAsync, DeleteByDocumentAsync — the repository methods this service will call
  - `3-Domain/MotorcycleRAG.Domain/Entities/GraphNode.cs` — Entity shape: Id, Name, Type, Description, SourceDocumentId, CreatedAtUtc, UpdatedAtUtc
  - `3-Domain/MotorcycleRAG.Domain/Entities/GraphEdge.cs` — Edge entity shape
  - `2-Application/local-processing-service/src/models/schemas.py:80-94` — Python `GraphEntity` Pydantic model — the JSON this service will deserialize

  **WHY Each Reference Matters**:
  - `IGraphRepository` defines the exact methods available — no new methods needed, just call existing ones
  - `GraphNode.cs` and `GraphEdge.cs` shapes must match the JSON produced by the Python graph extractor
  - Python `GraphEntity` schema shows what fields the JSON will contain — C# deserialization must handle this shape

  **Acceptance Criteria**:
  - [x] File `2-Application/MotorcycleRAG.Application/Pipeline/GraphEntityIngestionService.cs` exists
  - [x] Interface `IGraphEntityIngestionService` exists in `3-Domain/MotorcycleRAG.Contracts/Interfaces/`
  - [x] Service calls `IGraphRepository.DeleteByDocumentAsync` before upserting (clean re-ingestion)
  - [x] Service calls `UpsertNodesAsync` and `UpsertEdgesAsync` for batch operations
  - [x] Missing entities.json handled gracefully (log warning, return without error)
  - [x] `dotnet build MotorcycleRAG.sln --warnaserror` succeeds

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: Solution builds with GraphEntityIngestionService
    Tool: Bash
    Preconditions: IGraphRepository exists, GraphNode/GraphEdge exist
    Steps:
      1. Run `dotnet build MotorcycleRAG.sln --warnaserror`
      2. Check exit code is 0
    Expected Result: Build succeeded
    Failure Indicators: Missing type references, namespace errors
    Evidence: .sisyphus/evidence/task-13-dotnet-build.txt

  Scenario: Service uses correct repository methods
    Tool: Bash (grep)
    Preconditions: GraphEntityIngestionService.cs exists
    Steps:
      1. Grep for `DeleteByDocumentAsync` — must be called before upserts
      2. Grep for `UpsertNodesAsync` — must be present
      3. Grep for `UpsertEdgesAsync` — must be present
      4. Grep for `IGraphRepository` in constructor — must be injected
    Expected Result: All three repository methods used, repository injected
    Failure Indicators: Missing delete-before-upsert, missing batch methods
    Evidence: .sisyphus/evidence/task-13-repository-usage.txt
  ```

  **Commit**: YES (groups with Tasks 12, 14, 15)
  - Message: `feat(application): add GraphEntityIngestionService for SQL Graph population`
  - Files: `2-Application/MotorcycleRAG.Application/Pipeline/GraphEntityIngestionService.cs`, `3-Domain/MotorcycleRAG.Contracts/Interfaces/IGraphEntityIngestionService.cs`
  - Pre-commit: `dotnet build MotorcycleRAG.sln --warnaserror`

- [x] 14. Update Program.cs DI Registrations

  **What to do**:
  - Update `1-Presentation/MotorcycleRAG.API/Program.cs`:
    - Register `ILocalPipelineService` → `LocalPipelineService` as scoped/transient service
    - Register `IGraphEntityIngestionService` → `GraphEntityIngestionService`
    - Update options binding from `"FabricIngestion"` section to `"Ingestion"` section
    - Register named `HttpClient` for `LocalPipelineService` with base address from `IngestionOptions.LocalProcessingEndpoint`
    - Keep existing `IFabricPipelineService` registration (for Fabric mode fallback)
  - Follow existing DI registration patterns in Program.cs

  **Must NOT do**:
  - Do NOT remove existing Fabric service registrations
  - Do NOT change the order of existing middleware/service registrations
  - Do NOT add conditional registration based on ProcessingMode (register both, let runtime branching handle it)

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: DI registration additions — straightforward Program.cs updates
  - **Skills**: [`delegate-dotnet`, `clean-architecture`]
    - `delegate-dotnet`: C# DI registration patterns
    - `clean-architecture`: Correct service registration

  **Parallelization**:
  - **Can Run In Parallel**: NO (depends on Tasks 9, 10, 12, 13 completing)
  - **Parallel Group**: Wave 3 (after other Wave 3 tasks)
  - **Blocks**: Task 17
  - **Blocked By**: Tasks 9, 10, 12, 13

  **References**:

  **Pattern References**:
  - `1-Presentation/MotorcycleRAG.API/Program.cs` — Existing DI registration patterns for services, options binding, HttpClient registration
  - `4-Persistence/MotorcycleRAG.Persistence/ExternalServices/FabricPipelineService.cs:97` — Named HttpClient pattern: `_httpClientFactory.CreateClient(nameof(FabricPipelineService))`

  **WHY Each Reference Matters**:
  - Program.cs has established DI patterns — new registrations must follow the same style
  - Named HttpClient pattern must be consistent with FabricPipelineService

  **Acceptance Criteria**:
  - [x] `ILocalPipelineService` registered in DI
  - [x] `IGraphEntityIngestionService` registered in DI
  - [x] Options bound to `"Ingestion"` section (not `"FabricIngestion"`)
  - [x] Named HttpClient registered for LocalPipelineService
  - [x] `dotnet build MotorcycleRAG.sln --warnaserror` succeeds

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: DI registrations compile and resolve
    Tool: Bash
    Preconditions: All Wave 3 tasks completed
    Steps:
      1. Run `dotnet build MotorcycleRAG.sln --warnaserror`
      2. Check exit code is 0
    Expected Result: Build succeeded — all services resolvable
    Failure Indicators: Missing type, unresolvable dependency
    Evidence: .sisyphus/evidence/task-14-dotnet-build.txt

  Scenario: New services registered in Program.cs
    Tool: Bash (grep)
    Preconditions: Program.cs updated
    Steps:
      1. Grep Program.cs for `ILocalPipelineService` — must be found
      2. Grep Program.cs for `IGraphEntityIngestionService` — must be found
      3. Grep Program.cs for `"Ingestion"` section binding — must be found
      4. Grep Program.cs for `"FabricIngestion"` — must NOT be found (renamed)
    Expected Result: All new registrations present, old section name gone
    Failure Indicators: Missing registrations, old config section name
    Evidence: .sisyphus/evidence/task-14-di-check.txt
  ```

  **Commit**: YES (groups with Tasks 12, 13, 15)
  - Message: `feat(api): register local pipeline and graph entity services in DI`
  - Files: `1-Presentation/MotorcycleRAG.API/Program.cs`
  - Pre-commit: `dotnet build MotorcycleRAG.sln --warnaserror`

- [x] 15. Mark Fabric Code as [Obsolete]

  **What to do**:
  - Add `[Obsolete("Use LocalPipelineService instead. Fabric mode retained as fallback.")]` to:
    - `FabricPipelineService` class in `4-Persistence/MotorcycleRAG.Persistence/ExternalServices/FabricPipelineService.cs`
    - `MotorcyclePDFProcessor` class in `4-Persistence/MotorcycleRAG.Persistence/DataProcessing/MotorcyclePDFProcessor.cs`
    - `MotorcycleCSVProcessor` class in `4-Persistence/MotorcycleRAG.Persistence/DataProcessing/MotorcycleCSVProcessor.cs`
  - Suppress obsolete warnings in `IngestionJobService.cs` where Fabric service is still referenced (it's intentionally kept for fallback)
  - Do NOT delete any files or remove any code — only add attributes

  **Must NOT do**:
  - Do NOT delete any Fabric-related files
  - Do NOT remove any code — only add `[Obsolete]` attributes
  - Do NOT mark `IFabricPipelineService` as obsolete (interface stays clean)

  **Recommended Agent Profile**:
  - **Category**: `quick`
    - Reason: Adding attributes to 3 files — trivial
  - **Skills**: [`delegate-dotnet`]
    - `delegate-dotnet`: C# attribute addition

  **Parallelization**:
  - **Can Run In Parallel**: YES (within Wave 3)
  - **Parallel Group**: Wave 3 (with Tasks 12, 13, 14)
  - **Blocks**: Task 17
  - **Blocked By**: None

  **References**:

  **Pattern References**:
  - `4-Persistence/MotorcycleRAG.Persistence/ExternalServices/FabricPipelineService.cs:19` — Class declaration to add [Obsolete] to
  - `6-Docs/agent-notes/plan-local-docling-ingestion.md:160-162` — Deprecation instructions from original plan

  **WHY Each Reference Matters**:
  - Exact class declarations needed to know where to place the attribute
  - Original plan explicitly says mark obsolete, don't delete

  **Acceptance Criteria**:
  - [x] `FabricPipelineService` has `[Obsolete]` attribute
  - [x] `MotorcyclePDFProcessor` has `[Obsolete]` attribute (if file exists)
  - [x] `MotorcycleCSVProcessor` has `[Obsolete]` attribute (if file exists)
  - [x] `dotnet build MotorcycleRAG.sln` succeeds (warnings expected from [Obsolete], not errors)
  - [x] No files deleted

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: Obsolete attributes present on correct classes
    Tool: Bash (grep)
    Preconditions: Attributes added
    Steps:
      1. Grep FabricPipelineService.cs for `[Obsolete` — must be found
      2. Grep MotorcyclePDFProcessor.cs for `[Obsolete` — must be found (if file exists)
      3. Grep MotorcycleCSVProcessor.cs for `[Obsolete` — must be found (if file exists)
      4. Run `dotnet build MotorcycleRAG.sln` — must succeed (warnings OK)
    Expected Result: All 3 classes marked obsolete, build succeeds
    Failure Indicators: Missing attributes, build errors
    Evidence: .sisyphus/evidence/task-15-obsolete-check.txt
  ```

  **Commit**: YES (groups with Tasks 12-14)
  - Message: `chore(persistence): mark Fabric pipeline code as [Obsolete]`
  - Files: `FabricPipelineService.cs`, `MotorcyclePDFProcessor.cs`, `MotorcycleCSVProcessor.cs`
  - Pre-commit: `dotnet build MotorcycleRAG.sln`

- [x] 16. Python Unit Tests (pytest)

  **What to do**:
  - Create `2-Application/local-processing-service/tests/` directory with `__init__.py` and `conftest.py`
  - Create test files:
    - `tests/test_pdf_processor.py` — test PDFProcessor with mocked blob_writer, embedder, graph_extractor
    - `tests/test_csv_processor.py` — test CSVProcessor with mocked dependencies
    - `tests/test_ollama_embedder.py` — test OllamaEmbedder dimension validation, retry logic, status check
    - `tests/test_graph_extractor.py` — test GraphExtractor with mocked Ollama, malformed output handling
    - `tests/test_blob_writer.py` — test BlobWriter JSONL format, auth pattern
  - Test patterns:
    - Use `pytest-asyncio` for async test methods
    - Use `unittest.mock.AsyncMock` for mocking async dependencies
    - Test happy paths and error cases
    - Verify dimension validation raises ValueError for wrong vector size
    - Verify malformed LLM output returns empty list (not crash)

  **Must NOT do**:
  - Do NOT require Ollama or Azure to be running for unit tests — mock all external calls
  - Do NOT create integration tests here (that's Task 18)

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Multiple test files with mocking patterns across 5 modules
  - **Skills**: [`build-commands`]
    - `build-commands`: Run pytest

  **Parallelization**:
  - **Can Run In Parallel**: YES (within Wave 4)
  - **Parallel Group**: Wave 4 (with Tasks 17, 18)
  - **Blocks**: F1-F4
  - **Blocked By**: Task 8 (all Python modules wired)

  **References**:

  **Pattern References**:
  - `2-Application/local-processing-service/pyproject.toml:53-60` — pytest configuration (testpaths, markers, addopts)
  - All Python modules from Tasks 2-6 — the code being tested

  **Acceptance Criteria**:
  - [x] `cd 2-Application/local-processing-service && python -m pytest tests/ -v` passes
  - [x] At least 2 tests per module (happy path + error case)
  - [x] All tests run without Ollama or Azure connection (fully mocked)
  - [x] No bare `except:` blocks in test code

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: All Python tests pass
    Tool: Bash
    Preconditions: All Python modules exist
    Steps:
      1. cd 2-Application/local-processing-service
      2. Run `python -m pytest tests/ -v --tb=short`
      3. Check exit code is 0
    Expected Result: All tests pass, 0 failures
    Failure Indicators: Any test failure, import errors in tests
    Evidence: .sisyphus/evidence/task-16-pytest-results.txt
  ```

  **Commit**: YES (groups with Tasks 17, 18)
  - Message: `test(local-processing): add unit tests for all Python modules`
  - Files: `2-Application/local-processing-service/tests/**`
  - Pre-commit: `cd 2-Application/local-processing-service && python -m pytest tests/ -v`

- [x] 17. .NET Unit Tests (xUnit)

  **What to do**:
  - Add tests in `5-Test/tests/MotorcycleRAG.UnitTests/` (or appropriate test project):
    - `LocalPipelineServiceTests.cs` — mock IHttpClientFactory, verify correct HTTP calls for PDF/CSV
    - `IngestionJobServiceLocalModeTests.cs` — mock ILocalPipelineService, verify local mode branching
    - `GraphEntityIngestionServiceTests.cs` — mock IGraphRepository, verify delete-before-upsert, batch operations
    - `IngestionOptionsTests.cs` — verify ProcessingMode enum, default values
  - Test patterns:
    - Use Moq for mocking
    - Use xUnit `[Fact]` and `[Theory]` attributes
    - Follow existing test patterns in the test project
    - Test both ProcessingMode.Local and ProcessingMode.Fabric paths in IngestionJobService

  **Must NOT do**:
  - Do NOT require the Python service to be running for unit tests
  - Do NOT create integration tests (that's Task 18)

  **Recommended Agent Profile**:
  - **Category**: `unspecified-high`
    - Reason: Multiple test files with Moq mocking across 3 services
  - **Skills**: [`delegate-dotnet`, `build-commands`]
    - `delegate-dotnet`: C# xUnit test writing
    - `build-commands`: Run dotnet test

  **Parallelization**:
  - **Can Run In Parallel**: YES (within Wave 4)
  - **Parallel Group**: Wave 4 (with Tasks 16, 18)
  - **Blocks**: F1-F4
  - **Blocked By**: Tasks 12, 13, 14, 15

  **References**:

  **Pattern References**:
  - `5-Test/` — Existing test project structure and patterns
  - `2-Application/MotorcycleRAG.Application/Pipeline/IngestionJobService.cs` — Service being tested
  - `4-Persistence/MotorcycleRAG.Persistence/ExternalServices/LocalPipelineService.cs` — Service being tested
  - `2-Application/MotorcycleRAG.Application/Pipeline/GraphEntityIngestionService.cs` — Service being tested

  **Acceptance Criteria**:
  - [x] `dotnet test` passes with all new tests green
  - [x] At least 2 tests per service (happy path + error case)
  - [x] LocalPipelineService tests verify correct HTTP method and URL construction
  - [x] IngestionJobService tests verify both Local and Fabric mode branches

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: All .NET tests pass
    Tool: Bash
    Preconditions: All .NET services implemented
    Steps:
      1. Run `dotnet test MotorcycleRAG.sln --verbosity normal`
      2. Check exit code is 0
    Expected Result: All tests pass, 0 failures
    Failure Indicators: Test failures, build errors in test project
    Evidence: .sisyphus/evidence/task-17-dotnet-test-results.txt
  ```

  **Commit**: YES (groups with Tasks 16, 18)
  - Message: `test(dotnet): add unit tests for local pipeline integration services`
  - Files: `5-Test/tests/MotorcycleRAG.UnitTests/**`
  - Pre-commit: `dotnet test MotorcycleRAG.sln`

- [x] 18. Integration Test — End-to-End Flow

  **What to do**:
  - Create an integration test script or test class that verifies the full pipeline:
    1. Start the Python FastAPI service
    2. Send a small test PDF via curl to `/process/pdf`
    3. Poll `/jobs/{job_id}` until status is `completed` or `failed`
    4. Verify chunks JSON Lines file exists in blob storage
    5. Verify graph entities JSON file exists in blob storage
    6. Verify chunk JSON contains all required fields with correct types
    7. Verify contentVector has exactly 1536 dimensions
    8. Verify JSON Lines format (each line is standalone JSON)
    9. Test error case: send invalid document type, verify 400 response
    10. Test health endpoint returns connected status
  - This test requires Ollama running with qwen3-embedding:4b and qwen3:4b models
  - This test requires Azure Blob Storage access (or Azurite emulator)
  - Mark as `@pytest.mark.slow` or separate integration test suite

  **Must NOT do**:
  - Do NOT run this test in CI/CD (requires GPU + Ollama)
  - Do NOT skip dimension validation

  **Recommended Agent Profile**:
  - **Category**: `deep`
    - Reason: End-to-end flow with service startup, HTTP calls, blob verification, and cleanup
  - **Skills**: [`build-commands`, `azure-cli`]
    - `build-commands`: Service startup and test execution
    - `azure-cli`: Blob storage verification

  **Parallelization**:
  - **Can Run In Parallel**: YES (within Wave 4)
  - **Parallel Group**: Wave 4 (with Tasks 16, 17)
  - **Blocks**: F1-F4
  - **Blocked By**: Tasks 8, 11, 12, 13

  **References**:

  **Pattern References**:
  - `2-Application/local-processing-service/src/main.py` — All endpoints to test
  - `2-Application/local-processing-service/src/models/schemas.py` — Expected request/response shapes
  - `4-Persistence/MotorcycleRAG.Persistence/Search/MotorcycleIndexingService.cs:176-253` — Index schema to validate chunk output against

  **Acceptance Criteria**:
  - [x] Integration test script exists
  - [ ] PDF processing end-to-end works (input → chunks → blob)
  - [ ] Chunk vectors are exactly 1536 dimensions
  - [ ] JSON Lines format valid (each line parses independently)
  - [ ] Error cases handled (invalid input returns 400, not 500)

  **QA Scenarios (MANDATORY):**

  ```
  Scenario: End-to-end PDF processing flow
    Tool: Bash (curl)
    Preconditions: Python service running on port 8100, Ollama running, Azure Blob accessible
    Steps:
      1. Start service if not running
      2. curl -X POST http://localhost:8100/process/pdf -H 'Content-Type: application/json' -d '{"upload_id": "test-pdf-001", "document_type": "manual-pdf", "blob_container": "uploads", "metadata": {"make": "Honda", "model": "CBR600RR", "year": 2024}}'
      3. Parse job_id from response
      4. Poll GET /jobs/{job_id} every 5s until status != 'processing' (timeout: 300s)
      5. Verify final status is 'completed'
      6. Check blob storage for search-chunks/test-pdf-001/chunks.jsonl
      7. Parse first line of chunks.jsonl, verify 'contentVector' has 1536 elements
    Expected Result: Job completes, chunks written to blob, vectors are 1536-dim
    Failure Indicators: Job fails, no blob output, wrong vector dimensions
    Evidence: .sisyphus/evidence/task-18-e2e-pdf.txt

  Scenario: Invalid input returns error
    Tool: Bash (curl)
    Preconditions: Service running
    Steps:
      1. curl -X POST http://localhost:8100/process/pdf -H 'Content-Type: application/json' -d '{}'
      2. Check HTTP status is 400 or 422
    Expected Result: Validation error response, not 500 server error
    Failure Indicators: 500 Internal Server Error, unhandled exception
    Evidence: .sisyphus/evidence/task-18-error-handling.txt
  ```

  **Commit**: YES (groups with Tasks 16, 17)
  - Message: `test: add integration test for end-to-end local processing pipeline`
  - Files: Integration test script/file
  - Pre-commit: None (requires live services)

---

## Final Verification Wave (MANDATORY — after ALL implementation tasks)

> 4 review agents run in PARALLEL. ALL must APPROVE. Rejection → fix → re-run.

- [x] F1. **Plan Compliance Audit** — `oracle`
  Read the plan end-to-end. For each "Must Have": verify implementation exists (read file, curl endpoint, run command). For each "Must NOT Have": search codebase for forbidden patterns — reject with file:line if found. Check evidence files exist in .sisyphus/evidence/. Compare deliverables against plan.
  Output: `Must Have [N/N] | Must NOT Have [N/N] | Tasks [N/N] | VERDICT: APPROVE/REJECT`

- [x] F2. **Code Quality Review** — `unspecified-high`
  Run `dotnet build MotorcycleRAG.sln` + `dotnet test`. Review all changed files for: `as any`/`@ts-ignore`, empty catches, console.log in prod, commented-out code, unused imports. For Python: run `black --check`, `isort --check`, verify no bare `except:` blocks, no f-string injection in SQL/logs. Check AI slop: excessive comments, over-abstraction, generic names.
  Output: `Build [PASS/FAIL] | Lint [PASS/FAIL] | Tests [N pass/N fail] | Files [N clean/N issues] | VERDICT`

- [x] F3. **Real Manual QA** — `unspecified-high`
  Start from clean state. Start the Python FastAPI service. Send a small test PDF via curl to `/process/pdf`. Verify chunks appear in blob storage. Verify graph entities appear in blob storage. Test health endpoint. Test error cases (invalid input, Ollama down). Save evidence to `.sisyphus/evidence/final-qa/`.
  Output: `Scenarios [N/N pass] | Integration [N/N] | Edge Cases [N tested] | VERDICT`

- [x] F4. **Scope Fidelity Check** — `deep`
  For each task: read "What to do", read actual diff (git log/diff). Verify 1:1 — everything in spec was built (no missing), nothing beyond spec was built (no creep). Check "Must NOT do" compliance. Detect cross-task contamination: Task N touching Task M's files. Flag unaccounted changes.
  Output: `Tasks [N/N compliant] | Contamination [CLEAN/N issues] | Unaccounted [CLEAN/N files] | VERDICT`

---

## Commit Strategy

| Group | Message | Files | Pre-commit |
|-------|---------|-------|------------|
| Tasks 1-6 | `feat(local-processing): implement Python FastAPI service modules` | `2-Application/local-processing-service/src/**` | `cd 2-Application/local-processing-service && python -m pytest tests/ -x` |
| Task 7 | `feat(contracts): add ILocalPipelineService interface` | `3-Domain/MotorcycleRAG.Contracts/Interfaces/ILocalPipelineService.cs` | `dotnet build MotorcycleRAG.sln --no-restore` |
| Task 8 | `feat(local-processing): wire all modules in main.py` | `2-Application/local-processing-service/src/main.py` | curl health check |
| Tasks 9-10 | `feat(persistence): add LocalPipelineService + rename IngestionOptions` | `.cs files` | `dotnet build` |
| Task 11 | `feat(deployment): add Azure AI Search indexer setup script` | `7-Deployment/scripts/setup-search-indexer.sh` | `bash -n` syntax check |
| Tasks 12-15 | `feat(application): update IngestionJobService for local mode + deprecate Fabric` | `.cs files` | `dotnet build && dotnet test` |
| Tasks 16-18 | `test: add unit and integration tests for local pipeline` | `5-Test/**`, `tests/**` | `dotnet test && pytest` |

---

## Success Criteria

### Verification Commands
```bash
# Python service health
curl -s http://localhost:8100/health | python -m json.tool  # Expected: {"status": "healthy", "services": {"ollama": ...}}

# .NET build
dotnet build MotorcycleRAG.sln --warnaserror  # Expected: Build succeeded. 0 Warning(s). 0 Error(s).

# .NET tests
dotnet test MotorcycleRAG.sln  # Expected: All tests passed

# Python tests
cd 2-Application/local-processing-service && python -m pytest tests/ -v  # Expected: All tests passed
```

### Final Checklist
- [x] All "Must Have" present
- [x] All "Must NOT Have" absent
- [x] All .NET tests pass
- [x] All Python tests pass
- [ ] Python service starts and responds to health check
- [x] Chunk JSON Lines format matches Azure AI Search index schema
- [x] Embedding vectors are exactly 1536 dimensions
- [x] No secrets in source code
- [x] Fabric code marked [Obsolete] but not deleted
