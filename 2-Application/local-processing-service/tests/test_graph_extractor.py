"""Unit tests for GraphExtractor — Ollama LLM calls fully mocked."""

import json
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

import pytest


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


class TestGraphExtractorInstantiation:
    def test_instantiates_without_error(self):
        from extraction.graph_extractor import GraphExtractor

        extractor = GraphExtractor()
        assert extractor._model is not None
        assert extractor._host is not None


class TestExtract:
    @patch("extraction.graph_extractor.ollama.AsyncClient")
    async def test_returns_empty_list_on_empty_input(self, MockAsyncClient):
        from extraction.graph_extractor import GraphExtractor

        extractor = GraphExtractor()
        result = await extractor.extract("")
        assert result == []

    @patch("extraction.graph_extractor.ollama.AsyncClient")
    async def test_returns_empty_list_on_whitespace(self, MockAsyncClient):
        from extraction.graph_extractor import GraphExtractor

        extractor = GraphExtractor()
        result = await extractor.extract("   \n\t  ")
        assert result == []

    @patch("extraction.graph_extractor.ollama.AsyncClient")
    async def test_returns_list_with_nodes_edges_on_valid_json(self, MockAsyncClient):
        from extraction.graph_extractor import GraphExtractor

        valid_json = json.dumps(
            {
                "nodes": [
                    {
                        "id": "1",
                        "name": "Oil Filter",
                        "type": "Component",
                        "description": "",
                    }
                ],
                "edges": [
                    {
                        "fromNodeId": "1",
                        "toNodeId": "2",
                        "relationshipType": "PART_OF",
                        "weight": 1.0,
                        "context": "",
                    }
                ],
            }
        )

        mock_client = MockAsyncClient.return_value
        mock_response = SimpleNamespace(message=SimpleNamespace(content=valid_json))
        mock_client.chat = AsyncMock(return_value=mock_response)

        extractor = GraphExtractor()
        result = await extractor.extract("Change the oil filter on the Honda CB500.")

        assert isinstance(result, list)
        assert len(result) == 1
        assert "nodes" in result[0]
        assert "edges" in result[0]
        assert len(result[0]["nodes"]) == 1
        assert "sourceDocumentId" in result[0]["nodes"][0]

    @patch("extraction.graph_extractor.ollama.AsyncClient")
    async def test_returns_sourceDocumentId_on_nodes(self, MockAsyncClient):
        from extraction.graph_extractor import GraphExtractor

        valid_json = json.dumps(
            {
                "nodes": [
                    {
                        "id": "1",
                        "name": "Brake Pad",
                        "type": "Component",
                        "description": "",
                    }
                ],
                "edges": [],
            }
        )

        mock_client = MockAsyncClient.return_value
        mock_response = SimpleNamespace(message=SimpleNamespace(content=valid_json))
        mock_client.chat = AsyncMock(return_value=mock_response)

        extractor = GraphExtractor()
        result = await extractor.extract("Replace brake pads", source_document_id="doc-123")

        assert len(result) == 1
        assert result[0]["nodes"][0]["sourceDocumentId"] == "doc-123"
    @patch("extraction.graph_extractor.ollama.AsyncClient")
    async def test_returns_empty_list_on_malformed_json(self, MockAsyncClient):
        from extraction.graph_extractor import GraphExtractor

        mock_client = MockAsyncClient.return_value
        mock_response = SimpleNamespace(
            message=SimpleNamespace(content="This is not JSON {{{")
        )
        mock_client.chat = AsyncMock(return_value=mock_response)

        extractor = GraphExtractor()
        result = await extractor.extract("Some motorcycle text")

        assert result == []

    @patch("extraction.graph_extractor.ollama.AsyncClient")
    async def test_returns_empty_list_on_connection_error(self, MockAsyncClient):
        from extraction.graph_extractor import GraphExtractor

        mock_client = MockAsyncClient.return_value
        mock_client.chat = AsyncMock(side_effect=ConnectionError("Ollama unreachable"))

        extractor = GraphExtractor()
        result = await extractor.extract("Some text about brakes")

        assert result == []
