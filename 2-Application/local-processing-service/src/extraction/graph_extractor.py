"""Graph entity and relationship extraction using an OpenAI-compatible LLM."""

import json
import logging
import os
from typing import Any

import openai

logger = logging.getLogger(__name__)

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


class GraphExtractor:
    """Extracts graph entities and relationships from text using an OpenAI-compatible LLM.

    Reads configuration from environment variables:
        GRAPH_EXTRACTION_ENDPOINT – OpenAI-compatible base URL (required)
        GRAPH_EXTRACTION_MODEL     – model name (required)
    """

    def __init__(self) -> None:
        endpoint = os.getenv("GRAPH_EXTRACTION_ENDPOINT")
        if not endpoint:
            raise ValueError(
                "GRAPH_EXTRACTION_ENDPOINT environment variable must be set"
            )
        self._endpoint = endpoint

        model = os.getenv("GRAPH_EXTRACTION_MODEL")
        if not model:
            raise ValueError(
                "GRAPH_EXTRACTION_MODEL environment variable must be set"
            )
        self._model = model

        # Cache a single client for the lifetime of the extractor instead of
        # creating one per extract() call. AsyncOpenAI reuses the underlying
        # httpx connection pool, which keeps the client lightweight to reuse.
        self._client = openai.AsyncOpenAI(base_url=self._endpoint, api_key="local")

    async def extract(self, text: str, source_document_id: str = "") -> list[dict[str, Any]]:
        """Extract graph entities and relationships. Always returns a list, never raises."""
        try:
            if not text or not text.strip():
                return []

            client = self._client
            response = await client.chat.completions.create(
                model=self._model,
                messages=[
                    {"role": "system", "content": SYSTEM_PROMPT},
                    {"role": "user", "content": text},
                ],
                temperature=0.1,
            )
            # Defensive check: LM Studio may return HTTP 200 with null choices
            # when the model is not loaded or the request is malformed.
            if response is None or not response.choices:
                logger.warning(
                    "Graph extraction LLM response has no choices (response=%s)",
                    response,
                )
                return []
            content = response.choices[0].message.content or ""
            result = json.loads(content)

            nodes = result.get("nodes", [])
            for node in nodes:
                node["sourceDocumentId"] = source_document_id
            edges = result.get("edges", [])

            return [{"nodes": nodes, "edges": edges}]

        except Exception as exc:
            logger.warning("Graph extraction failed: %s", exc)
            return []
