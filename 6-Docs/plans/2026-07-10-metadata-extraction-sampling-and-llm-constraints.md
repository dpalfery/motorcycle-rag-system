# Metadata Extraction: Page Sampling Reduction and LLM Response Constraints

**Status:** Draft
**Date:** 2026-07-10
**Goal:** Reduce metadata extraction page sampling from [3, 6, 9, 10] to [1, 2, 3], add `max_tokens=300` and `response_format={"type": "json_object"}` to the LLM call.

---

## 1. Problem / Motivation

The metadata extractor currently samples up to 10 pages of each PDF (iterations of 3 → 6 → 9 → 10 pages), and the LLM call has no token limit or response format constraint. Reducing the sample sizes to [1, 2, 3] cuts extraction cost and latency while constraining the response to JSON format and 300 tokens ensures predictable, parseable output.

---

## 2. Approved decisions

- **D1:** Change `PAGE_SAMPLE_SIZES` from `[3, 6, 9, 10]` to `[1, 2, 3]`. The cumulative `pages[:actual_size]` slicing logic in `extract()` requires no code change — it automatically produces: iteration 1 → `pages[:1]` (page 1 only), iteration 2 → `pages[:2]` (pages 1–2), iteration 3 → `pages[:3]` (pages 1–3).
- **D2:** Add `max_tokens=300` to the `client.chat.completions.create()` call. 300 tokens is generous for the 5-field metadata JSON (~50 tokens in the example).
- **D3:** Add `response_format={"type": "json_object"}` to the same call. This is already proven in `graph_extractor.py:169` with the same LM Studio endpoint. The system prompt contains the word "JSON" (line 45), satisfying the OpenAI JSON mode requirement.
- **D4:** `_parse_llm_json` (lines 256–302) stays **unchanged** as a defensive safety net. The fallback paths (markdown fence stripping, regex extraction, trailing-comma repair) become effectively dead code but are harmless.
- **D5:** Update all stale docstring/comment references that mention the old progression "(3 → 6 → 9 → 10)" or cap "(10)" as literals.

---

## 3. Investigation findings

| Item | Location | Detail |
|------|----------|--------|
| `PAGE_SAMPLE_SIZES` constant | `metadata_extractor.py:77-78` | Class-level constant on `MetadataExtractor`, value `[3, 6, 9, 10]` |
| Cumulative loop | `metadata_extractor.py:210-244` | `pages[:actual_size]` — cumulative from page 0; skip logic uses `actual_size <= best_result["pages_sampled"]` |
| LLM `create()` call | `metadata_extractor.py:322-329` | Has `model`, `messages`, `temperature=0.1` only |
| `_parse_llm_json` | `metadata_extractor.py:256-302` | 4-stage fallback: direct parse → regex extract → trailing-comma fix → `{}` |
| `_extract_page_texts` cap | `pdf_processor.py:399` | Reads `PAGE_SAMPLE_SIZES[-1]` dynamically — auto-adjusts from 10 to 3 |
| `response_format` precedent | `graph_extractor.py:169` | `response_format={"type": "json_object"}` already works with LM Studio |
| Test fixture mock | `test_pdf_processor.py:104` | `me.PAGE_SAMPLE_SIZES = [3, 6, 9, 10]` hardcoded in `MockMetadataExtractor` |
| Cap test | `test_pdf_processor.py:706-719` | `test_capped_to_page_sample_upper_bound` reads cap from mock dynamically — self-adjusting |
| Test assertions on kwargs | `test_metadata_extractor.py:370-390` | Only inspect `messages[1]["content"]` (source path); never check `response_format` or `max_tokens` |

**Resolved questions:**

- *Does the cumulative logic still work?* Yes. `[1, 2, 3]` with `pages[:actual_size]` sends pages 0:1, 0:2, 0:3 — cumulative from page 0, not incremental. Verified against the loop code.
- *Does `_parse_llm_json` still work with JSON mode?* Yes. It tries `json.loads()` first (line 267), which will succeed immediately with clean JSON. Fallback paths are unused but harmless.
- *Does `_extract_page_texts` need updating?* No code change — it reads `PAGE_SAMPLE_SIZES[-1]` dynamically. Only the docstring literal "(10)" needs updating.
- *Will existing tests break?* No tests assert on `response_format` or `max_tokens`. The `test_capped_to_page_sample_upper_bound` test reads the cap from the mock dynamically. The `MockMetadataExtractor` fixture hardcodes `[3, 6, 9, 10]` and should be updated for consistency.

---

## 4. Task list

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| 1 | Implementation | `metadata_extractor.py` — `PAGE_SAMPLE_SIZES` | Change constant from `[3, 6, 9, 10]` to `[1, 2, 3]` at line 78. Update comment at line 77 from "capped at 10 pages" to "capped at 3 pages". Update module docstring at line 3 from "(3 → 6 → 9 → 10 pages)" to "(1 → 2 → 3 pages)". | python-dev |
| 2 | Implementation | `metadata_extractor.py` — LLM call | Add `max_tokens=300,` and `response_format={"type": "json_object"},` to the `create()` call at lines 322–329. | python-dev |
| 3 | Implementation | `pdf_processor.py` — docstring | Update `_extract_page_texts` docstring at line 392: change "(10)" to "(3)" in "Returns up to ``MetadataExtractor.PAGE_SAMPLE_SIZES[-1]`` (10) page strings". | python-dev |
| 4 | Test update | `test_pdf_processor.py` — mock fixture | Update `MockMetadataExtractor` at line 104: change `me.PAGE_SAMPLE_SIZES = [3, 6, 9, 10]` to `me.PAGE_SAMPLE_SIZES = [1, 2, 3]`. | python-dev |
| 5 | Verification | Tests | Run full pytest suite in `local-processing-service`. Verify all existing tests pass. Confirm no test relies on the old sample sizes or cap. | test-dev |

---

## 5. Sequencing / dependency graph

```
Task 1 (PAGE_SAMPLE_SIZES constant + docstrings)  ─┐
Task 2 (max_tokens + response_format)               ├──> Task 4 (test mock fixture update) ──> Task 5 (pytest run)
Task 3 (pdf_processor docstring)                   ─┘
```

- Tasks 1, 2, 3 are independent and edit different locations (lines 3/77-78, lines 322-329, and a different file respectively). They can be done in a single editing pass.
- Task 4 (test fixture) should follow Task 1 to keep the mock consistent with the new constant.
- Task 5 (verification) runs after all source and test changes are complete.
- **No conflicts**: Tasks 1 and 2 touch different lines in the same file but do not overlap.

---

## 6. Residual decisions / risks

- **Risk — reduced fill rates:** With only 3 pages sampled (down from 10), metadata that appears on pages 4+ will not be extracted. This is an intentional behavioral tradeoff. If fill rates drop unacceptably, `PAGE_SAMPLE_SIZES` can be adjusted again without code changes beyond the constant. **Owner:** user to validate after testing with real PDFs.
- **Risk — `max_tokens` truncation:** If the model produces unexpectedly verbose JSON, 300 tokens could truncate mid-object. The output schema is small (5 fields, short strings) so this is unlikely. `_parse_llm_json` returns `{}` on truncated JSON, causing a fill-rate miss rather than a crash. **Mitigation:** verify with real extraction runs.
- **Risk — LM Studio JSON mode robustness:** `graph_extractor.py` uses the same parameter successfully, but metadata extraction has a different prompt and smaller context. The system prompt already instructs JSON-only output, so the model behavior should not change significantly. **Mitigation:** verify via test run with a real LM Studio model loaded.
- **Dead code in `_parse_llm_json`:** The fallback paths (markdown stripping, regex, trailing-comma repair) are now unlikely to execute. Left as a defensive safety net per D4. Not a risk — just a maintenance note.

---

## 7. Out of scope

- **Removing or simplifying `_parse_llm_json`:** Per D4, the function stays unchanged as a defensive safety net. Any future simplification is a separate decision.
- **Adding new tests that assert `response_format` or `max_tokens` are passed:** Not requested. Could be added later as a regression guard.
- **Adjusting the `METADATA_SYSTEM_PROMPT`:** No prompt changes needed. The existing prompt already requests JSON-only output and contains the word "JSON" (required for JSON mode).
- **Behavioral analysis of fill-rate impact:** The user will validate with real PDFs after implementation. This plan covers code changes only.

---

## 8. Required skills

- **python-dev** — all source code changes (constant, LLM call parameters, docstrings) and test fixture updates in `local-processing-service`.
- **test-dev** — running and verifying the pytest suite after changes.

---

## 9. Verification harness

1. **Unit tests:** Run `pytest` from `local-processing-service/`. All existing tests in `test_metadata_extractor.py` and `test_pdf_processor.py` must pass with no failures.
2. **Code review:** `code-reviewer` agent reviews the diff for correctness — specifically verifying the `create()` call has correct syntax for the two new kwargs and the `PAGE_SAMPLE_SIZES` value is exactly `[1, 2, 3]`.
3. **Manual spot-check (optional):** With LM Studio running and a model loaded, process a test PDF and confirm:
   - Only 3 LLM calls maximum (matching 3 sample sizes)
   - Response content is valid JSON (no markdown fences)
   - Metadata fields are populated correctly
