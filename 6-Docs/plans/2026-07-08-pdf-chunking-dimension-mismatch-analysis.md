# PDF Manual Chunking Job — Dimension Mismatch Fix + TruncatingEmbedder Centralization

**Status:** Draft  
**Date:** 2026-07-08  
**Goal:** Resolve "Expected 1536 dims, got 2560" error by centralizing client-side MRL truncation via a decorator, correcting provider documentation, and aligning all dimension hard-codes.

---

## 1. Problem / Motivation

### Symptom
The PDF manual chunking job fails: `"PDF processing failed: Expected 1536 dims, got 2560"`.

### Root-Cause Chain (Verified)

**Provider**: LM Studio (NOT Ollama)

1. `.env:14` sets `EMBEDDING_PROVIDER_ENDPOINT=http://localhost:1234` (LM Studio's default port).
2. `embedder_factory.py:48-67` — when `EMBEDDING_PROVIDER_ENDPOINT` is set, it takes precedence over `EMBEDDING_BACKEND`. Model discovery detects LM Studio as `"openai-compatible"` via `/v1/models`.
3. `OpenAIEmbedder` is instantiated (not `OllamaEmbedder`).
4. `OpenAIEmbedder:131` passes `dimensions=1536` to the LM Studio API.
5. **LM Studio does NOT support the `dimensions` parameter** — it ignores it and returns the model's native vectors (Qwen3-Embedding-4B, 2560 dims).
6. `OpenAIEmbedder:138-139` dimension check fails: `Expected 1536 dims, got 2560`.

### Why Client-Side Truncation Is Correct

User confirmed (with documentation):
- LM Studio does not support the `dimensions` parameter for Matryoshka truncation.
- Qwen3-Embedding-4B is trained with Matryoshka Representation Learning (MRL), meaning vectors are **designed to be truncated** to fewer dimensions.
- Client-side slicing (keeping the first N dimensions) is the standard approach when the server ignores `dimensions`.
- Retrieval quality drop is **< 1–3%** — acceptable for this use case.

### Code Smell: DRY Violation in Embedder Architecture

Both `OpenAIEmbedder` and `OllamaEmbedder` independently implement:
- Retry logic (3 retries, exponential backoff)
- Dimension validation
- Timeout handling
- Health check caching

If truncation logic is added to each embedder individually, the DRY violation worsens. The user recalls the architecture was supposed to have **centralized embedder logic with IoC for the inference interface client**. The TruncatingEmbedder decorator pattern resolves this cleanly.

---

## 2. Approved Decisions

- **D1 — LM Studio is the provider**: Document that `EMBEDDING_PROVIDER_ENDPOINT=http://localhost:1234` means LM Studio, not Ollama. `EMBEDDING_BACKEND=ollama` is overridden by the endpoint.
- **D2 — Client-side MRL truncation**: Use first-N-dimensions slicing as the truncation strategy. This is the standard approach for MRL-trained models when the server ignores `dimensions`.
- **D3 — TruncatingEmbedder decorator pattern**: Create a new `TruncatingEmbedder` class that wraps any `Embedder` implementation. This centralizes truncation logic, eliminating the need to add it to each concrete embedder.
- **D4 — Factory wraps with TruncatingEmbedder**: `embedder_factory.py` wraps the selected embedder instance with `TruncatingEmbedder` before returning it. All callers get truncation transparently.
- **D5 — Dimension = 1536, hard-coded everywhere**: All embedding paths produce 1536-dim vectors. The 3584 hard-code in `MotorcycleIndexingService.cs:225` must change to 1536.
- **D6 — agents.md update**: Add a section to the local-processing-service documentation clarifying that LM Studio is the inference provider (not Ollama), and that client-side truncation is applied.

---

## 3. Investigation Findings

### 3.1 Provider Identification

**Actual Provider**: LM Studio (OpenAI-compatible server on port 1234)

| Source | Evidence |
|--------|----------|
| `.env:14` | `EMBEDDING_PROVIDER_ENDPOINT=http://localhost:1234` |
| `embedder_factory.py:48-67` | Endpoint takes precedence over `EMBEDDING_BACKEND` |
| `model_discovery.py:111-117` | Detects `"openai-compatible"` via `/v1/models` |
| `openai_embedder.py:82` | Default endpoint `http://localhost:1234/v1` |

**Why Ollama is NOT the provider**:
- `.env:10` has `EMBEDDING_BACKEND=ollama` but `EMBEDDING_PROVIDER_ENDPOINT` takes precedence in `embedder_factory.py:45-67`.
- Model discovery finds LM Studio first → `OpenAIEmbedder` is used.

### 3.2 Dimension Sources

| Source | Value | Context |
|--------|-------|---------|
| `openai_embedder.py:94` | `EMBEDDING_DIMS` default `1536` | Python embedder |
| `ollama_embedder.py:62` | `OLLAMA_EMBEDDING_DIMS` default `1536` | Python embedder |
| `MotorcycleIndexingService.cs:225` | `3584` (WRONG) | C# index service |
| `AzureFoundryClientWrapper.cs:131,154` | `1536` | C# query-time embedder |
| `IndexSchemaConfiguration.cs:17` | `1536` | C# config |

### 3.3 Existing Embedder Architecture (Python)

```
embedder.py (ABC)
├── generate_embedding(text) → list[float]
├── generate_embeddings_batch(texts) → list[list[float]]
└── check_status() → str

OpenAIEmbedder(Embedder)          OllamaEmbedder(Embedder)
- For LM Studio, DeepInfra,       - For Ollama servers
  Foundry Local, etc.             - Uses ollama SDK
- Uses openai SDK
- dims from EMBEDDING_DIMS        - dims from OLLAMA_EMBEDDING_DIMS
```

**Key finding**: Only two concrete embedders exist (`OpenAIEmbedder`, `OllamaEmbedder`). The plan previously referenced `DeepInfraEmbedder` and `FoundryLocalEmbedder` — these do NOT exist. DeepInfra and Foundry Local are both served through `OpenAIEmbedder` (OpenAI-compatible).

### 3.4 C# Dimension Inconsistency

`MotorcycleIndexingService.cs:225` has `VectorSearchDimensions = 3584` while everything else uses 1536. This is a latent bug — the index would be created with the wrong dimension count.

---

## 4. Task List

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| 1 | Architecture | `truncating_embedder.py` (NEW) | Create `TruncatingEmbedder` decorator class implementing `Embedder` ABC. Wraps any inner embedder, applies first-N-dimensions slicing on `generate_embedding` and `generate_embeddings_batch`. Passes through `check_status`. | python-dev |
| 2 | Architecture | `embedder_factory.py` | Wrap the selected embedder instance with `TruncatingEmbedder` before returning from `get_embedder()`. Read target dims from `EMBEDDING_DIMS` env var (default 1536). | python-dev |
| 3 | Architecture | `__init__.py` | Export `TruncatingEmbedder` in the embeddings package. | python-dev |
| 4 | Fix | `openai_embedder.py` | Remove the `dimensions=self._dims` parameter from the API call (line 131). LM Studio ignores it, and truncation is now handled centrally by `TruncatingEmbedder`. Keep the dimension validation as a safety net. | python-dev |
| 5 | Fix | `MotorcycleIndexingService.cs:225` | Change `VectorSearchDimensions = 3584` to `VectorSearchDimensions = 1536`. Update comment to reference Matryoshka truncation. | dotnet-dev |
| 6 | Docs | `AGENTS.md` (root) or `6-Docs/` | Add/update documentation clarifying that the local processor uses LM Studio (not Ollama) as the inference provider. Document that client-side MRL truncation is applied. | python-dev |
| 7 | Docs | `.env` (line 40) | Update comment from "3584-dim" to "1536-dim" and note that LM Studio does not support server-side truncation. | python-dev |
| 8 | Tests | Python tests | Add unit tests for `TruncatingEmbedder`: truncation applied when vector > target, no truncation when vector == target, error when vector < target. Verify `generate_embeddings_batch` and `check_status` pass-through. | test-dev |
| 9 | Tests | C# tests | Verify `MotorcycleIndexingService` index definition uses 1536 dimensions. | test-dev |

---

## 5. Sequencing / Dependency Graph

```
Task 1 (Create TruncatingEmbedder)
    ↓
Task 2 (Update factory to wrap) ← depends on Task 1
    ↓
Task 3 (Update __init__.py) ← depends on Task 1
    ↓
Task 4 (Clean up OpenAIEmbedder) ← depends on Task 2 (truncation now centralized)
    ↓
Task 5 (Fix C# dimension) ← independent, can run in parallel with Tasks 1-4
    ↓
Task 6 (Documentation update) ← depends on Task 1 (need to know the final architecture)
    ↓
Task 7 (.env comment fix) ← independent
    ↓
Task 8 (Python tests) ← depends on Tasks 1-4
    ↓
Task 9 (C# tests) ← depends on Task 5
```

**Parallel-safe**: Tasks 1-4 (Python architecture) and Task 5 (C# fix) can run concurrently. Tasks 6-7 (docs) are lightweight and can happen anytime after Task 1.

---

## 6. Residual Decisions / Risks

### Open Questions

1. **Should `OpenAIEmbedder` still pass `dimensions=1536` to the API as a "best effort"?**
   - Pro: If a future server supports MRL truncation, it would use it.
   - Con: Adds confusion when debugging (server ignores it).
   - **Recommendation**: Keep it. It's harmless if ignored, and beneficial if supported. The `TruncatingEmbedder` provides the safety net regardless.

2. **Should `OllamaEmbedder` also be wrapped by `TruncatingEmbedder`?**
   - If Ollama supports `dimensions` natively, truncation may not be needed.
   - **Recommendation**: Yes, wrap it. Defensive — if Ollama's MRL support changes, truncation still works. Zero cost when vectors are already the right size.

3. **Should we add a `TruncatingEmbedder` test that mocks an embedder returning oversized vectors?**
   - **Recommendation**: Yes. This is the core contract — verify the decorator works independently of any real provider.

### Known Risks

1. **MRL truncation quality**: < 1-3% retrieval quality drop is documented and acceptable.
2. **Index rebuild**: If any existing data was indexed with 2560-dim vectors, it would need re-indexing. Current state: `DocumentsProcessedCount=0` — no data to rebuild.
3. **Ollama dead code**: If LM Studio is the only provider, `OllamaEmbedder` may be dead code. Keep it for future flexibility.

---

## 7. Out of Scope

1. **Changing embedding model**: Using a model that natively produces 1536 dimensions.
2. **Changing Azure AI Search dimensions**: Keeping 3584 and updating all components.
3. **Refactoring retry/timeout logic into a base class**: The decorator pattern addresses the immediate truncation concern. Retry/timeout DRY improvements are a separate concern.
4. **Removing `OllamaEmbedder`**: May be needed for future Ollama support.
5. **Performance optimization**: Embedding generation speed optimization.

---

## 8. Skill → Agent Mapping Table

| Skill | Agent | Responsibility |
|-------|-------|----------------|
| python-dev | Python Developer | Create `TruncatingEmbedder`, update factory, update `__init__.py`, clean up `OpenAIEmbedder`, update `.env` comments |
| dotnet-dev | .NET Developer | Fix `MotorcycleIndexingService.cs` dimension, update agents.md if in .NET area |
| test-dev | Test Developer | Write `TruncatingEmbedder` unit tests, verify C# dimension in tests |
| code-review | Code Reviewer | Review all changes for correctness and architecture compliance |

---

## 9. Verification Harness

### Unit Tests (Python)
- `test_truncating_embedder.py`:
  - Vector > target → truncated to target dims, warning logged
  - Vector == target → returned unchanged
  - Vector < target → `ValueError` raised
  - `generate_embeddings_batch` applies truncation to each vector
  - `check_status` delegates to inner embedder
- `test_embedder_factory.py`:
  - Factory returns `TruncatingEmbedder` wrapping the selected backend
  - `EMBEDDING_DIMS` env var controls target dims

### Unit Tests (C#)
- Verify `MotorcycleIndexingService` index definition has `VectorSearchDimensions = 1536`
- Verify no `3584` hard-code remains in C# codebase

### Integration Tests
- End-to-end PDF processing with LM Studio produces 1536-dim vectors
- No dimension mismatch errors

### Manual Verification
1. Run PDF upload job → no dimension errors
2. Check Azure AI Search index has 1536-dim vector field
3. Verify LM Studio is the actual provider (logs show "openai-compatible" not "ollama")
4. Verify `rg "3584"` returns zero matches (excluding docs/plans)

---

## 10. Summary

### Root Cause
LM Studio (the actual provider) does not support the `dimensions` parameter. It returns native 2560-dim vectors from Qwen3-Embedding-4B, which fail the 1536-dim validation.

### Primary Fix
Create a `TruncatingEmbedder` decorator that wraps any `Embedder` and applies client-side Matryoshka truncation (first-N-dimensions slicing). This centralizes the logic, eliminating the code smell of adding truncation to each concrete embedder.

### Secondary Fix
Align `MotorcycleIndexingService.cs:225` from `3584` to `1536` dimensions.

### Documentation
Update agents.md and `.env` comments to clarify that LM Studio is the provider and client-side truncation is applied.

### Architecture Improvement
The `TruncatingEmbedder` decorator pattern:
- Follows the Open/Closed Principle (open for extension, closed for modification)
- Centralizes truncation in one place (no DRY violation)
- Works with any `Embedder` implementation (IoC-friendly)
- Is applied transparently by the factory (callers don't need to change)
