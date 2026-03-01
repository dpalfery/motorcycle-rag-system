# Hybrid Embedding Pipeline

## TL;DR

> **Quick Summary**: Replace the mock `AzureOpenAIClientWrapper` and expensive Azure embedding model dependency with a cost-effective hybrid approach. Ingestion uses the local RTX 5090 (Qwen3-Embedding-4B via Ollama or Azure AI Foundry Local) to generate full **3584-dim** vectors pushed directly to Azure AI Search via the Python SDK. Query-time embedding uses DeepInfra's hosted Qwen3-Embedding-4B API ($0.020/1M tokens). Azure AI Foundry serverless (GPT-4o-mini or Phi-4) replaces the mock chat completion. **No MRL truncation** — full native 3584 dims everywhere.
>
> **Deliverables**:
> - .NET: Update `MotorcycleIndexingService.cs` index schema from 1536 → 3584 dims
> - .NET: Update fallback vectors in `AzureOpenAIClientWrapper.cs` to 3584 dims
> - Python: Update `OllamaEmbedder` to remove MRL truncation (emit full 3584 dims)
> - Python: `DeepInfraEmbedder`, `AzureFoundryLocalEmbedder`, `EmbedderFactory`, `AzureSearchDirectUploader`
> - Python: Wire direct-push uploader into `pdf_processor.py` and `csv_processor.py`
> - .NET: Real DeepInfra HTTP embeddings replacing mock in `AzureOpenAIClientWrapper`
> - .NET: Real Azure AI Foundry serverless chat completion in `AzureOpenAIClientWrapper`
> - Config: `.env.example` updated with all new variables
> - Tests: Unit tests for all new/updated components (Python + .NET)
>
> **Estimated Effort**: Medium-Large
> **Parallel Execution**: YES - 8 waves
> **Critical Path**: T00 (schema) → T01 (deps) → T02+T03+T04 (embedders) → T05+T06 (factory+uploader) → T07+T08 (processors) → T09→T10 (.NET impl, sequential) → T11+T12 (config) → T13+T14 (tests)

---

## Context

### Original Request
Replace the $300/month Azure embedding model approach. Use local RTX 5090 (Azure AI Foundry Local or Ollama) for ingestion-time embeddings, DeepInfra for query-time embeddings, and Azure AI Foundry serverless for chat/reasoning. User elected to use the **full 3584 native dims** from Qwen3-Embedding-4B — no MRL truncation — since no real data has been indexed yet (mock embeddings only), so migration cost is zero.

### Architecture Decision
- **No MRL truncation**: Qwen3-Embedding-4B natively outputs 3584 dims; we use all of them. The index schema must change from 1536 → 3584. This is a breaking schema change but cost-free because no real documents are indexed yet.
- **Ingestion**: Python local service generates vectors → pushes directly to Azure AI Search (new push model alongside existing blob→indexer pull model)
- **Query time**: .NET API calls DeepInfra HTTP API for single-query embedding → passes vector to `AzureSearchQueryService`
- **Chat/Reasoning**: Azure AI Foundry serverless endpoint replaces mock `GetChatCompletionAsync`

### Key Technical Facts
- `AzureOpenAIClientWrapper.cs` is currently 100% mock — returns random 1536-dim floats and fake chat completions
- `MotorcycleIndexingService.cs` line 225: `VectorSearchDimensions = 1536` → **must change to 3584**
- `OllamaEmbedder` currently truncates 3584→1536 via MRL → **remove truncation, emit full 3584 dims**
- `azure-search-documents` Python SDK NOT currently in `pyproject.toml` — must be added
- `openai` Python package NOT in `pyproject.toml` — needed for DeepInfra (OpenAI-compatible API)
- DeepInfra endpoint: `https://api.deepinfra.com/v1/openai/embeddings`, model: `Qwen/Qwen3-Embedding-4B`
- DeepInfra outputs 3584 dims natively for Qwen3-Embedding-4B — no truncation needed
- Azure AI Foundry Local: local GPU inference runtime from Microsoft, OpenAI-compatible API on configurable local port (default 5272)
- Index name: `motorcycle-index` (must NOT change)
- Vector dims: **3584** (native Qwen3-Embedding-4B output — no truncation anywhere)
- No real indexed data exists yet — index rebuild is safe and has zero migration cost

### Embedding Source Strategy
| Scenario | Embedder | Dims |
|---|---|---|
| Ingestion (bulk, local) | `OllamaEmbedder` (updated) OR `AzureFoundryLocalEmbedder` (new) | 3584 |
| Query time (.NET) | DeepInfra HTTP call in `AzureOpenAIClientWrapper` | 3584 |
| Query time (Python) | `DeepInfraEmbedder` (new) | 3584 |
| Factory selection | `EMBEDDING_BACKEND` env var: `ollama` / `foundry_local` / `deepinfra` | — |

### Environment Variables (New)
```
# DeepInfra
DEEPINFRA_API_KEY=<your-key-here>
DEEPINFRA_EMBEDDING_MODEL=Qwen/Qwen3-Embedding-4B
DEEPINFRA_BASE_URL=https://api.deepinfra.com/v1/openai

# Azure AI Foundry Local (local GPU inference)
AZURE_FOUNDRY_LOCAL_ENDPOINT=http://localhost:5272
AZURE_FOUNDRY_LOCAL_EMBEDDING_MODEL=qwen3-embedding

# Azure AI Search (for direct push from Python)
AZURE_SEARCH_ENDPOINT=https://<service>.search.windows.net
AZURE_SEARCH_INDEX=motorcycle-index

# Embedding backend selector
EMBEDDING_BACKEND=ollama  # ollama | foundry_local | deepinfra

# Azure AI Foundry Serverless (for .NET chat)
MCR_API_FOUNDRY_ENDPOINT=https://<project>.services.ai.azure.com/models
MCR_API_FOUNDRY_CHAT_MODEL=gpt-4o-mini
```

---

## Tasks

### Wave 1 — Schema Update (.NET, sequential prerequisite for all vector work)

- [ ] **T00** — Update Azure AI Search index schema from 1536 → 3584 dims
  File: `4-Persistence/MotorcycleRAG.Persistence/Search/MotorcycleIndexingService.cs`
  - Change `VectorSearchDimensions = 1536` (line 225) to `VectorSearchDimensions = 3584`
  - Update the comment on that line from `// OpenAI text-embedding-3-large dimensions` to `// Qwen3-Embedding-4B native dimensions (no MRL truncation)`
  - Search entire codebase for any other hardcoded `1536` dimension references related to vectors and update them
  - Also update fallback in `AzureOpenAIClientWrapper.cs`: the fallback lambda in `GetEmbeddingsAsync` returns `new float[1536]` → change to `new float[3584]`
  - Also update the mock inner lambda: `Enumerable.Range(0, 1536)` → `Enumerable.Range(0, 3584)`
  - Verify: `dotnet build` passes after this change

### Wave 2 — Python Dependencies (sequential prerequisite for all Python work)

- [ ] **T01** — Add Python dependencies to `pyproject.toml`
  File: `2-Application/local-processing-service/pyproject.toml`
  - Add `azure-search-documents = "^11.4.0"`
  - Add `openai = "^1.0.0"`
  - Add `httpx = "^0.27.0"` (used by openai async client internally)
  - Verify: file is syntactically valid TOML

### Wave 3 — Update OllamaEmbedder + New Python Embedders (Parallel)

- [ ] **T02** — Update `OllamaEmbedder` to remove MRL truncation and emit full 3584 dims
  File: `2-Application/local-processing-service/src/embeddings/ollama_embedder.py`
  - Change `self._dims: int = 1536` to `self._dims: int = 3584`
  - Remove the MRL truncation block (`if len(vector) > self._dims: vector = vector[:self._dims]`)
  - Keep the dimension validation: `if len(vector) != self._dims: raise ValueError(...)`
  - Update docstring: remove reference to MRL truncation; state native 3584 dims
  - Update any unit test fixtures that assert 1536-length vectors → assert 3584 instead

- [ ] **T03** — Create `DeepInfraEmbedder`
  New file: `2-Application/local-processing-service/src/embeddings/deepinfra_embedder.py`
  - Reads `DEEPINFRA_API_KEY`, `DEEPINFRA_BASE_URL` (default: `https://api.deepinfra.com/v1/openai`), `DEEPINFRA_EMBEDDING_MODEL` (default: `Qwen/Qwen3-Embedding-4B`) from env
  - Uses `openai.AsyncOpenAI(base_url=..., api_key=...)` to call DeepInfra embeddings endpoint
  - Returns **3584-dim** vectors — DeepInfra/Qwen3-Embedding-4B native output, no truncation
  - Implements same public interface as `OllamaEmbedder`: `async generate_embedding(text: str) -> list[float]`, `async generate_embeddings_batch(texts: list[str]) -> list[list[float]]`, `async check_status() -> str`
  - Retry logic: 3 attempts, exponential backoff — follow `OllamaEmbedder` pattern exactly
  - Raises `ValueError` if returned vector length != 3584
  - Unit test: `tests/embeddings/test_deepinfra_embedder.py` (mock `openai.AsyncOpenAI`)

- [ ] **T04** — Create `AzureFoundryLocalEmbedder`
  New file: `2-Application/local-processing-service/src/embeddings/foundry_local_embedder.py`
  - Azure AI Foundry Local runs a local OpenAI-compatible inference server (default port 5272)
  - Reads `AZURE_FOUNDRY_LOCAL_ENDPOINT` (default: `http://localhost:5272`) and `AZURE_FOUNDRY_LOCAL_EMBEDDING_MODEL` (default: `qwen3-embedding`) from env
  - Uses `openai.AsyncOpenAI(base_url=..., api_key="local")` — Foundry Local requires any non-empty API key string
  - Returns **3584-dim** vectors — no truncation
  - Implements same public interface as `OllamaEmbedder`
  - Retry logic: 3 attempts, exponential backoff
  - Raises `ValueError` if returned vector length != 3584
  - Unit test: `tests/embeddings/test_foundry_local_embedder.py` (mock `openai.AsyncOpenAI`)

### Wave 4 — EmbedderFactory + AzureSearchDirectUploader (Parallel, depends on Wave 3)

- [ ] **T05** — Create `EmbedderFactory`
  New file: `2-Application/local-processing-service/src/embeddings/embedder_factory.py`
  - Reads `EMBEDDING_BACKEND` env var (default: `ollama`)
  - Returns appropriate embedder instance: `ollama` → `OllamaEmbedder`, `foundry_local` → `AzureFoundryLocalEmbedder`, `deepinfra` → `DeepInfraEmbedder`
  - Raises `ValueError` with clear message for unknown backend values
  - Module-level singleton pattern: instantiate once on first call, return same instance on subsequent calls
  - Unit test: `tests/embeddings/test_embedder_factory.py` — test all three backends and unknown backend error

- [ ] **T06** — Create `AzureSearchDirectUploader`
  New file: `2-Application/local-processing-service/src/search/azure_search_uploader.py`
  - Reads `AZURE_SEARCH_ENDPOINT` and `AZURE_SEARCH_INDEX` (default: `motorcycle-index`) from env
  - Uses `DefaultAzureCredential` for auth — follow exact pattern from `blob_writer.py`
  - Uses `azure.search.documents.aio.SearchClient` (async client)
  - Constructor: `__init__(self) -> None` — lazy-initialize client on first use
  - Method: `async def upload_documents(self, documents: list[dict]) -> None`
    - Each document dict must include: `id` (str), `contentVector` (list[float], len=3584), plus any metadata fields
    - Uses `merge_or_upload_documents` for idempotent upserts
    - Batches in groups of 100 documents per SDK call
    - Logs upload count and any per-document errors
  - Method: `async def check_status(self) -> str` — attempts a zero-result search to verify connectivity; returns `'connected'` or `'disconnected'`; never raises
  - Unit test: `tests/search/test_azure_search_uploader.py` (mock `SearchClient`)

### Wave 5 — Wire Processors (Parallel, depends on Wave 4)

- [ ] **T07** — Update `pdf_processor.py` to use `EmbedderFactory` + `AzureSearchDirectUploader`
  File: `2-Application/local-processing-service/src/processors/pdf_processor.py`
  - Replace hardcoded `OllamaEmbedder()` instantiation with `EmbedderFactory.get_embedder()`
  - After generating embeddings for each batch, call `AzureSearchDirectUploader().upload_documents(batch_docs)` in parallel with the existing blob write using `asyncio.gather`
  - If `AZURE_SEARCH_ENDPOINT` env var is not set → skip direct upload, log a `WARNING`, continue with blob write only (graceful degradation)
  - All existing behaviour is preserved; this is purely additive
  - Verify: existing unit tests for `pdf_processor.py` still pass

- [ ] **T08** — Update `csv_processor.py` to use `EmbedderFactory` + `AzureSearchDirectUploader`
  File: `2-Application/local-processing-service/src/processors/csv_processor.py`
  - Same changes as T07 but for the CSV processor
  - Replace hardcoded `OllamaEmbedder()` with `EmbedderFactory.get_embedder()`
  - Call `AzureSearchDirectUploader().upload_documents(batch_docs)` in parallel with blob write via `asyncio.gather`
  - Graceful degradation if `AZURE_SEARCH_ENDPOINT` not set
  - Verify: existing unit tests for `csv_processor.py` still pass

### Wave 6 — .NET Real Implementations (Sequential — same file)

> **Note**: T09 and T10 both modify `AzureOpenAIClientWrapper.cs`. They MUST be delegated **sequentially** — T09 first, then T10 as a follow-up in the same session or a fresh delegation. Do NOT parallelize.

- [ ] **T09** — Replace mock embeddings in `AzureOpenAIClientWrapper` with real DeepInfra HTTP call
  File: `4-Persistence/MotorcycleRAG.Persistence/Azure/AzureOpenAIClientWrapper.cs`
  - Inject `IHttpClientFactory` via constructor (add parameter; check if DI registration needs updating in startup)
  - In `GetEmbeddingsAsync(string model, string[] texts, CancellationToken)`:
    - Replace the random-float mock inner lambda with a real HTTP POST to DeepInfra
    - Read `DEEPINFRA_API_KEY` and `DEEPINFRA_BASE_URL` via `Environment.GetEnvironmentVariable()`
    - Request: `POST {DEEPINFRA_BASE_URL}/embeddings` with `Authorization: Bearer {DEEPINFRA_API_KEY}` header
    - Body (JSON): `{"model": "Qwen/Qwen3-Embedding-4B", "input": [...texts], "encoding_format": "float"}`
    - Parse response: `data[].embedding` → `float[][]`
    - Verify output: each embedding must be exactly 3584 dims; throw `InvalidOperationException` if not
    - Keep the `_resilienceService.ExecuteAsync` wrapper — only replace the inner lambda body
    - Fallback lambda: return `texts.Select(_ => new float[3584]).ToArray()`
    - MUST NOT add any NuGet packages not already referenced in the project
  - Unit test: update `5-Test/tests/MotorcycleRAG.UnitTests/Persistence/AzureOpenAIClientWrapperTests.cs`
    - Mock `IHttpClientFactory` / `HttpMessageHandler`
    - Verify correct request JSON is sent
    - Verify 3584-dim float arrays are returned correctly
    - Verify fallback returns 3584-dim zero vectors

- [ ] **T10** — Replace mock chat completion in `AzureOpenAIClientWrapper` with Azure AI Foundry serverless
  File: `4-Persistence/MotorcycleRAG.Persistence/Azure/AzureOpenAIClientWrapper.cs`
  - In `GetChatCompletionAsync(string deploymentName, string prompt, CancellationToken)`:
    - Replace the fake-string mock inner lambda with a real HTTP POST to Azure AI Foundry serverless
    - Read `MCR_API_FOUNDRY_ENDPOINT` and `MCR_API_FOUNDRY_CHAT_MODEL` via `Environment.GetEnvironmentVariable()`
    - Authenticate using `DefaultAzureCredential` to obtain a bearer token for scope `https://cognitiveservices.azure.com/.default`
    - Request: `POST {MCR_API_FOUNDRY_ENDPOINT}/chat/completions?api-version=2024-05-01-preview`
    - Headers: `Authorization: Bearer {token}`, `Content-Type: application/json`
    - Body (JSON): `{"model": "{MCR_API_FOUNDRY_CHAT_MODEL}", "messages": [{"role": "user", "content": "{prompt}"}]}`
    - Parse response: `choices[0].message.content` → return as string
    - Keep `_resilienceService.ExecuteAsync` wrapper and existing fallback lambda
  - Unit test: update `AzureOpenAIClientWrapperTests.cs`
    - Mock `IHttpClientFactory` and verify correct Foundry request JSON
    - Verify response content is correctly extracted

### Wave 7 — Config + Docs (Parallel, can run alongside Wave 6)

- [ ] **T11** — Update `.env.example` with all new environment variables
  File: `2-Application/local-processing-service/.env.example`
  - Add all new variables with `<placeholder>` values and inline comments explaining each
  - Group by section with headers: `# DeepInfra Embeddings`, `# Azure AI Foundry Local`, `# Azure AI Search (Direct Push)`, `# Embedding Backend`, `# Azure AI Foundry Serverless (Chat)`
  - Do NOT add real secrets — only `<your-key-here>` placeholders
  - All existing variables must remain unchanged

- [ ] **T12** — Update `6-Docs/environment-variables.md` with new variables
  File: `6-Docs/environment-variables.md`
  - Add entries for: `MCR_API_FOUNDRY_ENDPOINT`, `MCR_API_FOUNDRY_CHAT_MODEL`
  - Document `DEEPINFRA_API_KEY` as Python-service-only (not needed by .NET API)
  - If the file does not exist, skip this task and note it in the implementation notepad

### Wave 8 — Integration Tests

- [ ] **T13** — Python integration test: embedder → uploader round-trip
  New file: `2-Application/local-processing-service/tests/integration/test_direct_upload_pipeline.py`
  - Mark with `@pytest.mark.slow`
  - Skip automatically if `AZURE_SEARCH_ENDPOINT` or `DEEPINFRA_API_KEY` not set
  - Test: generate one embedding via `DeepInfraEmbedder`, verify it is 3584 dims, upload to Azure AI Search, verify document is retrievable via a search query, clean up (delete) test document after assertion
  - Uses a test document ID prefixed with `test-` to identify and safely delete

- [ ] **T14** — .NET integration test: DeepInfra embedding end-to-end
  New file: `5-Test/tests/MotorcycleRAG.IntegrationTests/Persistence/DeepInfraEmbeddingTests.cs`
  - Mark with `[Trait("Category", "Integration")]`
  - Skip if `DEEPINFRA_API_KEY` env var is not set
  - Test: call `GetEmbeddingsAsync` with a real HTTP call against DeepInfra, verify output is a 3584-dim float array with no all-zero values

---

## Parallelization Map

```
Wave 1: T00 (.NET schema — sequential, fixes the 1536 constant everywhere)
         ↓
Wave 2: T01 (Python deps — sequential, Poetry lock needed first)
         ↓
Wave 3: T02, T03, T04 (parallel — independent new/updated files)
         ↓
Wave 4: T05, T06 (parallel — independent new files)
         ↓
Wave 5: T07, T08 (parallel — different processor files)

Wave 6: T09 → T10 (sequential — same .NET file; T09 first, T10 resumes session)

Wave 7: T11, T12 (parallel — different config files, independent of Waves 3-5)

Wave 8: T13, T14 (parallel — different test files)
```

---

## Definition of Done

- [ ] `VectorSearchDimensions` is **3584** in `MotorcycleIndexingService.cs`
- [ ] No `1536` dimension references remain anywhere in the codebase for vectors
- [ ] `OllamaEmbedder` emits **3584** dims (MRL truncation removed)
- [ ] `DeepInfraEmbedder` and `AzureFoundryLocalEmbedder` exist with 3584-dim output and unit tests
- [ ] `EmbedderFactory` selects embedder from `EMBEDDING_BACKEND` env var with unit tests
- [ ] `AzureSearchDirectUploader` exists with unit tests
- [ ] `pdf_processor.py` and `csv_processor.py` use `EmbedderFactory` and call `AzureSearchDirectUploader`
- [ ] `AzureOpenAIClientWrapper.GetEmbeddingsAsync` makes real DeepInfra HTTP call returning 3584-dim vectors
- [ ] `AzureOpenAIClientWrapper.GetChatCompletionAsync` makes real Foundry serverless call
- [ ] `.env.example` updated with all new variables
- [ ] `dotnet build` passes with zero errors
- [ ] `dotnet test` passes (all unit tests)
- [ ] `pytest` passes (all Python unit tests)
- [ ] No secrets hardcoded anywhere
- [ ] No MRL truncation anywhere in the codebase
