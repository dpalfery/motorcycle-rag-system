# Learnings — hybrid-embedding-pipeline

## 2026-03-01 — OllamaEmbedder MRL truncation location
File: `src/embeddings/ollama_embedder.py`
- `self._dims = 1536` at line ~30 (constructor)
- Truncation block at lines ~55-57: `if len(vector) > self._dims: vector = vector[:self._dims]`
- Both must be updated in T02: change dims to 3584, remove truncation block

## 2026-03-01 — AzureOpenAIClientWrapper mock locations
File: `4-Persistence/MotorcycleRAG.Persistence/Azure/AzureOpenAIClientWrapper.cs`
- Mock embedding inner lambda at lines ~127-131: `Enumerable.Range(0, 1536)` → change to 3584
- Fallback lambda at line ~139: `new float[1536]` → change to 3584
- Both updated in T00 (schema wave) since they are dimension constants

## 2026-03-01 — MotorcycleIndexingService.cs dimension location
File: `4-Persistence/MotorcycleRAG.Persistence/Search/MotorcycleIndexingService.cs`
- `VectorSearchDimensions = 1536` at line 225
- Comment: `// OpenAI text-embedding-3-large dimensions`
- Target: change to 3584, update comment to `// Qwen3-Embedding-4B native dimensions (no MRL truncation)`

## 2026-03-01 — LSP errors in .NET Persistence project are pre-existing
The LSP errors on `AzureOpenAIClientWrapper.cs`, `AzureSearchClientWrapper.cs`, `MotorcycleIndexingService.cs` etc. (Azure namespace not found, ILogger not found, etc.) are **pre-existing** and appear to be caused by LSP not restoring NuGet packages in this session context. They are NOT introduced by our changes. `dotnet build` is the authoritative verification — use that, not LSP diagnostics.

## 2026-03-01 — DeepInfra API
- Endpoint: `https://api.deepinfra.com/v1/openai` (OpenAI-compatible base URL)
- Model: `Qwen/Qwen3-Embedding-4B`
- Auth: `Authorization: Bearer {DEEPINFRA_API_KEY}`
- Native output: 3584 dims
- Price: $0.020/1M tokens

## 2026-03-01 — Azure AI Foundry Local
- Runs a local OpenAI-compatible server, default port 5272
- API key can be any non-empty string (e.g. `"local"`)
- Model name in request matches whatever is loaded (env var `AZURE_FOUNDRY_LOCAL_EMBEDDING_MODEL`)

## [2026-03-01] T00: Dimension constant changes

- Updated `MotorcycleIndexingService.cs` line 225: `VectorSearchDimensions = 3584` with updated comment to "Qwen3-Embedding-4B native dimensions (no MRL truncation)"
- Updated `AzureOpenAIClientWrapper.cs` line 129: Changed `Enumerable.Range(0, 1536)` to `Enumerable.Range(0, 3584)` (mock embedding generation)
- Updated `AzureOpenAIClientWrapper.cs` line 139: Changed `new float[1536]` to `new float[3584]` (fallback embeddings)
- Build succeeded: Persistence, API, and core projects compiled cleanly
- Pre-existing test project errors (PerformanceTests, EndToEndTests) are unrelated to dimension changes
- No schema migration needed: no real data yet, all mock fixtures
- Change rationale: Full 3584 native dimensions from Qwen3-Embedding-4B model (no MRL truncation penalty)


## [2026-03-01] T01: Add Python dependencies

- Added three dependencies to local-processing-service `pyproject.toml` under `[tool.poetry.dependencies]`
- `azure-search-documents = "^11.4.0"` - Direct Azure AI Search client for document indexing
- `openai = "^1.40.0"` - OpenAI-compatible client for embeddings API (DeepInfra, Azure AI Foundry Local)
- `httpx = "^0.27.0"` - Async HTTP client for embedding API calls
- Used Poetry constraint syntax (^) for compatible version pinning
- File verified as valid TOML with no syntax errors
- No code changes, no installations run (pure dependency declaration)


## 2026-03-01 — T01: OllamaEmbedder 3584-dim migration

### Changes made
- `ollama_embedder.py`: `self._dims = 3584` (was 1536), removed 3-line MRL truncation block (`# MRL truncation – safe for Matryoshka-trained models` / `if len(vector) > self._dims:` / `vector = vector[:self._dims]`), updated class docstring and `generate_embedding` docstring to reflect 3584 native dims, no truncation.
- `tests/test_ollama_embedder.py`: updated `_dims == 3584` assertion, renamed `test_returns_1536_floats` → `test_returns_3584_floats` with 3584-element mock, deleted `test_truncates_larger_vectors_to_1536`, updated `ValueError` match string to `"Expected 3584 dims"`.

### Discovered: missing `pythonpath` in pyproject.toml
- The task spec said `src` is on Python path via conftest or pytest config — but neither existed.
- `pyproject.toml` `[tool.pytest.ini_options]` was missing `pythonpath = ["src"]`.
- All 5 tests failed with `ModuleNotFoundError: No module named 'embeddings'` before fix.
- Fix: added `pythonpath = ["src"]` to `[tool.pytest.ini_options]` in `pyproject.toml`. Tests then passed 5/5.
- **Rule**: Any new pytest-based test module targeting `src/` must have `pythonpath = ["src"]` in pyproject.toml.

### Result
- 5 tests collected, 5 passed in 0.97s.

## [2026-03-01] T03: DeepInfraEmbedder created

- Created `src/embeddings/deepinfra_embedder.py` — mirrors `OllamaEmbedder` structure exactly (same retry loop, ValueError passthrough, `asyncio.gather` batch pattern)
- Uses `openai.AsyncOpenAI(base_url=..., api_key=...)` per the OpenAI-compatible DeepInfra API
- `DEEPINFRA_API_KEY` env var is validated in `__init__`; empty/missing → `ValueError` immediately
- `_dims = 3584`; dimension mismatch raises `ValueError` (not retried)
- 3-attempt retry with `asyncio.sleep(2**attempt)` exponential backoff on transient exceptions
- Created `tests/embeddings/__init__.py` (empty) for test discovery
- Created `tests/embeddings/test_deepinfra_embedder.py` with 6 unit tests; all pass
- Tests use `unittest.mock.AsyncMock` + `monkeypatch.setenv`; `asyncio_mode = "auto"` means no `@pytest.mark.asyncio` decorator needed
- `module reload` pattern needed in tests because `openai.AsyncOpenAI` is instantiated in `__init__` — patch must be applied before class instantiation; `reload(mod)` ensures the patched constructor is picked up
- Test run: `6 passed in 1.83s` on Python 3.13.12 / pytest 9.0.2


## [2026-03-01] T04: AzureFoundryLocalEmbedder created
- Created `src/embeddings/foundry_local_embedder.py` — mirrors `deepinfra_embedder.py` pattern exactly (same retry loop, same `ValueError` re-raise, same batch via `asyncio.gather`)
- Uses `openai.AsyncOpenAI(base_url=endpoint, api_key="local")` — Foundry Local accepts any non-empty key
- `AZURE_FOUNDRY_LOCAL_ENDPOINT` defaults to `http://localhost:5272` (Azure AI Foundry Local default port)
- `AZURE_FOUNDRY_LOCAL_EMBEDDING_MODEL` defaults to `qwen3-embedding`
- `self._dims = 3584` — Qwen3-Embedding native output, matches Azure AI Search index
- `tests/embeddings/__init__.py` already existed from T03 (DeepInfra)
- Test file `tests/embeddings/test_foundry_local_embedder.py` created with 6 tests using `reload()` pattern (same as deepinfra tests to avoid module caching issues with `monkeypatch.setenv`)
- All 6 tests pass: `6 passed in 1.67s`
- Retry test patches `asyncio.sleep` with `AsyncMock` to avoid real waits
## [2026-03-01] T05: embedder_factory.py — singleton factory
- Created src/embeddings/embedder_factory.py with get_embedder() / reset_embedder() singleton pattern.
- EMBEDDING_BACKEND env var selects backend: ollama (default), foundry_local, deepinfra.
- Unknown backend raises ValueError with sorted valid options list.
- autouse=True fixture calls reset_embedder() before AND after each test (belt-and-suspenders).
- Patch targets: embeddings.ollama_embedder.ollama.AsyncClient, embeddings.foundry_local_embedder.openai.AsyncOpenAI, embeddings.deepinfra_embedder.openai.AsyncOpenAI.
- DEEPINFRA_API_KEY must be set via monkeypatch.setenv before calling get_embedder() with deepinfra backend.
- src/embeddings/__init__.py updated: added imports for DeepInfraEmbedder, AzureFoundryLocalEmbedder, get_embedder, reset_embedder; __all__ updated to full list.
- All 6 tests pass: 6 passed in 1.62s.

## [2026-03-01] T05: AzureSearchDirectUploader created

### Files created
- src/search/__init__.py (empty, for package discovery)
- src/search/azure_search_uploader.py
- tests/search/__init__.py (empty)
- tests/search/test_azure_search_uploader.py (7 unit tests)

### Key design points
- upload() is synchronous (azure-search-documents SDK is sync)
- Graceful degradation: no AZURE_SEARCH_ENDPOINT -> _enabled=False, no-op
- Auth: AzureKeyCredential if key set, else DefaultAzureCredential
- Batch size: 100, idempotent merge_or_upload_documents, never raises

### azure-search-documents not installed
- Declared in pyproject.toml but not in venv. Installed via pip install.
- Rule: declaring in pyproject.toml != installed; check with pip show before running tests.

### Test patterns
- Patch at search.azure_search_uploader.SearchClient (module-local)
- SimpleNamespace(succeeded=True) as mock IndexingResult
- No reload() needed (unlike embedder tests); monkeypatch.setenv before construction is enough
- 7 tests, 0.46s, all pass

## [2026-03-01] T07: pdf_processor.py wired

### Changes made
- Replaced `from embeddings.ollama_embedder import OllamaEmbedder` with `from typing import Any`
- Added `from search.azure_search_uploader import AzureSearchDirectUploader` import
- Changed `embedder: OllamaEmbedder` type hint to `embedder: Any` in `__init__`
- Added `AzureSearchDirectUploader()` instantiation + `.upload(records)` call after blob JSONL upload (line ~157), before graph extraction

### Key patterns
- Uploader instantiated fresh inside `_process_pdf` (not in `__init__`) — graceful degradation check at upload time
- `upload()` is synchronous — no `await` needed
- No test file changes required: `AzureSearchDirectUploader.__init__` reads env vars; in test env `AZURE_SEARCH_ENDPOINT` is unset → `_enabled=False` → `upload()` is a no-op automatically
- `OllamaEmbedder` import fully removed (only used for type hint, now replaced with `Any`)

### Test results
- 7 tests collected, 7 passed in 19.76s (no test file modifications)

## [2026-03-01] T08: csv_processor.py wired to AzureSearchDirectUploader

### Change
- Added `from search.azure_search_uploader import AzureSearchDirectUploader` import at line 8
- Added 2 lines in `_process_background()` after blob JSONL upload (lines 182-184), before job status update:
  ```python
  uploader = AzureSearchDirectUploader()
  uploader.upload(chunks)
  ```
- Uploader instantiated fresh inside method (not constructor) — graceful degradation check at upload time
- Synchronous call (no `await`) — `upload()` is sync

### Why tests pass without modification
- `AzureSearchDirectUploader.__init__` reads `AZURE_SEARCH_ENDPOINT` from env
- In test environment, env var is unset → `_enabled=False` → `upload()` is a no-op
- No mock needed in test file; graceful degradation handles it automatically
- All 7 existing tests pass unchanged (1.82s)


## [2026-03-01] T09: Real DeepInfra embeddings in AzureOpenAIClientWrapper

### Changes
- Added `IHttpClientFactory` as 5th constructor parameter (field `_httpClientFactory`)
- `GetEmbeddingsAsync` inner lambda: real HTTP POST to `{DEEPINFRA_BASE_URL}/embeddings`
  - Reads `DEEPINFRA_API_KEY` and `DEEPINFRA_BASE_URL` from env (never hardcoded)
  - Parses `data[].embedding` JSON; validates 3584 dims; throws InvalidOperationException if wrong
  - Uses `System.Text.Json` + built-in `HttpClient` — no new NuGet packages
- Fallback lambda unchanged (already `new float[3584]`)
- AzureOpenAIClientWrapperTests.cs: added Mock<IHttpClientFactory>, updated all constructor call sites
- AzureOpenAIClientWrapperResilienceTests.cs: updated constructor, fixed 1536→3584 at lines 169 and 278
- `dotnet build` passes; `dotnet test MotorcycleRAG.UnitTests` all pass (321 passed, 2 pre-existing failures in unrelated ServiceCollectionExtensionsTests)

## [2026-03-01] T10: Real Azure AI Foundry chat completion in AzureOpenAIClientWrapper

### Changes
- `GetChatCompletionAsync` inner lambda: replaced 3 mock lines with real HTTP POST to `{MCR_API_FOUNDRY_ENDPOINT}/chat/completions?api-version=2024-05-01-preview`
- Auth via `DefaultAzureCredential` → Bearer token scoped to `https://cognitiveservices.azure.com/.default`
- Reads `MCR_API_FOUNDRY_ENDPOINT` (required, throws `InvalidOperationException`) and `MCR_API_FOUNDRY_CHAT_MODEL` (defaults `gpt-4o-mini`) from env
- Parses `choices[0].message.content` from JSON response; throws `InvalidOperationException` if null
- Uses `_httpClientFactory.CreateClient("AzureFoundry")` for named HTTP client
- Log message changed to `"Successfully retrieved chat completion from Azure AI Foundry"`
- Fallback lambda UNCHANGED

### Build issue: Azure.Core namespace collision
- `Azure.Core.TokenRequestContext` failed to resolve because `AzureOpenAIClientWrapper.cs` is in `MotorcycleRAG.Persistence.Azure` namespace
- Compiler resolves `Azure` to `MotorcycleRAG.Persistence.Azure` first, missing `Core` sub-namespace
- **Fix**: use `global::Azure.Core.TokenRequestContext` to force global namespace resolution
- **Rule**: Any code in `MotorcycleRAG.Persistence.Azure` namespace that references `Azure.Core.*` types must use `global::` prefix

### Why tests pass unchanged
- `GetChatCompletionAsync_Success_ReturnsResult`: mocks `ExecuteAsync` → inner lambda never called
- `GetChatCompletionAsync_WithFallback_UsesFallbackOnFailure`: calls fallback, not operation lambda
- `GetChatCompletionAsync_WithCancellation_PassesCancellationToken`: mock throws `OperationCanceledException` → inner lambda never called
- `GetChatCompletionAsync_CreatesLoggingScope`: DOES call inner lambda → `CreateLoggingScope` succeeds → then `DefaultAzureCredential.GetTokenAsync` throws (no real creds in unit test) → catch block handles it → `CreateLoggingScope` verify still passes

### Test results
- 321 passed, 2 failed (pre-existing `AzureAIConfigurationValidatorTests`), 10 skipped
## T11: .env.example File Creation

**Task**: Create `.env.example` for local-processing-service with all hybrid-embedding-pipeline environment variables (T01–T08).

**Completed**: 2026-03-01

**Key Variables Documented**:
- `EMBEDDING_BACKEND`: Selector for embedder backend (ollama | foundry_local | deepinfra)
- `OLLAMA_*`: Pre-existing Ollama config (BASE_URL, MODEL)
- `AZURE_FOUNDRY_LOCAL_*`: New Azure AI Foundry Local endpoints and models
- `DEEPINFRA_*`: New remote DeepInfra API integration variables
- `AZURE_SEARCH_*`: New Azure AI Search configuration (endpoint, index, optional API key)
- `AZURE_STORAGE_*`: Pre-existing Azure Blob Storage for staging (connection string, container)

**Design Decision**: Organized by logical grouping with section headers. Placeholder values only (no real secrets). Clear inline comments explaining each variable's purpose and valid values.

**Learning**: T11 completes the .env infrastructure for the hybrid pipeline. All variables from T01–T08 are now documented in a single reference file suitable for `.env` configuration templates.

## [2026-03-01] T12: environment-variables.md documentation appended

**Task**: Append environment variables section for Python local-processing-service and .NET API DeepInfra/Foundry integration to `6-Docs/environment-variables.md`.

**Completed**: Successfully appended new section at end of file.

**Content Added**:
- **New section**: "## Local Processing Service (Python) — Hybrid Embedding Pipeline"
- **Subsections**:
  - Embedding Backend (backend selector enum)
  - Ollama (Local GPU) — local embeddings
  - Azure AI Foundry Local — local OpenAI-compatible server
  - DeepInfra (Remote Query-Time Embeddings) — remote embeddings with API key
  - Azure AI Search (Direct Upload) — search service config
  - .NET API — Azure AI Foundry Serverless (Chat) — MCR_API_* vars
  - .NET API — DeepInfra Embeddings — shared DeepInfra API key

**Format**: Markdown tables matching existing document style (3-column → 5-column with Required/Type columns). All variables marked as Secret vs. Non-Secret.

**File State**:
- Pre-append line 207: `For deployment procedures and GitHub Actions setup, see \`deployment.md\`.`
- Post-append line 270: Final `DEEPINFRA_BASE_URL` table entry
- Total lines: 207 → 270 (+63 lines)
- No lines edited; entire section appended after `## Cross-Reference` section via horizontal rule (`---`)

**Learning**: Documentation complete for all hybrid-embedding-pipeline environment variables across Python service (T01–T08) and .NET API (T09–T10). All config reference points now consolidated in single environment variables doc.

## [2026-03-01] T13: Integration test � DeepInfraEmbedder ? AzureSearchDirectUploader pipeline

**Task**: Create unit-style integration test verifying the full embed-to-upload pipeline with mocked externals.

**Files created**:
-  (empty, for pytest discovery)
-  (1 test)

**Test**: 
- Monkeypatches all env vars (DEEPINFRA_API_KEY, DEEPINFRA_BASE_URL, AZURE_SEARCH_ENDPOINT, AZURE_SEARCH_INDEX, AZURE_SEARCH_KEY)
- Patches  with mock returning 3584-dim vector
- Patches  with mock capturing upload call
- Uses  pattern for DeepInfraEmbedder (client instantiated in )
- No reload needed for AzureSearchDirectUploader (reads env in  but no module-level client)
- Asserts: vector length == 3584,  called once with correct doc
- Marked 
-  � no  needed (asyncio_mode = auto)

**Key patterns reused**:
-  pattern from 
-  mock from 
-  patch required alongside  patch

**Result**: 1 test collected, 1 passed in 2.16s


## [2026-03-01] T13: Integration test - DeepInfraEmbedder to AzureSearchDirectUploader pipeline

**Task**: Create unit-style integration test verifying the full embed-to-upload pipeline with mocked externals.

**Files created**:
- tests/integration/__init__.py (empty, for pytest discovery)
- tests/integration/test_direct_upload_pipeline.py (1 test)

**Test**: test_deepinfra_embedder_to_uploader_pipeline
- Monkeypatches all env vars (DEEPINFRA_API_KEY, DEEPINFRA_BASE_URL, AZURE_SEARCH_ENDPOINT, AZURE_SEARCH_INDEX, AZURE_SEARCH_KEY)
- Patches embeddings.deepinfra_embedder.openai.AsyncOpenAI with mock returning 3584-dim vector
- Patches search.azure_search_uploader.SearchClient with mock capturing upload call
- Uses importlib.reload() pattern for DeepInfraEmbedder (client instantiated in __init__)
- No reload needed for AzureSearchDirectUploader (reads env in __init__ but no module-level client)
- Asserts: vector length == 3584, merge_or_upload_documents called once with correct doc
- Marked with pytest.mark.slow
- async def with no pytest.mark.asyncio needed (asyncio_mode = auto)

**Key patterns reused**:
- reload() pattern from test_deepinfra_embedder.py
- SimpleNamespace(succeeded=True) mock from test_azure_search_uploader.py
- AzureKeyCredential patch required alongside SearchClient patch

**Result**: 1 test collected, 1 passed in 2.16s

## [2026-03-01] T14: .NET integration test — DeepInfraEmbeddingTests

**Task**: Create `DeepInfraEmbeddingTests.cs` in `MotorcycleRAG.IntegrationTests/Persistence/` to verify the real `AzureOpenAIClientWrapper.GetEmbeddingsAsync` HTTP path to DeepInfra returns valid 3584-dim float arrays.

**File created**: `5-Test/tests/MotorcycleRAG.IntegrationTests/Persistence/DeepInfraEmbeddingTests.cs`

**Key patterns used**:
- `[Fact(Skip = "Integration test - requires real DeepInfra API key and network")]` — always skipped by default
- `[Trait("Category", "Integration")]` — filterable via `--filter "Category=Integration"`
- `IResilienceService` mock: passthrough on `ExecuteAsync<T>(string, Func<Task<T>>, Func<Task<T>>?, string?, CancellationToken)` — 5-param overload
- `ICorrelationService` mock: `GetOrCreateCorrelationId()` returns dummy ID, `CreateLoggingScope(Dictionary)` returns `NoOpDisposable`
- Constructor sig: `AzureOpenAIClientWrapper(IOptions<AzureAIOptions>, ILogger, IResilienceService, ICorrelationService, IHttpClientFactory)` — note `ICorrelationService` was added since T09 task description (was not in original task spec)
- `NullLogger<T>.Instance` from `Microsoft.Extensions.Logging.Abstractions` for logger params
- `AzureAIOptions` requires `OpenAIEndpoint`, `SearchServiceEndpoint`, `DocumentIntelligenceEndpoint` (all `[Required, Url]`)
- Defensive `Assert.Fail()` guard inside test body for case when Skip is removed but env var missing

**Analyzer warnings suppressed**:
- `S1244` — float equality check `f != 0f` intentional for zero-vector detection
- `CA1861` — constant array `new[] { "Honda..." }` acceptable in test method
- `S3881` — simplified IDisposable pattern acceptable in test class

**Build result**: 0 errors, only pre-existing CA2000 warning in AuthorizationTests.cs
**Test result**: 1 test discovered, 1 skipped (as expected)
