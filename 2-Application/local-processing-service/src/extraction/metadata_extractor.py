"""Motorcycle metadata extraction using an OpenAI-compatible LLM.

Iteratively samples the first N pages of a parsed PDF (3 -> 6 -> 9 -> 10 pages)
and asks the LLM for make, model, year, category, and tags. Sampling stops as
soon as all four required fields are filled (fill rate == 1.0).

The caller is responsible for producing the per-page text list. Docling exposes
this via ``DoclingDocument.export_to_text(page_no=N)`` (one string per page);
``document.pages`` is a ``dict[int, PageItem]`` keyed by 1-based page number.
Phase 2 (pdf_processor integration) wires that up; this module stays pure by
operating on a ``list[str]``.
"""

import json
import logging
import os
from typing import Any

import openai

logger = logging.getLogger(__name__)

METADATA_SYSTEM_PROMPT = """You are a motorcycle document metadata extractor.
Given pages from a motorcycle manual or specification document, extract the following metadata:

Required fields:
- make: The manufacturer (e.g., "Honda", "Yamaha", "Kawasaki")
- model: The specific model name (e.g., "CBR600RR", "YZF-R1")
- year: The model year as an integer (e.g., 2023)
- category: The motorcycle category (e.g., "sport", "cruiser", "touring", "naked", "adventure", "off-road")

Optional field:
- tags: A list of relevant tags (e.g., ["sport", "inline-4", "600cc"])

Return ONLY a JSON object with this exact structure:
{
  "make": "Honda",
  "model": "CBR600RR",
  "year": 2023,
  "category": "sport",
  "tags": ["sport", "inline-4", "600cc"]
}

If a field cannot be determined, use null or an empty string for strings, and 0 for year.
Do not include markdown formatting or explanations."""


class MetadataExtractor:
    """Extracts motorcycle metadata from PDF text using an OpenAI-compatible LLM.

    Reads configuration from the same environment variables as GraphExtractor:
        GRAPH_EXTRACTION_ENDPOINT - OpenAI-compatible base URL
            (default: http://localhost:1234/v1)
        GRAPH_EXTRACTION_MODEL     - model name (default: qwen3.5-0.8b)

    The extractor never raises: LLM connection errors and malformed JSON are
    logged as warnings and treated as a failed attempt so the pipeline can fall
    back to manual metadata entry.
    """

    #: Iterative page sample sizes (inclusive growth, capped at 10 pages).
    PAGE_SAMPLE_SIZES = [3, 6, 9, 10]
    #: Fields required for a 100% fill rate.
    REQUIRED_FIELDS = ["make", "model", "year", "category"]

    def __init__(self) -> None:
        self._endpoint = os.getenv("GRAPH_EXTRACTION_ENDPOINT", "http://localhost:1234/v1")
        self._model = os.getenv("GRAPH_EXTRACTION_MODEL", "qwen3.5-0.8b")
        # Cache a single client for the lifetime of the extractor instead of
        # creating one per extract() call. AsyncOpenAI reuses the underlying
        # httpx connection pool, which keeps the client lightweight to reuse.
        self._client = openai.AsyncOpenAI(base_url=self._endpoint, api_key="local")

    async def extract(self, pages: list[str], job_id: str | None = None) -> dict[str, Any]:
        """Iteratively sample pages until fill rate is 100% or max pages reached.

        Args:
            pages: Per-page text strings from the parsed PDF, in page order.
                Fewer pages than a sample size is fine; the sample is clamped.
            job_id: Optional job identifier for log correlation. Included in
                log lines so failures can be traced back to a specific PDF job.

        Returns:
            dict with keys: make, model, year, category, tags, fill_rate,
            pages_sampled. Always returns a dict; never raises.
        """
        # Prefix log messages with the job_id when provided for correlation.
        jid_tag = f" job_id={job_id}" if job_id else ""
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
            parsed = await self._query_llm(self._client, sample_text, job_id=job_id)
            self._merge(best_result, parsed)

            best_result["pages_sampled"] = actual_size
            best_result["fill_rate"] = self._compute_fill_rate(best_result)

            if best_result["fill_rate"] >= 1.0:
                logger.info(
                    "Metadata extraction reached 100%% fill rate after %d pages%s",
                    actual_size,
                    jid_tag,
                )
                break

        if best_result["fill_rate"] < 1.0:
            logger.info(
                "Metadata extraction incomplete: %.0f%% fill rate after %d pages%s",
                best_result["fill_rate"] * 100,
                best_result["pages_sampled"],
                jid_tag,
            )

        return best_result

    async def _query_llm(
        self, client: Any, text: str, job_id: str | None = None
    ) -> dict[str, Any]:
        """Call the LLM and parse the JSON response.

        Returns an empty dict on any failure (connection error, non-JSON
        response, empty content) so the caller can treat it as a miss.
        """
        try:
            response = await client.chat.completions.create(
                model=self._model,
                messages=[
                    {"role": "system", "content": METADATA_SYSTEM_PROMPT},
                    {"role": "user", "content": text},
                ],
                temperature=0.1,
            )
            content = response.choices[0].message.content or "{}"
            return json.loads(content)
        except Exception as exc:  # noqa: BLE001 - intentional broad guard for LLM calls
            logger.warning(
                "Metadata extraction LLM call failed%s: %s",
                f" job_id={job_id}" if job_id else "",
                exc,
            )
            return {}

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
