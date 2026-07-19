"""Motorcycle metadata extraction using an OpenAI-compatible LLM.

Iteratively samples the first N pages of a parsed PDF (1 -> 2 -> 3 pages)
and asks the LLM for make, model, year, category, and tags. Sampling stops as
soon as all four required fields are filled (fill rate == 1.0).

The caller is responsible for producing the per-page text list. Docling exposes
this via ``DoclingDocument.export_to_text(page_no=N)`` (one string per page);
``document.pages`` is a ``dict[int, PageItem]`` keyed by 1-based page number.
Phase 2 (pdf_processor integration) wires that up; this module stays pure by
operating on a ``list[str]``.
"""

import asyncio
import hashlib
import json
import logging
import os
import re
import time
from typing import Any

import httpx
import openai

from security.log_sanitizer import sanitize_log_value

logger = logging.getLogger(__name__)

_LOG_TRUNCATE = 2000


def _truncate(text: str, limit: int = _LOG_TRUNCATE) -> str:
    """Truncate text to ``limit`` chars, appending a count if truncated."""
    if len(text) <= limit:
        return text
    return text[:limit] + f"...[truncated {len(text) - limit} chars]"


def _approx_tokens(text: str) -> int:
    """Rough token estimate (4 chars per token)."""
    return len(text) // 4


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


class MetadataExtractor:
    """Extracts motorcycle metadata from PDF text using an OpenAI-compatible LLM.

    Reads configuration from the same environment variables as GraphExtractor:
        GRAPH_EXTRACTION_ENDPOINT - OpenAI-compatible base URL (required)
        GRAPH_EXTRACTION_MODEL     - model name (required)

    The extractor never raises: LLM connection errors and malformed JSON are
    logged as warnings and treated as a failed attempt so the pipeline can fall
    back to manual metadata entry.

    Security: when ``source_path`` is provided to ``extract()``, the full path
    is transmitted to the inference endpoint inside the LLM user message. This
    is safe by default because the endpoint is a local LM Studio server; if the
    endpoint is reconfigured to a remote host, see ``extract()`` for caveats.
    """

    #: Iterative page sample sizes, capped at 3 pages.
    PAGE_SAMPLE_SIZES = [1, 2, 3]
    #: Fields required for a 100% fill rate.
    REQUIRED_FIELDS = ["make", "model", "year", "category"]

    def __init__(self) -> None:
        endpoint = os.getenv("GRAPH_EXTRACTION_ENDPOINT")
        if not endpoint:
            raise ValueError(
                "GRAPH_EXTRACTION_ENDPOINT environment variable must be set"
            )
        self._endpoint = endpoint

        model = os.getenv("GRAPH_EXTRACTION_MODEL")
        if not model:
            raise ValueError("GRAPH_EXTRACTION_MODEL environment variable must be set")
        self._model = model

        # Cache a single client for the lifetime of the extractor instead of
        # creating one per extract() call. AsyncOpenAI reuses the underlying
        # httpx connection pool, which keeps the client lightweight to reuse.
        self._client = openai.AsyncOpenAI(base_url=self._endpoint, api_key="local")

        # Best-effort connectivity probe: fires a background task so it never
        # blocks or fails startup.  Only schedules when there is a running
        # event loop (async context); no-ops in sync contexts.
        try:
            asyncio.get_running_loop()
        except RuntimeError:
            pass
        else:
            asyncio.create_task(self.check_connectivity())

    async def check_connectivity(self) -> None:
        """Probe the LLM endpoint to confirm the configured model is available.

        Makes a ``GET`` request to ``{base_url}/models`` and logs whether the
        configured model is found.  Never raises — all errors are swallowed
        and logged as warnings for operator visibility.
        """
        models_url = f"{self._endpoint.rstrip('/')}/models"
        try:
            async with httpx.AsyncClient(timeout=httpx.Timeout(5.0)) as client:
                response = await client.get(models_url)
                response.raise_for_status()
                data = response.json()

                # OpenAI-compatible endpoints return {"data": [{"id": "...", ...}]}
                available: list[str] = []
                if isinstance(data, dict):
                    raw_data = data.get("data")
                    if isinstance(raw_data, list):
                        available = [
                            str(item["id"])
                            for item in raw_data
                            if isinstance(item, dict) and "id" in item
                        ]

                if self._model in available:
                    logger.info(
                        "component=metadata_extraction model='%s' confirmed endpoint=%s",
                        sanitize_log_value(self._model),
                        sanitize_log_value(self._endpoint),
                    )
                else:
                    logger.warning(
                        "component=metadata_extraction model='%s' NOT FOUND endpoint=%s "
                        "available=%s",
                        sanitize_log_value(self._model),
                        sanitize_log_value(self._endpoint),
                        sanitize_log_value(str(available)),
                    )
        except Exception as exc:
            logger.warning(
                "component=metadata_extraction endpoint=%s probe_failed error=%s",
                sanitize_log_value(self._endpoint),
                sanitize_log_value(str(exc)),
            )

    async def extract(
        self,
        pages: list[str],
        job_id: str | None = None,
        source_path: str | None = None,
    ) -> dict[str, Any]:
        """Iteratively sample pages until fill rate is 100% or max pages reached.

        Args:
            pages: Per-page text strings from the parsed PDF, in page order.
                Fewer pages than a sample size is fine; the sample is clamped.
            job_id: Optional job identifier for log correlation. Included in
                log lines so failures can be traced back to a specific PDF job.
            source_path: Optional original file path of the source document
                (e.g. ``/data/manuals/2023/Honda/CBR600RR/service-manual.pdf``).
                When provided, the **full** path is sent to the LLM endpoint as
                leading context so the model can infer year/make/model hints
                from directory names. Never logged in full; only the basename
                may appear in debug logs.

                Security note: the entire filesystem path is transmitted to the
                inference endpoint inside the LLM user message (see
                ``_build_user_content``). If ``GRAPH_EXTRACTION_ENDPOINT``
                points to a remote host rather than a local LM Studio server,
                absolute file paths will leave the machine over the network; in
                that case pass ``source_path=None`` for untrusted paths or
                strip the path to a basename before calling.

        Returns:
            dict with keys: make, model, year, category, tags, fill_rate,
            pages_sampled. Always returns a dict; never raises.
        """
        if source_path:
            logger.debug(
                "Metadata extraction using file path context basename=%s",
                sanitize_log_value(os.path.basename(source_path)),
            )
        # Prefix log messages with the job_id when provided for correlation.
        safe_job_id = sanitize_log_value(job_id)
        jid_tag = f" job_id={safe_job_id}" if job_id else ""
        best_result: dict[str, Any] = {
            "make": None,
            "model": None,
            "year": 0,
            "category": None,
            "tags": [],
            "fill_rate": 0.0,
            "pages_sampled": 0,
        }

        if not pages:
            return best_result

        for sample_size in self.PAGE_SAMPLE_SIZES:
            actual_size = min(sample_size, len(pages))
            if actual_size <= best_result["pages_sampled"]:
                continue

            sample_text = "\n\n".join(pages[:actual_size])
            try:
                parsed = await self._query_llm_with_retry(
                    self._client, sample_text, job_id=job_id, source_path=source_path
                )
            except Exception:
                logger.warning(
                    "Metadata extraction LLM call failed after all retries%s",
                    jid_tag,  # codeql[py/log-injection]
                )
                break
            self._merge(best_result, parsed)

            best_result["pages_sampled"] = actual_size
            best_result["fill_rate"] = self._compute_fill_rate(best_result)

            logger.info(
                "component=metadata_extraction job_id=%s "
                "sample_size=%d actual_pages=%d fill_rate=%.2f",
                safe_job_id or "?",  # codeql[py/log-injection]
                sample_size,
                actual_size,
                best_result["fill_rate"],
            )

            if best_result["fill_rate"] >= 1.0:
                logger.info(
                    "Metadata extraction reached 100%% fill rate after %d pages%s",
                    actual_size,
                    jid_tag,  # codeql[py/log-injection]
                )
                break

        if best_result["fill_rate"] < 1.0:
            logger.info(
                "Metadata extraction incomplete: %.0f%% fill rate after %d pages%s",
                best_result["fill_rate"] * 100,
                best_result["pages_sampled"],
                jid_tag,  # codeql[py/log-injection]
            )

        return best_result

    @staticmethod
    def _parse_llm_json(content: str | None) -> dict[str, Any]:
        """Parse JSON from LLM response, handling common formatting issues.

        Handles markdown fences, explanatory text, trailing commas, and other
        common LLM output quirks. Returns ``{}`` when no valid JSON is found.
        """
        if content is None:
            return {}
        if not content.strip():
            return {}

        text = content.strip()

        # Strip markdown code fences (````json ... ````` or ```` ... `````)
        if text.startswith("```"):
            first_newline = text.find("\n")
            if first_newline != -1:
                text = text[first_newline + 1 :]
            if text.endswith("```"):
                text = text[:-3].rstrip()
            text = text.strip()

        # Try direct parse
        try:
            return json.loads(text)
        except json.JSONDecodeError:
            pass

        # Try to extract JSON object from mixed content
        json_match = re.search(
            r"\{[^{}]*(?:\{[^{}]*\}[^{}]*)*\}",
            text,
            re.DOTALL,
        )
        if json_match:
            try:
                return json.loads(json_match.group())
            except json.JSONDecodeError:
                pass

        # Try to fix common issues (trailing commas before ] or })
        fixed = re.sub(r",\s*([}\]])", r"\1", text)
        try:
            return json.loads(fixed)
        except json.JSONDecodeError:
            pass

        return {}

    async def _query_llm(
        self,
        client: Any,
        text: str,
        job_id: str | None = None,
        source_path: str | None = None,
    ) -> dict[str, Any]:
        """Call the LLM and parse the JSON response.

        Raises on connection errors so the caller can implement retry logic.
        JSON parse failures are handled gracefully by ``_parse_llm_json``.

        Args:
            source_path: Optional original file path. When provided it is
                prepended (verbatim, not pre-parsed) to the page text so the
                LLM can infer year/make/model from directory names.
        """
        user_content = self._build_user_content(text, source_path)
        call_start = time.perf_counter()
        input_chars = len(user_content)

        response = await client.chat.completions.create(
            model=self._model,
            messages=[
                {"role": "system", "content": METADATA_SYSTEM_PROMPT},
                {"role": "user", "content": user_content},
            ],
            temperature=0.1,
            max_tokens=300,
            response_format={"type": "json_object"},
        )
        elapsed_ms = int((time.perf_counter() - call_start) * 1000)

        # Defensive check: LM Studio may return HTTP 200 with null choices
        # when the model is not loaded or the request is malformed.
        if response is None or not response.choices:
            safe_job_id = sanitize_log_value(job_id)
            jid_suffix = f" job_id={safe_job_id}" if job_id else ""
            logger.warning(
                "LLM response has no choices (model=%s, id=%s)%s",
                sanitize_log_value(str(response.model)) if response else "N/A",
                sanitize_log_value(str(response.id)) if response else "N/A",
                jid_suffix,  # codeql[py/log-injection]
            )
            logger.info(
                "component=metadata_extraction job_id=%s model=%s endpoint=%s "
                "input_chars=%d tokens_approx=%d elapsed_ms=%d result=%s",
                sanitize_log_value(job_id) or "?",  # codeql[py/log-injection]
                sanitize_log_value(self._model),
                sanitize_log_value(self._endpoint),
                input_chars,
                _approx_tokens(user_content),
                elapsed_ms,
                "no_choices",
            )
            return {}
        content = response.choices[0].message.content or ""

        logger.info(
            "component=metadata_extraction job_id=%s model=%s endpoint=%s "
            "input_chars=%d tokens_approx=%d elapsed_ms=%d result=%s",
            sanitize_log_value(job_id) or "?",  # codeql[py/log-injection]
            sanitize_log_value(self._model),
            sanitize_log_value(self._endpoint),
            input_chars,
            _approx_tokens(user_content),
            elapsed_ms,
            "ok" if content else "empty",
        )

        logger.debug(
            "component=metadata_extraction job_id=%s prompt=%s response=%s",
            sanitize_log_value(job_id) or "?",  # codeql[py/log-injection]
            sanitize_log_value(_truncate(user_content)),
            sanitize_log_value(_truncate(content)),
        )

        sha_prefix = (
            hashlib.sha256(content.encode()).hexdigest()[:16] if content else "<empty>"
        )
        logger.debug(
            "LLM response received: length=%d sha256_prefix=%s%s",
            len(content),
            sha_prefix,
            f" job_id={sanitize_log_value(job_id)}" if job_id else "",  # codeql[py/log-injection]
        )
        return self._parse_llm_json(content)

    async def _query_llm_with_retry(
        self,
        client: Any,
        text: str,
        job_id: str | None = None,
        source_path: str | None = None,
        max_retries: int = 2,
    ) -> dict[str, Any]:
        """Call the LLM with retry for transient failures.

        Retries up to ``max_retries`` times on ``openai.APITimeoutError``,
        ``openai.APIConnectionError``, or exceptions whose message contains
        "unreachable".  All other errors propagate immediately.  After retries
        are exhausted the exception propagates to the caller.
        """
        for attempt in range(max_retries + 1):
            try:
                return await self._query_llm(
                    client,
                    text,
                    job_id,
                    source_path,
                )
            except Exception as exc:
                exc_lower = str(exc).lower()
                is_retryable = (
                    isinstance(exc, (openai.APITimeoutError, openai.APIConnectionError))
                    or "unreachable" in exc_lower
                )
                if is_retryable and attempt < max_retries:
                    delay = 1.0 * (attempt + 1)
                    logger.warning(
                        "component=metadata_extraction job_id=%s "
                        "attempt=%d/%d error=%s retrying_in=%.1fs",
                        sanitize_log_value(job_id) or "?",  # codeql[py/log-injection]
                        attempt + 1,
                        max_retries + 1,
                        sanitize_log_value(str(exc)[:200]),
                        delay,
                    )
                    await asyncio.sleep(delay)
                else:
                    logger.warning(
                        "component=metadata_extraction job_id=%s "
                        "attempt=%d/%d error=%s retries_exhausted",
                        sanitize_log_value(job_id) or "?",  # codeql[py/log-injection]
                        attempt + 1,
                        max_retries + 1,
                        sanitize_log_value(str(exc)[:200]),
                    )
                    raise

        return {}  # pragma: no cover

    @staticmethod
    def _build_user_content(text: str, source_path: str | None) -> str:
        """Build the LLM user message, optionally prefixing file-path context.

        The original file path is included verbatim (not pre-parsed) so the LLM
        can infer year/make/model hints from directory names. It is prepended
        BEFORE the page text so the model sees it as leading context.

        Security note: the full ``source_path`` becomes part of the user message
        sent to the inference endpoint. Safe when the endpoint is the default
        local LM Studio server; if the endpoint is remote the path is
        transmitted over the network. See ``extract()`` for details.
        """
        if not source_path:
            return text
        return (
            f"The original file path is: {source_path}\n\n"
            f"This path may contain hints about year, make, and model.\n\n"
            f"{text}"
        )

    def _merge(self, best: dict[str, Any], parsed: dict[str, Any]) -> None:
        """Merge parsed fields into the running best result.

        Only fills empty required slots (first non-empty value wins across
        iterations). Year is coerced to int; a non-numeric year is treated as
        unfilled (0). Tags are taken from the first response that provides them.
        """
        for field in self.REQUIRED_FIELDS:
            if field == "year":
                continue
            if not best.get(field) and parsed.get(field):
                best[field] = parsed[field]

        if not best.get("year") and parsed.get("year"):
            best["year"] = self._coerce_year(parsed["year"])

        if parsed.get("tags") and not best.get("tags"):
            best["tags"] = parsed["tags"]

    def _compute_fill_rate(self, metadata: dict[str, Any]) -> float:
        """Count non-empty required fields divided by the number of required fields."""
        filled = sum(1 for f in self.REQUIRED_FIELDS if metadata.get(f))
        return filled / len(self.REQUIRED_FIELDS)

    @staticmethod
    def _coerce_year(value: Any) -> int:
        """Coerce an LLM-supplied year value to int; 0 when not convertible."""
        try:
            return int(value)
        except (TypeError, ValueError):
            return 0
