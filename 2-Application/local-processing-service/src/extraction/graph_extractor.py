"""Graph entity and relationship extraction using an OpenAI-compatible LLM.

Batched + deduped + retrying graph extraction with per-call I/O telemetry.
"""

import asyncio
import json
import logging
import os
import time
from typing import Any

import httpx
import openai

from security.log_sanitizer import sanitize_log_value
from security.safe_http import (
    create_model_provider_async_client,
    validate_model_provider_endpoint,
)

logger = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Batching & retry constants
# ---------------------------------------------------------------------------
BATCH_TOKEN_BUDGET = 4000  # approximate token budget per batch
BATCH_MAX_RETRIES = 2  # retries per batch before skipping
BATCH_RETRY_BASE_DELAY = 1.0  # seconds, multiplied by (attempt + 1)
_LOG_TRUNCATE = 2000  # chars for prompt/response body logging

# Preserve prior AsyncOpenAI/httpx defaults for local LLM chat (long read).
# Factory default is 10s, which would regress multi-minute completions.
_CHAT_HTTP_TIMEOUT = httpx.Timeout(
    connect=5.0,
    read=600.0,
    write=600.0,
    pool=600.0,
)

SYSTEM_PROMPT = """You are a knowledge graph extractor for motorcycle technical documentation.
Extract entities and relationships from the provided text.

Entity types: Procedure, Component, Motorcycle, Specification, Warning
Relationship types: REQUIRES, PART_OF, PRECEDES, REFERENCES

Return a JSON object with this exact structure:
{
  "nodes": [
    {"id": "<uuid>", "name": "<entity name>", "type": "<entity type>", "description": "<optional description>"}
  ],
  "edges": [
    {"fromNodeId": "<source node id>", "toNodeId": "<target node id>", "relationshipType": "<type>", "weight": 1.0, "context": "<optional sentence>"}
  ]
}

Return ONLY the JSON object. No markdown. No explanation."""


def _approx_tokens(text: str) -> int:
    """Rough token estimate: chars // 4."""
    return len(text) // 4


def _truncate(text: str, limit: int = _LOG_TRUNCATE) -> str:
    """Truncate text for logging, appending truncation notice."""
    if len(text) <= limit:
        return text
    return text[:limit] + f"...[truncated {len(text) - limit} chars]"


def _split_chunks_into_batches(
    chunks: list[tuple[str, str]],
) -> list[list[tuple[str, str]]]:
    """Group whole chunks into ~BATCH_TOKEN_BUDGET-token approximate batches.

    Groups whole ``(chunk_id, chunk_text)`` pairs so that a batch is always
    an integral set of whole chunks — a chunk's text is never split across
    two batches/LLM calls.

    If a single chunk's text alone exceeds the budget, it still becomes
    its own single-chunk batch and is sent to the LLM as-is — it is never
    dropped, only ever grown into its own batch when it can't share one.
    """
    budget_chars = BATCH_TOKEN_BUDGET * 4  # approximate chars per batch
    batches: list[list[tuple[str, str]]] = []
    current_batch: list[tuple[str, str]] = []
    current_chars = 0

    for chunk_id, chunk_text in chunks:
        chunk_chars = len(chunk_text)
        if current_batch and current_chars + chunk_chars > budget_chars:
            batches.append(current_batch)
            current_batch = []
            current_chars = 0
        current_batch.append((chunk_id, chunk_text))
        current_chars += chunk_chars

    if current_batch:
        batches.append(current_batch)

    return batches


def _merge_results(
    batch_results: list[dict[str, Any] | None],
    batch_chunk_ids: list[list[str]] | None = None,
) -> dict[str, Any]:
    """Merge batched graph extraction results.

    Node dedupe by lowercased name (first-seen-wins id/type/description).
    Edge endpoints rewritten from batch-local to canonical node ids.
    Edge dedupe by (from_canonical, to_canonical, relationshipType).

    ``batch_chunk_ids``, if provided, must be index-aligned with
    ``batch_results`` — ``batch_chunk_ids[i]`` is the list of chunk ids
    that contributed to the LLM call that produced ``batch_results[i]``.
    When provided, every merged node gains a ``sourceChunkIds`` key: the
    sorted union of chunk ids from every batch that contributed a node of
    that (lowercased) name. When omitted (the legacy raw-text contract),
    no ``sourceChunkIds`` attribution is possible and none is added.
    """
    all_nodes: list[dict[str, Any]] = []
    all_edges: list[dict[str, Any]] = []
    # Parallel to all_nodes: the contributing chunk ids for each node,
    # only populated when batch_chunk_ids is supplied.
    all_node_chunk_ids: list[list[str]] = []

    for batch_index, result in enumerate(batch_results):
        if result is None:
            continue
        nodes = result.get("nodes", []) or []
        edges = result.get("edges", []) or []
        chunk_ids_for_batch = (
            batch_chunk_ids[batch_index] if batch_chunk_ids is not None else []
        )
        all_nodes.extend(nodes)
        all_node_chunk_ids.extend([chunk_ids_for_batch] * len(nodes))
        all_edges.extend(edges)

    # Dedupe nodes by lowercased name (first-seen-wins), unioning the
    # contributing chunk ids across every batch that named this node.
    name_to_canonical: dict[str, dict[str, Any]] = {}
    name_to_chunk_ids: dict[str, set[str]] = {}
    for node, chunk_ids in zip(all_nodes, all_node_chunk_ids):
        name = (node.get("name") or "").strip().lower()
        if not name:
            continue
        if name not in name_to_canonical:
            name_to_canonical[name] = node
            name_to_chunk_ids[name] = set()
        if batch_chunk_ids is not None:
            name_to_chunk_ids[name].update(chunk_ids)

    if batch_chunk_ids is not None:
        for name, canonical_node in name_to_canonical.items():
            canonical_node["sourceChunkIds"] = sorted(name_to_chunk_ids[name])

    # Build id -> canonical_id mapping
    id_to_canonical: dict[str, str] = {}
    for node in all_nodes:
        node_id: str | Any = node.get("id")
        name = (node.get("name") or "").strip().lower()
        if node_id and name and name in name_to_canonical:
            id_to_canonical[node_id] = name_to_canonical[name].get("id", node_id)

    # Rewrite and dedupe edges
    # First-seen-wins edge deduplication: if multiple batches produce
    # the same (from_canonical, to_canonical, relationshipType) edge,
    # only the first occurrence is kept. Differing weight or context
    # from duplicate edges is discarded.
    seen_edges: set[tuple[str, str, str]] = set()
    merged_edges: list[dict[str, Any]] = []
    dropped = 0
    for edge in all_edges:
        from_id = id_to_canonical.get(
            edge.get("fromNodeId", ""), edge.get("fromNodeId", "")
        )
        to_id = id_to_canonical.get(edge.get("toNodeId", ""), edge.get("toNodeId", ""))
        rel_type = edge.get("relationshipType", "")

        # Drop edges referencing unknown node ids
        if not from_id or not to_id:
            dropped += 1
            logger.debug(
                "graph_merge dropping edge: unresolved endpoint from=%s to=%s",
                sanitize_log_value(str(edge.get("fromNodeId", ""))),
                sanitize_log_value(str(edge.get("toNodeId", ""))),
            )
            continue

        edge_key = (from_id, to_id, rel_type)
        if edge_key not in seen_edges:
            seen_edges.add(edge_key)
            merged_edge: dict[str, Any] = dict(edge)
            merged_edge["fromNodeId"] = from_id
            merged_edge["toNodeId"] = to_id
            merged_edges.append(merged_edge)

    if dropped:
        logger.debug("graph_merge dropped %d edges with unresolved endpoints", dropped)

    return {
        "nodes": list(name_to_canonical.values()),
        "edges": merged_edges,
    }


async def _query_llm_with_retry(
    client: openai.AsyncOpenAI,
    model: str,
    batch_text: str,
    batch_index: int,
    total_batches: int,
    source_document_id: str,
) -> dict[str, Any] | None:
    """Send one batch to the LLM with retry logic. Returns parsed JSON or None."""
    for attempt in range(BATCH_MAX_RETRIES + 1):
        start = time.perf_counter()
        try:
            response = await client.chat.completions.create(
                model=model,
                messages=[
                    {"role": "system", "content": SYSTEM_PROMPT},
                    {"role": "user", "content": batch_text},
                ],
                temperature=0.1,
                response_format={"type": "json_object"},
            )
            elapsed_ms = int((time.perf_counter() - start) * 1000)
            content = response.choices[0].message.content if response.choices else ""

            parsed: dict[str, Any] = json.loads(content) if content else {}
            nodes = parsed.get("nodes", []) if isinstance(parsed, dict) else []
            edges = parsed.get("edges", []) if isinstance(parsed, dict) else []
            node_count = len(nodes)
            edge_count = len(edges)

            # LOG SUMMARY (info level)
            logger.info(
                "component=graph_extraction job_id=%s model=%s endpoint=%s "
                "batch_index=%d/%d input_chars=%d tokens_approx=%d "
                "elapsed_ms=%d result=%s node_count=%d edge_count=%d",
                sanitize_log_value(source_document_id),  # codeql[py/log-injection]
                sanitize_log_value(model),
                sanitize_log_value(str(client.base_url)),
                batch_index + 1,
                total_batches,
                len(batch_text),
                _approx_tokens(batch_text),
                elapsed_ms,
                "ok",
                node_count,
                edge_count,
            )

            # LOG BODY (debug level)
            logger.debug(
                "component=graph_extraction job_id=%s batch_index=%d/%d "
                "prompt=%s response=%s",
                sanitize_log_value(source_document_id),  # codeql[py/log-injection]
                batch_index + 1,
                total_batches,
                sanitize_log_value(_truncate(batch_text)),
                sanitize_log_value(_truncate(content or "")),
            )

            return parsed

        except Exception as e:
            elapsed_ms = int((time.perf_counter() - start) * 1000)
            is_retryable = (
                isinstance(e, (openai.APITimeoutError, openai.APIConnectionError))
                or "unreachable" in str(e).lower()
            )

            if is_retryable and attempt < BATCH_MAX_RETRIES:
                delay = BATCH_RETRY_BASE_DELAY * (attempt + 1)
                logger.warning(
                    "component=graph_extraction job_id=%s batch_index=%d/%d "
                    "attempt=%d/%d error=%s retrying_in=%.1fs",
                    sanitize_log_value(source_document_id),  # codeql[py/log-injection]
                    batch_index + 1,
                    total_batches,
                    attempt + 1,
                    BATCH_MAX_RETRIES + 1,
                    sanitize_log_value(str(e)[:200]),
                    delay,
                )
                await asyncio.sleep(delay)
                continue

            logger.error(
                "component=graph_extraction job_id=%s batch_index=%d/%d "
                "attempt=%d/%d elapsed_ms=%d error=%s result=%s",
                sanitize_log_value(source_document_id),  # codeql[py/log-injection]
                batch_index + 1,
                total_batches,
                attempt + 1,
                BATCH_MAX_RETRIES + 1,
                elapsed_ms,
                sanitize_log_value(_truncate(str(e))),
                "error",
            )
            return None

    return None  # all retries exhausted


class GraphExtractor:
    """Extracts graph entities and relationships from text using an OpenAI-compatible LLM.

    Reads configuration from environment variables:
        GRAPH_EXTRACTION_ENDPOINT – OpenAI-compatible base URL (required)
        GRAPH_EXTRACTION_MODEL     – model name (required)

    ``GRAPH_EXTRACTION_ENDPOINT`` must be public HTTPS or literal-loopback HTTP;
    invalid endpoints raise ``EndpointPolicyError`` at construction. Outbound
    calls use a policy-bound ``safe_http`` transport injected into AsyncOpenAI.
    """

    def __init__(self) -> None:
        endpoint = os.getenv("GRAPH_EXTRACTION_ENDPOINT")
        if not endpoint:
            raise ValueError(
                "GRAPH_EXTRACTION_ENDPOINT environment variable must be set"
            )
        _, self._policy = validate_model_provider_endpoint(endpoint)
        self._endpoint = endpoint

        model = os.getenv("GRAPH_EXTRACTION_MODEL")
        if not model:
            raise ValueError("GRAPH_EXTRACTION_MODEL environment variable must be set")
        self._model = model

        # Cache a single policy-bound httpx client (and OpenAI wrapper) for the
        # lifetime of the extractor. AsyncOpenAI reuses the underlying pool.
        http_client = create_model_provider_async_client(
            self._policy,
            timeout=_CHAT_HTTP_TIMEOUT,
        )
        self._client = openai.AsyncOpenAI(
            base_url=self._endpoint,
            api_key="local",
            http_client=http_client,
        )

    async def extract(
        self,
        chunks: list[tuple[str, str]],
        source_document_id: str = "",
    ) -> list[dict[str, Any]]:
        """Extract knowledge graph entities/relationships.

        ``chunks`` is ``list[tuple[str, str]]`` — each item a
        ``(chunk_id, chunk_text)`` pair. Batches are re-aligned to
        whole-chunk boundaries under the existing ``BATCH_TOKEN_BUDGET``: a
        batch is always an integral set of whole chunks, never a
        sub-chunk slice. If a single chunk's text alone exceeds the
        budget, it still becomes its own single-chunk batch and is sent
        to the LLM — it is never dropped. Every merged node carries
        ``sourceChunkIds``: the sorted union of chunk ids from every batch
        that contributed a node of that (lowercased) name.

        A bare ``str`` is a contract violation and raises ``TypeError``.

        Returns ``[{"nodes": [...], "edges": [...]}]`` or ``[]``.
        """
        if not isinstance(chunks, list):
            raise TypeError(
                "chunks must be a list of (chunk_id, chunk_text) tuples, got "
                f"{type(chunks).__name__}"
            )
        if not chunks:
            logger.info(
                "component=graph_extraction job_id=%s result=empty_text returning_empty",
                sanitize_log_value(source_document_id),  # codeql[py/log-injection]
            )
            return []

        chunk_batches = _split_chunks_into_batches(chunks)
        num_batches = len(chunk_batches)
        total_chars = sum(len(chunk_text) for _, chunk_text in chunks)
        logger.info(
            "component=graph_extraction job_id=%s total_chars=%d tokens_approx=%d "
            "num_batches=%d",
            sanitize_log_value(source_document_id),  # codeql[py/log-injection]
            total_chars,
            total_chars // 4,
            num_batches,
        )

        batch_results = []
        batch_chunk_ids: list[list[str]] = []
        for i, batch_chunks in enumerate(chunk_batches):
            batch_text = "\n\n".join(
                chunk_text for _, chunk_text in batch_chunks
            )
            result = await _query_llm_with_retry(
                self._client,
                self._model,
                batch_text,
                i,
                num_batches,
                source_document_id,
            )
            batch_results.append(result)
            batch_chunk_ids.append([chunk_id for chunk_id, _ in batch_chunks])

        merged = _merge_results(batch_results, batch_chunk_ids)

        nodes = merged.get("nodes", [])
        edges = merged.get("edges", [])

        # Inject sourceDocumentId into each node (existing contract)
        for node in nodes:
            node["sourceDocumentId"] = source_document_id

        logger.info(
            "component=graph_extraction job_id=%s result=merged "
            "batches_processed=%d/%d total_nodes=%d total_edges=%d",
            sanitize_log_value(source_document_id),  # codeql[py/log-injection]
            sum(1 for r in batch_results if r is not None),
            num_batches,
            len(nodes),
            len(edges),
        )

        if not nodes and not edges:
            return []
        return [merged]
