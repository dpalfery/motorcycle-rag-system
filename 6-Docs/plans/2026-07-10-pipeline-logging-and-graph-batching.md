# Pipeline Logging Audit + Graph Extraction Batching

**Status:** Final
**Date:** 2026-07-10
**Goal:** Add per-call LLM telemetry and per-stage timing across the PDF pipeline, and replace the single full-document graph-extraction LLM call with batched + deduped extraction so `phi-4-reasoning-plus` reliably produces a non-empty knowledge graph for long manuals.

---

## 1. Problem / Motivation

### Symptom (from LM Studio logs)
After the 2026-07-09 endpoint fix, the metadata request reached `microsoft/phi-4-reasoning-plus` and it began thinking — but `stopGenerating()` was called mid-generation (23:19:39), unloading the model. The operator had no clear telemetry to diagnose whether the cause was context overflow, idle-timeout unload, or a malformed response. Graph extraction silently returned `[]` for long documents.

### Root causes (verified against live source)
1. **Graph extraction sends the ENTIRE document in one LLM call.** `pdf_processor.py:690-691` joins every HybridChunker chunk into `combined_text` and hands it to `GraphExtractor.extract(combined_text, ...)` as a single user message. `phi-4-reasoning-plus` is a reasoning model that emits long chain-of-thought; a full-manual call is slow and at high risk of interruption (the observed `stopGenerating()` unload).
2. **Graph extraction has no retry.** `graph_extractor.extract()` catches all exceptions and returns `[]`. A single transient unload or timeout kills the entire extraction for that document. (`metadata_extractor` already retries; `graph_extractor` does not.)
3. **LLM-call telemetry is missing in both extractors.** Neither `graph_extractor.py` nor `metadata_extractor.py` logs the `model`, `endpoint`, input size, response time, or result status on each call. This is THE gap that prevents diagnosing the unload/overflow scenario.
4. **Per-stage wall-clock timing is absent** in `pdf_processor.py` — there is no way to tell how long each pipeline stage took.

### Non-cause (corrected during planning)
The graph-extraction model is **`microsoft/phi-4-reasoning-plus`** and the main-branch source already reflects this (`.env:86`, `.env.example:71`, `config.ts:41`; extractors raise `ValueError` with no code fallback). The earlier `qwen3.5-0.8b` references in the 2026-07-09 RCA/fix plan documents were a **false premise** — they do not match the live config. No graph-model code cleanup is required on the main branch.

---

## 2. Approved Decisions

| ID | Decision |
|----|----------|
| **D0** | The graph-extraction model is `microsoft/phi-4-reasoning-plus`. Main-branch source is already correct. This plan and all future work never reference qwen as the graph-extraction model. (Stale `qwen3.5-0.8b` graph refs exist only in the `.kilo` worktree and historical plan docs — both out of scope.) |
| **D1** | **Logging = full audit + rewrite** of every `logger.*` call in `graph_extractor.py`, `metadata_extractor.py`, and `pdf_processor.py`. Standardize on the existing printf key=value style: every line carries a component tag + `job_id` (when applicable); every LLM call line carries `elapsed_ms`. **Token counts use `len(text)//4`** (approximate, clearly labeled `tokens_approx`); no new dependencies (no tiktoken). |
| **D2** | **Graph batching lives inside `graph_extractor.extract()`.** Split incoming text into **4000-token (~16000-char) batches** via `chars//4`, one LLM call per batch. Merge: dedupe nodes by lowercased `name` (first-seen-wins, preserve its `id`/`type`/`description`); build a `batch_node_id → canonical_node_id` map; rewrite every edge's `fromNodeId`/`toNodeId` to canonical ids; dedupe edges by `(canonical_from, canonical_to, relationshipType)`; drop edges referencing unknown node ids (debug-log). On a batch failing after retries: log a warning and continue with remaining batches (partial results). `pdf_processor.py:690-691` call site stays a single line. |
| **D3** | **Metadata sampling stays iterative** `PAGE_SAMPLE_SIZES = [3, 6, 9, 10]` (stops at first 100% fill rate). It is a strict superset of a hard 3-page cap; reverting would be a regression. Only the surrounding logging is improved. |
| **D4** | **Add retry to `graph_extractor`** mirroring `metadata_extractor._query_llm_with_retry`: retry on `openai.APITimeoutError` / `openai.APIConnectionError` / message containing "unreachable", exponential backoff (`1.0 * (attempt+1)` seconds), max 2 retries. No keep-alive pings, no LM Studio lifecycle management. |
| **D5** | **Add input/output body logging (additive to D1).** Every LLM call must additionally emit, at `logger.debug` level: the prompt text actually sent (messages/content, truncated to the first **2000 chars** with a `...[truncated N chars]` suffix when longer) and the response text returned by LM Studio (truncated the same way). This is in addition to the D1 `info`-level summary stats (`model`, `endpoint`, `input_chars`, `tokens_approx`, `elapsed_ms`, `result`). Rationale: lets the operator diagnose failures directly from the app logs without needing LM Studio's own logs. Truncation guard = 2000 chars; the `N` in the suffix is the number of omitted chars so the operator knows how much was elided. The truncation helper is shared (a single `_truncate(text, limit=2000) -> str` utility) to avoid drift. |

---

## 3. Investigation Findings

- **`graph_extractor.py`** (94 lines): `extract()` sends `text` as one user message (line 70). Defensive null-choices check exists (lines 76-81). No retry, no batching, near-zero logging.
- **`metadata_extractor.py`** (416 lines): already mature — iterative `PAGE_SAMPLE_SIZES = [3,6,9,10]` (line 62), `_parse_llm_json` with markdown/trailing-comma fallbacks (lines 233-279), `_query_llm_with_retry` (lines 327-363), `check_connectivity()` probe (lines 96-140), response length+sha debug logging (lines 319-324). **Gap:** does not log `model`, `endpoint`, input size, or `elapsed_ms`.
- **`pdf_processor.py`** (791 lines): stage transitions via `_set_stage` + `logger.info` at most boundaries; graph call at lines 690-691. **Gap:** no per-stage wall-clock timing; no `combined_text` size logging.
- **Embeddings:** `TruncatingEmbedder` unwrap health fix is an **uncommitted diff in `main.py`** — a separate in-flight task, out of scope here.
- **Config (grep-verified):** graph model = `microsoft/phi-4-reasoning-plus` everywhere on the main branch. Qwen refs are the embedding model (Qwen3-Embedding) — intentionally separate and correct.
- **Tests:** `tests/test_graph_extractor.py` exists (ValueError-on-missing-env-var coverage); room for batching/dedupe/retry tests.

---

## 4. Task List

### T1 — Batched + deduped + retrying graph extraction with telemetry

| Field | Value |
|-------|-------|
| **Objective** | Rewrite `graph_extractor.extract()` to split text into 4000-token batches, call the LLM per batch with retry, merge/dedupe nodes+edges across batches, and emit per-call telemetry. Preserve the existing contract: always returns a `list[dict]`, never raises. |
| **Exact files** | `2-Application/local-processing-service/src/extraction/graph_extractor.py` |
| **Symbols to add** | `BATCH_TOKEN_BUDGET = 4000` constant; `_LOG_TRUNCATE = 2000` constant; `_approx_tokens(text) -> int`; `_split_into_batches(text) -> list[str]`; `_truncate(text, limit=_LOG_TRUNCATE) -> str` (returns text if shorter than limit, else `text[:limit] + f"...[truncated {len(text)-limit} chars]"`); `_query_llm_with_retry(client, batch_text, batch_index, total_batches, source_document_id) -> dict | None`; `_merge_results(batch_results: list[dict]) -> dict` (node name-map + canonical-id edge rewrite + edge dedupe). Rewrite `extract()` to orchestrate: batch → loop retry-extract → merge → log totals. |
| **Telemetry fields (every LLM call, `info`)** | `component=graph_extraction`, `job_id` (n/a here but log `source_document_id`), `model`, `endpoint`, `batch_index`, `total_batches`, `input_chars`, `tokens_approx`, `elapsed_ms`, `result` ∈ {`ok`, `empty`, `no_choices`, `error`, `retried`}, `node_count`, `edge_count`. |
| **Body logging (every LLM call, `debug`)** | Per D5, immediately around each `client.chat.completions.create(...)` call emit two `logger.debug` lines (or one with both fields): the **prompt text sent** (`prompt=...`, truncated via `_truncate`) and the **response text returned** (`response=...`, truncated via `_truncate`). When the response is multi-choice, log `choices[0].message.content`. On error, log the exception message as the "response" body so the operator sees the failure inline. This runs inside `_query_llm_with_retry` so it covers retry attempts too. |
| **Merge rules** | Node dedupe key = `name.strip().lower()`; first-seen-wins (keep its `id`,`type`,`description`, then set `sourceDocumentId`). Edge endpoint rewrite via name-map by looking up each batch-local node `id` → its lowercased name → canonical id. Drop edges whose endpoints cannot be resolved (debug-log count). Edge dedupe key = `(from_canonical, to_canonical, relationshipType)`. |
| **Failure handling** | Batch raises after retries → `logger.warning("graph_extraction batch failed ...")`, continue. Empty text or empty batches → return `[]`. All paths return `list[dict]` (currently shape `[{"nodes":[...],"edges":[...]}]` — keep that shape). |
| **Acceptance criteria** | 1. A >4000-token input produces ≥2 LLM calls (verified via mock). 2. Two batches emitting a node named "Brake" (different ids) collapse to one node with one canonical id. 3. An edge in batch 2 referencing a batch-2 id that dedupes to a batch-1 node is rewritten to the batch-1 id. 4. A batch whose call raises "unreachable" twice then succeeds on retry 3 returns that batch's result. 5. A batch that fails all retries is skipped; remaining batches still produce results; no exception propagates. 6. Each LLM call emits an `info` telemetry line (`model=`, `endpoint=`, `elapsed_ms=`, `result=`) **and** a `debug` line containing the truncated prompt (`prompt=`) and response (`response=`); prompt/response longer than 2000 chars end in `...[truncated N chars]`. 7. `mypy` + `pyright` clean; existing tests pass. |
| **Skill** | python-dev |
| **Dependencies** | None |

### T2 — Audit + standardize logging in MetadataExtractor

| Field | Value |
|-------|-------|
| **Objective** | Audit every `logger.*` call in `metadata_extractor.py`; ensure each LLM-call path logs `model`, `endpoint`, `input_chars`, `tokens_approx`, `elapsed_ms`, `result`. Add D5 input/output body logging at `debug` level (truncated prompt + response). Keep the existing iterative sampling, JSON-parsing, retry, and connectivity probe unchanged in behavior. |
| **Exact files** | `2-Application/local-processing-service/src/extraction/metadata_extractor.py` |
| **Symbols** | Wrap the `client.chat.completions.create(...)` in `_query_llm` with `time.perf_counter()` timing; add `input_chars`/`tokens_approx`/`elapsed_ms`/`result` to the existing debug log (lines 319-324) and promote key fields to `info`. Add D5 body logging: a `logger.debug` line emitting the truncated prompt (`prompt=`) and truncated response (`response=`) for every `_query_llm` call (import/reuse `_truncate` — defined in T1's `graph_extractor.py`; if a shared module is preferred, put it in a small local util, but avoid a new file unless an existing util module exists). Standardize the retry warnings (lines 350-360) and connectivity probe (lines 122-134) to the component+key=value convention. Add `_approx_tokens` helper. |
| **Acceptance criteria** | 1. Every LLM call emits one `info` line containing `model`, `endpoint`, `input_chars`, `tokens_approx`, `elapsed_ms`, `result`, **and** one `debug` line containing the truncated `prompt=` and `response=`. 2. No PII regression — `source_path` still only logged as basename; full path only goes to the endpoint (unchanged). 3. Body logging is `debug`-level so it does not spam normal/info operation but is always available when debug logging is enabled. 4. Behavior unchanged: sampling schedule, retry counts, JSON parsing, fill-rate logic identical. 5. `mypy` + `pyright` clean; existing `test_metadata_extractor.py` passes. |
| **Skill** | python-dev |
| **Dependencies** | None |

### T3 — Per-stage timing + log audit in pdf_processor

| Field | Value |
|-------|-------|
| **Objective** | Add wall-clock timing per pipeline stage to `pdf_processor.py` and audit/standardize all `logger.*` calls. Log the `combined_text` size, a truncated preview of `combined_text` (D5), and the batch count that `GraphExtractor` will use at the graph-extraction call site. |
| **Exact files** | `2-Application/local-processing-service/src/processors/pdf_processor.py` |
| **Symbols** | Add a small timing helper (e.g. `_stage_started: dict[str, float]` on the processor keyed by stage name, set in/around `_set_stage`; log `stage`, `elapsed_ms` on each transition). Add `len(combined_text)` + `len(combined_text)//4` + expected batch count (`ceil(tokens/4000)`) to the existing "extracting graph entities" info block (lines 685-688). Add a `logger.debug` line emitting the truncated `combined_text` preview (`combined_text_preview=...`, via the shared `_truncate`) immediately before the `extract(...)` call at lines 690-691 — so the operator can see the exact text handed to the extractor when debugging. Standardize all `logger.*` lines to `pdf_processor` component tag + `job_id`. |
| **Call-site note** | Line 690-691 stays a single call — batching is internal to `extract()` (T1). Do NOT loop here. |
| **Acceptance criteria** | 1. Each stage transition logs the previous stage's `elapsed_ms`. 2. The graph-extraction stage logs `combined_chars`, `combined_tokens_approx`, `expected_batches`, **and** a `debug`-level `combined_text_preview=` (truncated to 2000 chars with `...[truncated N chars]` suffix when longer). 3. After T1, the post-extraction log reports actual node/edge counts (T1 returns merged result; processor logs `len(entities[0]["nodes"])` / `["edges"]`). 4. All `logger.*` lines use consistent component+`job_id` format. 5. Behavior unchanged (stages, pause/resume, cancel handling). 6. `mypy` + `pyright` clean; existing pdf_processor tests pass. |
| **Skill** | python-dev |
| **Dependencies** | None (disjoint file from T1/T2). T3's post-extraction count logging benefits from T1's merged-result shape but does not block it — can be wired in Wave 1 and verified after T1 lands. |

### T4 — Tests for graph batching / dedupe / retry + telemetry assertions

| Field | Value |
|-------|-------|
| **Objective** | Add unit tests covering T1's new batching, merge/dedupe, partial-failure, and retry behavior, plus caplog assertions that telemetry fields appear. |
| **Exact files** | `2-Application/local-processing-service/tests/test_graph_extractor.py` |
| **Test cases** | (a) input >4000 tokens → mock client called ≥2 times. (b) two batches each return a node `{"name":"Brake",...}` with different ids → merged result has one "Brake" node. (c) batch-2 edge referencing a batch-2 node id that dedupes to a batch-1 node → edge rewritten to batch-1 id. (d) one batch's call raises "unreachable" twice then succeeds → result included, retry logged. (e) one batch fails all retries → skipped, other batches' results still returned, no exception. (f) empty/whitespace text → returns `[]`. (g) caplog assertion (with `caplog.set_level(logging.DEBUG)`): a successful call log contains `info`-level `model=`, `endpoint=`, `tokens_approx=`, `elapsed_ms=` **and** `debug`-level `prompt=` + `response=`; a prompt/response longer than 2000 chars is suffixed with `...[truncated N chars]`. (h) caplog assertion that a failed batch's `debug` response line carries the exception message inline. |
| **Acceptance criteria** | 1. All new tests pass via `pytest tests/test_graph_extractor.py -v`. 2. Tests use `monkeypatch` + a fake/mock async client (consistent with existing test style). 3. No live LM Studio calls in CI. 4. `mypy` + `pyright` clean. |
| **Skill** | test-dev |
| **Dependencies** | **T1** (tests the new batching logic). |

---

## 5. Sequencing / Dependency Graph

```
WAVE 1 (parallel — disjoint files)
   T1  graph_extractor.py     (batching + dedupe + retry + telemetry)
   T2  metadata_extractor.py  (logging audit + telemetry)
   T3  pdf_processor.py       (stage timing + logging audit + call-site size log)
        │
        ▼
WAVE 2
   T4  test_graph_extractor.py  ← depends on T1
        │
        ▼
WAVE 3
   code-reviewer  (correctness + quality)
   security-review (no PII/log-leak regression; source_path handling intact)
```

| Wave | Tasks | Notes |
|------|-------|-------|
| 1 | T1, T2, T3 | All independent (different files). T3's post-extraction count log assumes T1's merged-result shape; safe to author in Wave 1, verified after T1. |
| 2 | T4 | Tests the T1 batching logic; needs T1 complete. |
| 3 | Reviews | code-reviewer + security-review before marking complete. |

---

## 6. Residual Decisions / Risks

| # | Risk / Open item | Owner | Condition to resolve |
|---|------------------|-------|----------------------|
| R1 | **Name-based dedupe collapses same-name different-type nodes** (e.g. a Component "Brake" and a Procedure "Brake" become one node). D2 chose Option 1 (name-only) over name+type. | python-dev | If the merged graph shows semantic loss in practice, revisit to name+type tuple dedupe (D2 alt). |
| R2 | **4000-token batch budget may need tuning** for the operator's specific LM Studio GPU memory / idle-timeout. If phi-4-reasoning-plus still unloads mid-batch, lower the budget or lengthen retry backoff. | Operator / python-dev | Confirm via the manual run in the verification harness. Budget is a constant (`BATCH_TOKEN_BUDGET`); easy to change. |
| R3 | **Reasoning-model CoT can still exceed output budget** for a dense batch, producing truncated JSON. `_parse_llm_json`-style resilience does NOT exist in `graph_extractor` (it uses bare `json.loads`). | python-dev | Consider porting `metadata_extractor._parse_llm_json` to graph extraction if truncated-JSON failures appear in the manual run. Flagged as a likely follow-up, not blocked. |
| R4 | **Dropped edges with unresolved endpoints** remove some LLM-produced relationships. Debug-log count is emitted; acceptable for graph quality but worth monitoring. | python-dev | Review debug log counts during manual run. |
| R5 | **T3 post-extraction count logging** depends on T1's merged-result shape `[{"nodes":..., "edges":...}]`. If T1 changes the return shape, T3's count extraction must follow. | python-dev | T1's acceptance criteria pin the existing shape; verify in Wave 2. |
| R6 | **`_truncate` helper placement.** D5 calls for a shared truncation utility used by graph_extractor, metadata_extractor, and pdf_processor. If an existing small util module exists in `local-processing-service/src/`, prefer importing from it; otherwise define `_truncate` in `graph_extractor.py` (T1, first implementation) and import elsewhere, or duplicate the trivial one-liner. Avoid creating a new file solely for this helper (architecture rule: no new folders/files without approval). | python-dev | Confirm placement during T1 implementation; keep all three copies identical if duplicated. |
| R7 | **Document content visible in debug logs.** D5 body logging emits actual prompt/response text at `debug`, which includes PDF page content and extracted metadata. For motorcycle manuals this is not PII, but if the service is ever pointed at sensitive documents, debug logs would expose that content. Mitigation: body logging is `debug`-only (off in normal/info operation), and `metadata_extractor` still never logs `source_path` in full. | security-review | Confirm during Wave 3 review that no `info`-level line leaks document content and that `source_path` handling is unchanged. |

---

## 7. Out of Scope

| Item | Why |
|------|-----|
| `.kilo/worktrees/prong-tugboat/` stale `qwen3.5-0.8b` graph refs | Separate git worktree/branch — not a main-branch deliverable. Worktree hygiene, not a pipeline change. |
| Editing 2026-07-09 RCA/fix plan documents to remove the false qwen premise | Historical records; D0 records the correction going forward. Not source code. |
| `main.py` TruncatingEmbedder unwrap (embedding health fix) | Separate in-flight task (uncommitted diff); unrelated to pipeline logging/graph batching. |
| LM Studio keep-alive pings / model auto-reload / idle-timeout config | LM Studio's responsibility, not ours. D4 retry is the in-app mitigation. |
| Reverting metadata sampling to a hard 3-page cap | D3: iterative is strictly better. |
| Adding tiktoken / python-json-logger dependencies | D1: approximate tokens + existing log format; no new deps. |
| C# / TypeScript / Rust changes | All work is in `local-processing-service` Python. |
| Tokenizer reuse from HybridChunker | D1: approximate `chars//4` is sufficient for diagnosability. |

---

## 8. Required Skills

| Skill | Used for |
|-------|----------|
| python-dev | T1 (graph_extractor), T2 (metadata_extractor), T3 (pdf_processor) |
| test-dev | T4 (graph extraction tests + caplog assertions) |

Code-reviewer and security-review are verification gates (Wave 3), not implementation skills.

---

## 9. Verification Harness

### Compile / type gates
- `mypy` (authoritative Pydantic-aware checker) clean on the three changed source files. Per project memory: `python_version="3.12"`; bare `# type: ignore[code]` comments only.
- `pyright` (basic mode) clean.
- `python -m py_compile` on all three files succeeds.

### Test gates
- `pytest tests/test_graph_extractor.py tests/test_metadata_extractor.py -v` — all existing + new T4 tests pass.
- `pytest` (full suite) — no regressions.

### Review gates
- **code-reviewer:** correctness + quality pass on T1–T4 before marking complete.
- **security-review:** confirm no PII/log-leak regression — specifically that `source_path` is still only basename-logged in `metadata_extractor`; that graph-extraction telemetry never logs full file paths; and that D5 body logging (`prompt=`/`response=`) is `debug`-level only so document content does not appear in normal `info`-level operation.

### Manual verification (operator)
1. LM Studio running with `microsoft/phi-4-reasoning-plus` loaded.
2. Process a long PDF (≥30 pages) via the Admin Desktop.
3. Confirm the Python log shows: per-stage `elapsed_ms`; the graph stage's `combined_chars` / `combined_tokens_approx` / `expected_batches` (info) plus `combined_text_preview=` (debug); per-batch LLM telemetry lines (`model=`, `endpoint=`, `batch_index=`, `elapsed_ms=`, `result=`, `node_count=`, `edge_count=`); per-batch `debug` body lines (`prompt=`, `response=`) showing the actual text sent to and returned from LM Studio; and a final merged `node_count` / `edge_count`.
4. Confirm the uploaded graph is **non-empty** (previously `[]` for long docs).
5. If `stopGenerating()` unload recurs, confirm the retry path fires (`result=retried` lines) and at least partial batches succeed. Inspect the `debug` `response=` lines to see whether the model returned partial/truncated JSON (informs R3 follow-up).
6. To see body logs: run with debug logging enabled for the extraction modules (e.g. `LOG_LEVEL=DEBUG` or per-module debug; confirm the app's logging config surfaces `graph_extraction`/`metadata_extraction` at DEBUG).
