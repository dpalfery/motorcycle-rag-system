# Revised Root Cause Analysis: Metadata Extraction Failure (Job 25)

**Status:** Draft  
**Date:** 2026-07-09  
**Goal:** Update root cause analysis for job 25's metadata extraction failure based on feedback about settings centralization and model choice.

---

## 1. Executive Summary

Job 25 failed with: *"Metadata extraction incomplete after 10 pages (fill rate 0%). Manual entry required."*

After investigation, the root causes are:

1. **JSON parsing fragility** — The LLM returns content that isn't pure JSON (markdown fences, explanations, empty strings), causing `json.loads()` to fail silently.
2. **LM Studio availability** — Some calls fail with "LM Studio unreachable" when the server isn't running or is overloaded.
3. **Settings are already centralized** — The admin app's settings page correctly configures `GRAPH_EXTRACTION_ENDPOINT` and `GRAPH_EXTRACTION_MODEL`, which are passed as env vars to the Python service. No architecture change needed for settings.

The extraction model is **whatever is configured in Admin Desktop Settings** (`graphExtractionModel` → `GRAPH_EXTRACTION_MODEL`), per plan decision D2. Designs must not hard-code a model name — the operator may change it (currently `microsoft/phi-4-reasoning-plus`). The fix should improve JSON parsing resilience, not pin a specific model.

---

## 2. Settings Architecture Investigation

### Current State (Already Working)

The admin app's settings page **already centralizes** the graph extraction configuration:

```
┌─────────────────────────────────────────────────────────────────┐
│  Admin Desktop Settings Screen                                  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ Graph extraction                                          │  │
│  │   Endpoint: [http://localhost:1234/v1]                    │  │
│  │   Model:    [<from Settings, e.g. microsoft/phi-4-…>]   │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  AppConfig (config.ts)                                          │
│  - graphExtractionEndpoint: string                              │
│  - graphExtractionModel: string                                 │
│  Persisted in: tauri-plugin-store → config.json                 │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  processor.ts → toStartConfig()                                 │
│  Maps AppConfig → ProcessorStartConfig                          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  Rust lib.rs → processor_start()                                │
│  envs.insert("GRAPH_EXTRACTION_ENDPOINT", ...)                  │
│  envs.insert("GRAPH_EXTRACTION_MODEL", ...)                     │
│  Spawns Python process with these env vars                      │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  Python metadata_extractor.py                                   │
│  self._endpoint = os.getenv("GRAPH_EXTRACTION_ENDPOINT", ...)  │
│  self._model = os.getenv("GRAPH_EXTRACTION_MODEL", ...)        │
└─────────────────────────────────────────────────────────────────┘
```

### Key Files

| File | Role |
|------|------|
| `1-Presentation/MotorcycleRAG.AdminDesktop/src/screens/SettingsScreen.tsx` | Settings UI with "Graph extraction" section |
| `1-Presentation/MotorcycleRAG.AdminDesktop/src/lib/config.ts` | `AppConfig` interface and persistence |
| `1-Presentation/MotorcycleRAG.AdminDesktop/src/lib/processor.ts` | `toStartConfig()` maps config to processor start params |
| `1-Presentation/MotorcycleRAG.AdminDesktop/src-tauri/src/lib.rs` | Rust passes env vars to Python process |
| `2-Application/local-processing-service/src/extraction/metadata_extractor.py` | Reads env vars in `__init__` |

### Conclusion

**No architecture change is needed for settings centralization.** The env var approach is the correct mechanism:
- Admin settings → `AppConfig` → `ProcessorStartConfig` → Rust env vars → Python `os.getenv()`
- Both `metadata_extractor.py` and `graph_extractor.py` read the same env vars
- The Python code isn't "hardcoding" — it's reading from env vars set by the admin app

---

## 3. Root Cause Analysis

### 3.1 Primary Cause: JSON Parsing Fragility

**Evidence from logs:**
```
WARNING  extraction.metadata_extractor — Metadata extraction LLM call failed: Expecting value: line 1 column 1 (char 0)
```

This error appears repeatedly in the logs. It means `json.loads()` received content that isn't valid JSON.

**Common failure modes:**
1. **Markdown fences** — LLM wraps JSON in `` ```json ... ``` ``
2. **Empty content** — LLM returns `""` or `None`
3. **Explanatory text** — LLM adds "Here is the metadata:" before the JSON
4. **Trailing commas** — Invalid JSON syntax
5. **Partial responses** — LLM truncates mid-JSON

**Current code behavior:**
```python
content = response.choices[0].message.content or "{}"
return json.loads(content)  # Fails if content isn't pure JSON
```

The `except` clause catches the error but returns `{}`, causing 0% fill rate.

### 3.2 Secondary Cause: LM Studio Availability

**Evidence from logs:**
```
WARNING  extraction.metadata_extractor — Metadata extraction LLM call failed: LM Studio unreachable
```

When LM Studio isn't running or is overloaded, all extraction attempts fail.

### 3.3 Non-Cause: Hard-Coded Model Name

Plan decision D2 requires reusing Settings / env config:
> "Reuse GraphExtractor's LM Studio endpoint and model config"

There is no single correct model name in the design. The operator configures the model in Admin Desktop Settings (currently `microsoft/phi-4-reasoning-plus`); that value is passed through as `GRAPH_EXTRACTION_MODEL`. Metadata extraction should use that configured model with proper JSON parsing.

---

## 4. Revised Proposed Fixes

### Fix 1: Robust JSON Parsing (Primary)

**File:** `2-Application/local-processing-service/src/extraction/metadata_extractor.py`

Add a `_parse_llm_json()` method that handles common LLM output formats:

```python
@staticmethod
def _parse_llm_json(content: str) -> dict[str, Any]:
    """Parse JSON from LLM response, handling common formatting issues."""
    if not content or not content.strip():
        return {}
    
    # Strip markdown code fences
    text = content.strip()
    if text.startswith("```"):
        # Remove opening fence (```json or ```)
        first_newline = text.find("\n")
        if first_newline != -1:
            text = text[first_newline + 1:]
        # Remove closing fence
        if text.endswith("```"):
            text = text[:-3].rstrip()
    
    # Try direct parse
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        pass
    
    # Try to extract JSON object from mixed content
    import re
    json_match = re.search(r'\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}', text, re.DOTALL)
    if json_match:
        try:
            return json.loads(json_match.group())
        except json.JSONDecodeError:
            pass
    
    # Try to fix common issues (trailing commas)
    fixed = re.sub(r',\s*([}\]])', r'\1', text)
    try:
        return json.loads(fixed)
    except json.JSONDecodeError:
        pass
    
    return {}
```

Then update `_query_llm()`:
```python
content = response.choices[0].message.content or ""
return self._parse_llm_json(content)
```

### Fix 2: Improved System Prompt

**File:** `2-Application/local-processing-service/src/extraction/metadata_extractor.py`

Make the prompt more explicit about output format:

```python
METADATA_SYSTEM_PROMPT = """You are a motorcycle document metadata extractor.

CRITICAL: Return ONLY a valid JSON object. No markdown, no explanations, no text before or after.

Extract these fields from the document:
- make: Manufacturer name (e.g., "Honda", "Yamaha")
- model: Model name (e.g., "CBR600RR", "YZF-R1")
- year: Model year as integer (e.g., 2023)
- category: Category (e.g., "sport", "cruiser", "touring")
- tags: List of relevant tags (e.g., ["sport", "inline-4"])

If a field cannot be determined, use null for strings and 0 for year.

Example output:
{"make":"Honda","model":"CBR600RR","year":2023,"category":"sport","tags":["sport","inline-4","600cc"]}"""
```

### Fix 3: Retry Logic for Transient Failures

**File:** `2-Application/local-processing-service/src/extraction/metadata_extractor.py`

Add retry logic for connection errors:

```python
async def _query_llm_with_retry(
    self, client: Any, text: str, job_id: str | None = None,
    source_path: str | None = None, max_retries: int = 2,
) -> dict[str, Any]:
    """Call LLM with retry for transient failures."""
    for attempt in range(max_retries + 1):
        try:
            return await self._query_llm(client, text, job_id, source_path)
        except Exception as exc:
            if attempt < max_retries and "unreachable" in str(exc).lower():
                logger.warning(
                    "LLM call failed (attempt %d/%d), retrying: %s",
                    attempt + 1, max_retries + 1, exc,
                )
                await asyncio.sleep(1.0 * (attempt + 1))  # Backoff
            else:
                raise
    return {}
```

### Fix 4: Logging Raw LLM Response (Debug Aid)

**File:** `2-Application/local-processing-service/src/extraction/metadata_extractor.py`

Add debug logging to capture raw LLM responses for troubleshooting:

```python
content = response.choices[0].message.content or ""
logger.debug(
    "LLM raw response%s: %s",
    f" job_id={job_id}" if job_id else "",
    content[:500] if content else "<empty>",
)
return self._parse_llm_json(content)
```

---

## 5. Impact Assessment

### Files That Would Change

| File | Change | Scope |
|------|--------|-------|
| `2-Application/local-processing-service/src/extraction/metadata_extractor.py` | Add `_parse_llm_json()`, update `_query_llm()`, improve prompt | Primary fix |
| `2-Application/local-processing-service/tests/test_metadata_extractor.py` | Add tests for JSON parsing edge cases | Test coverage |

### Files That Would NOT Change

| File | Reason |
|------|--------|
| `1-Presentation/MotorcycleRAG.AdminDesktop/src/screens/SettingsScreen.tsx` | Settings already correct |
| `1-Presentation/MotorcycleRAG.AdminDesktop/src/lib/config.ts` | Config already has graph extraction fields |
| `1-Presentation/MotorcycleRAG.AdminDesktop/src-tauri/src/lib.rs` | Already passes env vars correctly |
| `2-Application/local-processing-service/src/extraction/graph_extractor.py` | Different concern (graph extraction) |
| `2-Application/local-processing-service/src/processors/pdf_processor.py` | Integration already works |

### No Settings Architecture Changes Needed

The current architecture is correct:
- Admin settings page → `AppConfig` → `ProcessorStartConfig` → Rust env vars → Python `os.getenv()`
- Both `metadata_extractor.py` and `graph_extractor.py` read the same env vars
- The Python code isn't "hardcoding" — it's reading from env vars set by the admin app

---

## 6. Test Plan

### New Tests for `_parse_llm_json()`

| Test Case | Input | Expected Output |
|-----------|-------|-----------------|
| Pure JSON | `{"make":"Honda"}` | `{"make":"Honda"}` |
| Markdown fences | `` ```json\n{"make":"Honda"}\n``` `` | `{"make":"Honda"}` |
| Mixed content | `Here is the metadata:\n{"make":"Honda"}` | `{"make":"Honda"}` |
| Empty string | `""` | `{}` |
| None content | `None` | `{}` |
| Trailing commas | `{"make":"Honda",}` | `{"make":"Honda"}` |
| Partial JSON | `{"make":"Honda","model":` | `{}` (graceful failure) |

### Integration Test

1. Start LM Studio with the model configured in Admin Desktop Settings (currently `microsoft/phi-4-reasoning-plus`)
2. Upload a PDF with clear metadata (e.g., "2023 Honda CBR600RR Service Manual")
3. Verify metadata extraction succeeds with 100% fill rate
4. Check logs for raw LLM response (should be valid JSON)

---

## 7. Residual Risks

| Risk | Mitigation |
|------|------------|
| Model may still return invalid JSON for some documents | The `_parse_llm_json()` method handles common formats; manual fallback remains |
| LM Studio availability | Retry logic helps; manual fallback is the safety net |
| Configured model may struggle with complex documents | Manual fallback exists for this reason; operators may change the Settings model |

---

## 8. Out of Scope

| Item | Reason |
|------|--------|
| Pinning a specific model name in code or plans | Model comes from Settings and may change; D2 requires reusing that config |
| Adding a separate model config for metadata | Violates D2 ("no separate config") |
| Changing settings architecture | Already correct; env vars are the mechanism |
| Persisting extraction attempts in database | Only final metadata matters |

---

*Analysis revised based on feedback. Ready for implementation.*
