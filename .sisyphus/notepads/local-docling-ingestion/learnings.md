# Learnings — local-docling-ingestion

## [2026-03-01T09:07:46Z] Initial codebase scan

### Python Service Structure
- Scaffold exists at `2-Application/local-processing-service/`
- Files: `Dockerfile`, `pyproject.toml`, `src/` 
- Src subdirs already created: `processors/`, `embeddings/`, `extraction/`, `storage/`, `models/`
- But ALL subdirs are empty (only `models/schemas.py` exists)
- `main.py` exists with imports for modules that need to be created
- `.env.example` needs updating (no separate copy found yet)

### .NET Solution Structure
- `3-Domain/MotorcycleRAG.Contracts/Interfaces/` — existing interfaces (IFabricPipelineService.cs exists)
- `4-Persistence/MotorcycleRAG.Persistence/ExternalServices/FabricPipelineService.cs` — reference implementation
- `0-Base/MotorcycleRAG.Core/Options/FabricIngestionOptions.cs` — to be renamed → IngestionOptions
- `2-Application/MotorcycleRAG.Application/Pipeline/IngestionJobService.cs` — main orchestrator to modify
- NO `LocalPipelineService.cs` or `ILocalPipelineService.cs` exist yet
- NO `GraphEntityIngestionService.cs` exists yet

### Key Conventions
- 1 class or interface per file (AGENTS.md)
- Namespace: `MotorcycleRAG.Contracts.Interfaces` for Contracts
- Follow FabricPipelineService.cs as reference for new services
- Use IHttpClientFactory, not raw HttpClient
- All dependencies: ArgumentNullException.ThrowIfNull in constructors
- Structured logging: ILogger<T> pattern

### Python Conventions
- No hardcoded secrets — use env vars or DefaultAzureCredential
- No bare `except:` — use specific exception types
- No excessive comments or docstrings
- Poetry-based project (pyproject.toml), not requirements.txt
- Azure Blob: DefaultAzureCredential primary, AZURE_STORAGE_CONNECTION_STRING fallback for local dev only

## [2026-03-01T09:15:00Z] CSVProcessor Implementation (Task 3)
- `processors/` dir didn't exist yet — created with __init__.py
- CSVProcessor uses module-level `_jobs` dict for job tracking
- Background processing via `asyncio.create_task` — returns job_id immediately
- Grouping: normalizes columns to lowercase, groups by make/model/year if present
- Chunk output uses camelCase keys for Azure AI Search compatibility (20 fields)
- Empty CSV → 'failed' status, no crash
- pandas needed `pip install` — wasn't in poetry env by default in this worktree


## [2026-03-01] Scaffold Fix — Dockerfile, main.py, .env.example
- CUDA base image (`nvidia/cuda:12.3.1-runtime-ubuntu22.04`) needs `python3` + `python3-pip` installed via apt (not pre-installed like `python:3.9-slim`)
- Need `ln -sf /usr/bin/python3 /usr/bin/python` symlink for `poetry` and `uvicorn` to work
- `nvidia-driver-470` apt install fails on ubuntu:22.04 CUDA runtime image — GPU drivers come from the host/NVIDIA runtime
- NVIDIA Container Toolkit setup blocks (gpgkey, repo list) are NOT needed inside the container — handled by host
- `WORKDIR /app` must appear BEFORE `COPY pyproject.toml` — otherwise files land in `/`

## [2026-03-01] GraphExtractor Implementation (Task 8)
- `extraction/` package: `__init__.py` (empty) + `graph_extractor.py`
- Uses `ollama.AsyncClient` (not sync Client) for async extraction
- Env vars: `OLLAMA_HOST` (default localhost:11434), `OLLAMA_MODEL_LLM` (default qwen3:4b)
- Output uses camelCase keys matching .NET GraphNode/GraphEdge entities
- Return shape: `[{"nodes": [...], "edges": [...]}]` — single-element list wrapping full extraction
- NEVER raises from `extract()` — wraps entire body in try/except, returns `[]` on any error
- Empty/whitespace text guard returns `[]` without calling Ollama
## PDFProcessor Creation (2026-03-01)

- `extraction/graph_extractor.py` already exists under `src/extraction/` — glob didn't find it but `ls` did (Windows path issue with glob)
- `GraphExtractor.extract(text)` is async, returns list, never raises — safe to call without try/except
- `BlobWriter.upload_json(container, path, data)` handles json serialization internally — no need to import `json` in consumers
- `BlobWriter.upload_jsonl(container, path, records: list[dict])` — uses `"\n".join(json.dumps(r) for r in records)`
- Docling imports: `from docling.document_converter import DocumentConverter` and `from docling.chunking import HybridChunker`
- HybridChunker chunk object: `chunk.text` for content, `chunk.meta.headings` for heading list, `chunk.meta.doc_items[0].prov[0].page_no` for page number
- Must check `chunk.meta.doc_items and chunk.meta.doc_items[0].prov` before accessing page_no (can be None/empty)
- `headings` can be falsy (None or empty) — always guard with `if chunk.meta.headings`
- Module-level `_jobs` dict pattern: matches csv_processor.py exactly
- `asyncio.to_thread()` used for blocking Docling calls (convert, chunk)
- `tempfile.NamedTemporaryFile(suffix=".pdf", delete=False)` + `os.unlink` in `finally` for cleanup
- pip installed docling+deps successfully — docling v2.75.0, docling-core v2.66.0

## [2026-03-01] T16: pytest unit tests for local-processing-service

### Test Structure
- 5 test files + `__init__.py` + `conftest.py` in `tests/`
- 34 tests total, all passing in ~16s
- `asyncio_mode = "auto"` added to `[tool.pytest.ini_options]` in pyproject.toml

### Mock Strategies
- **CSVProcessor/PDFProcessor**: Accept deps via constructor → pass `MagicMock` with `AsyncMock` methods directly. No patching needed for these.
- **OllamaEmbedder**: Must `@patch("embeddings.ollama_embedder.ollama.AsyncClient")` at module level since `__init__` creates the client.
- **GraphExtractor**: Same approach — patch `ollama.AsyncClient` at module level. The `.chat()` return is `SimpleNamespace(message=SimpleNamespace(content=json_str))`.
- **BlobWriter**: Most complex — `__init__` reads env vars and creates `BlobServiceClient`. Strategy: `patch.dict("os.environ")` + `reload(module)` for instantiation tests, or `BlobWriter.__new__` + set `_client` directly for method tests. The upload methods use `asyncio.to_thread` wrapping sync calls, so mocking the sync blob client methods is sufficient.
- **PDFProcessor background tasks**: Must `@patch("processors.pdf_processor.DocumentConverter")` and `@patch("processors.pdf_processor.HybridChunker")` to avoid real Docling conversion. Fake chunks use `SimpleNamespace` with `.text`, `.meta.headings`, `.meta.doc_items[0].prov[0].page_no`.

### Key Pattern: Module-level `_jobs` dict
- Processors use `_jobs: dict[str, dict] = {}` at module level for job tracking.
- Must `_jobs.clear()` in `autouse` fixtures to prevent cross-test contamination.
- `asyncio.create_task` fires background processing — tests must `await asyncio.sleep()` + `asyncio.wait()` for background tasks to complete before asserting.

### Gotchas
- `pytest-asyncio` 1.3.0 installed (compatible with `asyncio_mode = "auto"`).
- `PYTHONPATH=src` required when running pytest because source modules live under `src/`.
- `BlobWriter._upload_bytes` uses `asyncio.to_thread` wrapping sync operations — the sync mock is called inside the thread. Works fine with `MagicMock`.
- Docling's `HybridChunker` and `DocumentConverter` are heavy imports — patching at module path (`processors.pdf_processor.DocumentConverter`) is essential.

## [Task 17] xUnit .NET unit tests
- Moq.Contrib.HttpClient pattern: `handler.SetupRequest(HttpMethod.Post, url).ReturnsResponse(...)` and `handler.VerifyRequest(...)` for verifying HTTP calls
- IFabricPipelineService is [Obsolete] — CS0618 warnings expected in tests
- IngestionJobService uses `?? throw` not `ThrowIfNull` — parameter names match ctor params
- Pre-existing failures: AzureAIConfigurationValidatorTests.Validate_WithInvalidFoundryEndpoint (2 tests)
- LocalPipelineService routes: "manual-pdf" → /process/pdf, "spec-dataset" → /process/csv

## [2026-03-01] T18: Integration Verification

### Verified Integration Points
- **DI wiring complete**: DataPipelineConfiguration.cs registers ILocalPipelineService, IGraphEntityIngestionService, IFabricPipelineService, IngestionOptions, and named HTTP client for LocalPipelineService.
- **Dual-mode branching works**: IngestionJobService checks `_options.Mode == ProcessingMode.Local` and delegates to the appropriate pipeline service. Both paths tested via IngestionJobServiceDualModeTests.
- **Python modules all importable**: PDFProcessor, CSVProcessor, OllamaEmbedder, GraphExtractor, BlobWriter all import cleanly with `PYTHONPATH=src`.
- **Full build chain compiles**: Core → Domain → Contracts.Models → Contracts → Persistence → Application → API → UnitTests — 0 warnings, 0 errors.

### Test Results
- .NET Unit Tests: 321 passed, 2 failed (pre-existing FoundryEndpoint validator tests), 10 skipped (integration tests needing Azure services)
- Python Tests: 34/34 passed across all 5 test modules

### Gaps Found
- **No [Obsolete] markers on Fabric code**: FabricPipelineService, IFabricPipelineService, and FabricIngestionOptions lack `[Obsolete]` attributes. This was not part of T18's scope but should be tracked if Fabric deprecation is planned.

### Key Patterns
- The `workdir` parameter in tooling doubles `/d/` paths — use `D:/` drive-letter paths or run from the directory directly to avoid MSBuild path resolution issues.
- Named HTTP client pattern: `services.AddHttpClient(nameof(MotorcycleRAG.Persistence.ExternalServices.LocalPipelineService))` — matches the `IHttpClientFactory.CreateClient()` call in LocalPipelineService.

## [2026-03-01] Fix A/B/C Applied
- graph_extractor.extract() now takes optional source_document_id param
- Each node dict gets sourceDocumentId injected post-LLM-parse (not in prompt — more reliable)
- pdf_processor.py passes upload_id as source_document_id to extract()
- main.py: 3 endpoints now log exception + return generic "An unexpected error occurred" detail
- main.py: Added `except HTTPException: raise` before broad `except Exception` to preserve 400 validation errors
- pdf_processor.py: job status error field now returns safe message, not raw exception
- pytest: 35 tests passing after fixes (34 original + 1 new test_returns_sourceDocumentId_on_nodes)

## [2026-03-01] F3: Real Manual QA
- Python: 35 tests passed, 0 failures
- .NET: 321 passed, 2 pre-existing failures (AzureAIConfigurationValidatorTests.Validate_WithInvalidFoundryEndpoint_ShouldReturnFailure), 10 skipped (integration tests requiring Azure services)
- Verdict: PASS
