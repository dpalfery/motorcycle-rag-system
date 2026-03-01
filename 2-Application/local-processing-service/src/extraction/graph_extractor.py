"""Graph entity and relationship extraction using Ollama LLM."""

import json
import logging
import os

import ollama

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
    """Extracts graph entities and relationships from text using Ollama LLM."""

    def __init__(self):
        self._host = os.getenv("OLLAMA_HOST", "http://localhost:11434")
        self._model = os.getenv("OLLAMA_MODEL_LLM", "qwen3:4b")

    async def extract(self, text: str, source_document_id: str = "") -> list:
        """Extract graph entities and relationships. Always returns a list, never raises."""
        try:
            if not text or not text.strip():
                return []

            client = ollama.AsyncClient(host=self._host)
            response = await client.chat(
                model=self._model,
                messages=[
                    {"role": "system", "content": SYSTEM_PROMPT},
                    {"role": "user", "content": text},
                ],
            )
            result = json.loads(response.message.content)

            nodes = result.get("nodes", [])
            for node in nodes:
                node["sourceDocumentId"] = source_document_id
            edges = result.get("edges", [])

            return [{"nodes": nodes, "edges": edges}]

        except Exception as e:
            logger.warning("Graph extraction failed: %s", e)
            return []
