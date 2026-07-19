# Local processor start failure

**Status:** Archived  
**Date:** 2026-07-19  
**Archived:** 2026-07-19  
**Goal:** Stop Admin Desktop processor starts from dying with exit status 1 during import-time embedding discovery, and surface the real Python error when start still fails.

---

## 1. Problem / Motivation

**Symptom (confirmed):** Admin Desktop Start fails with:

> Could not start the processor: Starting the local processor failed in the desktop app's local processor integration. processor process exited early with exit status: 1

That string is `formatAdminError(..., kind: "local-processor", location: "integration")` wrapping Rust `child_exit_description` → `processor process exited early with {status}`.

**Verified root-cause chain:**

1. `ProcessorScreen` Start → `processor.start` → Rust `processor_start` spawns venv uvicorn with TLS + control token; stdout/stderr discarded (`Stdio::null()`).
2. Loading `main:app` runs module-level init: `BlobWriter()` → `ApiClient()` → **`get_embedder()`** → `GraphExtractor()` → `MetadataExtractor()` → processors.
3. When Admin Desktop sets `EMBEDDING_PROVIDER_ENDPOINT`, `get_embedder()` always calls `discover_embedding_models_sync()` before constructing the embedder — even when `EMBEDDING_MODEL` is set.
4. Discovery failure escapes module import; uvicorn never binds; process exits with status 1. No log after `ApiClient` because `get_embedder` only logs after discovery succeeds.
5. Live evidence (`src/logs/local-processor.log`, 2026-07-19 02:36): starts log BlobWriter + ApiClient, then silence.
6. Operators only see exit status 1 because child stderr is discarded.

---

## 2. Approved decisions

| ID | Decision |
| --- | --- |
| D1 | Failure class is **early process exit (status 1)** during Admin Desktop `processor_start`, not the TypeScript “did not become ready” preflight path. |
| D2 | **Lazy-init embedder:** no network I/O during `main` module import / `get_embedder()` construction. Discovery and concrete embedder construction run on first use (`check_status` / embed). Init/discovery failure must not kill the process; `/health` reports embedding provider disconnected/unhealthy. |
| D3 | **Capture child stderr** (bounded, redacted) on `processor_start` failure and include it in the error returned to the UI. |
| D4 | TypeScript `processorIsReady` / `ensureProcessorReady` healthy-vs-degraded alignment is **out of scope** for this plan. |

---

## 3. Investigation findings

| Fact | Source |
| --- | --- |
| UI wraps Rust error via `formatLocalProcessorError` (integration) | `adminError.ts`, `ProcessorScreen` start mutation |
| Rust maps early child death to `processor process exited early with {status}` | `lib.rs` `child_exit_description` / `wait_for_processor_listening` |
| Module init order places network discovery before listen | `main.py` (~119–124) |
| Discovery is unconditional when endpoint is set | `embedder_factory.get_embedder` |
| 02:36 Admin Desktop log truncates after ApiClient | `src/logs/local-processor.log` |
| Lifecycle tests empty `EMBEDDING_PROVIDER_ENDPOINT` to avoid this path | `processor_transport.rs` `start_real_processor` |
| `Embedder` ABC: `generate_embedding`, `generate_embeddings_batch`, `check_status` | `embeddings/embedder.py` |
| Health unwraps `TruncatingEmbedder._embedder` for endpoint/model attrs | `main.py` `_build_health_response` |
| Separate readiness mismatch exists but is not this symptom | `processor.ts`, `_build_health_response` |

**Resolved planning Q&A:** Q1=C (exit status 1); Q2=A (lazy-init + stderr); Q3=TS readiness out of scope.

---

## 4. Task list

| # | Phase | Component | Description | Skills | Depends on |
|---|-------|-----------|-------------|--------|------------|
| T1 | Fix | local-processing-service | Implement D2. **Files/symbols:** `2-Application/local-processing-service/src/embeddings/embedder_factory.py` (`get_embedder`); new lazy `Embedder` type (dedicated module — one class per file); `src/main.py` (`embedder = get_embedder()`, `_build_health_response`, `health_check`); tests in `5-Test/local-processing-service.Tests/embeddings/test_embedder_factory.py` (+ health tests if present). Lazy proxy performs existing discovery+construction only on first method call. On init failure: `check_status` → `"disconnected"` (never raise); embed methods raise clear runtime error for jobs; process stays up. Expose configured endpoint/model for health before successful inner init. **Acceptance:** endpoint set + discovery failing/mocked → `get_embedder()` / import does not raise or network; authenticated `/health` returns; embedding_provider disconnected/unhealthy. | `python-dev`, `test-dev` | — |
| T2 | Fix | Admin Desktop Rust | Implement D3. **Files/symbols:** `1-Presentation/MotorcycleRAG.AdminDesktop/src-tauri/src/lib.rs` (`processor_start`, `wait_for_processor_listening`, `child_exit_description`; spawn `.stderr(Stdio::null())`); tests in `lib.rs` / `processor_transport.rs` lifecycle coverage. Pipe stderr; on early exit or listen timeout append bounded redacted stderr tail (or log-path pointer). Redact `MCR_LOCAL_PROCESSOR_CONTROL_TOKEN` and secret-shaped values. **Acceptance:** child that prints discovery/import error and exits 1 → `processor_start` error contains that text (redacted), not only `exit status: 1`. | Admin Desktop Rust / `tauri-dev`, `test-dev` | — |
| T3 | Docs | 6-Docs | **Files:** `6-Docs/local-processing-service/local-processor.md`, `onboarding.md`; `6-Docs/catalog.md` only if public behavior summary requires it. Document listen vs healthy; embedder lazy-init; exit status 1 troubleshooting; Admin Desktop log path (`cwd` → `src/logs`). **Acceptance:** docs match D2/D3; no claim discovery runs at import. | `app-docs-standard` | T1, T2 |
| T4 | Verify | reviews | `code-reviewer` then `security-review` on T1–T3 diffs; stderr must not leak control token / upload-job secret. | `code-review`, `security-review` | T3 |

---

## 5. Sequencing / dependency graph

```
T1 ∥ T2  →  T3  →  T4
```

T1 and T2 are independent file scopes (Python vs Rust). Docs after both land. Reviews last.

---

## 6. Residual decisions / risks

- **None blocking.**
- Residual: `GraphExtractor`/`MetadataExtractor` still raise at import if graph env missing; Admin Desktop sets model and defaults endpoint — edge case if endpoint emptied; out of scope unless reproduced.
- Risk: Lazy proxy must satisfy `_build_health_response` getattr patterns (`_endpoint` / `_host` / `_base_url`, `_model`, unwrap `_embedder` for `TruncatingEmbedder`).
- Risk: First `/health` may trigger discovery (network); intentional; must not crash the process.

---

## 7. Out of scope

- Chrome profile auth / SignInScreen WIP.
- PyInstaller sidecar migration.
- Cloud API ingestion contract changes.
- Azure resource changes.
- TypeScript `processorIsReady` / `ensureProcessorReady` healthy-vs-degraded / missing-secret UX (D4) — follow-up plan if operators still hit “did not become ready” after this fix.
- Changing embedding provider selection policy beyond deferring it off the import path.

---

## 8. Required skills

`python-dev`, `test-dev`, Admin Desktop Rust (`tauri-dev` / scoped AGENTS), `app-docs-standard`, `code-review`, `security-review`

---

## 9. Verification harness

- **Python unit:** discovery failure / unreachable endpoint does not raise from `get_embedder()` or module import; `check_status` returns `disconnected`.
- **Python unit:** successful lazy path still wraps with `TruncatingEmbedder` and preserves prior factory behavior when provider is up.
- **Rust unit/lifecycle:** start failure includes stderr snippet; redaction of control token; happy-path start still works.
- **Manual:** Admin Desktop Start with LM Studio stopped → processor listens with embedding disconnected (or fails with visible discovery text if something else crashes) — not opaque exit 1 alone.
- **Reviews:** `code-reviewer`, `security-review`.
