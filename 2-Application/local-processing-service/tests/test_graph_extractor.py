"""Unit tests for GraphExtractor — OpenAI-compatible LLM calls fully mocked."""

import json
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch

import pytest


class TestGraphExtractorInstantiation:
    def test_instantiates_without_error(self):
        from extraction.graph_extractor import GraphExtractor

        extractor = GraphExtractor()
        assert extractor._endpoint == "http://localhost:9999/v1"
        assert extractor._model == "test-model"

    def test_raises_value_error_without_endpoint(self, monkeypatch):
        from extraction.graph_extractor import GraphExtractor

        monkeypatch.delenv("GRAPH_EXTRACTION_ENDPOINT", raising=False)
        with pytest.raises(ValueError, match="GRAPH_EXTRACTION_ENDPOINT"):
            GraphExtractor()

    def test_raises_value_error_without_model(self, monkeypatch):
        from extraction.graph_extractor import GraphExtractor

        monkeypatch.delenv("GRAPH_EXTRACTION_MODEL", raising=False)
        with pytest.raises(ValueError, match="GRAPH_EXTRACTION_MODEL"):
            GraphExtractor()


class TestExtract:
    @patch("extraction.graph_extractor.openai.AsyncOpenAI")
    async def test_returns_empty_list_on_empty_input(self, MockOpenAI):
        from extraction.graph_extractor import GraphExtractor

        extractor = GraphExtractor()
        result = await extractor.extract("")
        assert result == []

    @patch("extraction.graph_extractor.openai.AsyncOpenAI")
    async def test_returns_empty_list_on_whitespace(self, MockOpenAI):
        from extraction.graph_extractor import GraphExtractor

        extractor = GraphExtractor()
        result = await extractor.extract("   \n\t  ")
        assert result == []

    @patch("extraction.graph_extractor.openai.AsyncOpenAI")
    async def test_returns_list_with_nodes_edges_on_valid_json(self, MockOpenAI):
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

        mock_client = MockOpenAI.return_value
        mock_choice = SimpleNamespace(message=SimpleNamespace(content=valid_json))
        mock_response = SimpleNamespace(choices=[mock_choice])
        mock_client.chat.completions.create = AsyncMock(return_value=mock_response)

        extractor = GraphExtractor()
        result = await extractor.extract("Change the oil filter on the Honda CB500.")

        assert isinstance(result, list)
        assert len(result) == 1
        assert "nodes" in result[0]
        assert "edges" in result[0]
        assert len(result[0]["nodes"]) == 1
        assert "sourceDocumentId" in result[0]["nodes"][0]

    @patch("extraction.graph_extractor.openai.AsyncOpenAI")
    async def test_returns_sourceDocumentId_on_nodes(self, MockOpenAI):
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

        mock_client = MockOpenAI.return_value
        mock_choice = SimpleNamespace(message=SimpleNamespace(content=valid_json))
        mock_response = SimpleNamespace(choices=[mock_choice])
        mock_client.chat.completions.create = AsyncMock(return_value=mock_response)

        extractor = GraphExtractor()
        result = await extractor.extract("Replace brake pads", source_document_id="doc-123")

        assert len(result) == 1
        assert result[0]["nodes"][0]["sourceDocumentId"] == "doc-123"

    @patch("extraction.graph_extractor.openai.AsyncOpenAI")
    async def test_returns_empty_list_on_malformed_json(self, MockOpenAI):
        from extraction.graph_extractor import GraphExtractor

        mock_client = MockOpenAI.return_value
        mock_choice = SimpleNamespace(
            message=SimpleNamespace(content="This is not JSON {{{")
        )
        mock_response = SimpleNamespace(choices=[mock_choice])
        mock_client.chat.completions.create = AsyncMock(return_value=mock_response)

        extractor = GraphExtractor()
        result = await extractor.extract("Some motorcycle text")

        assert result == []

    @patch("extraction.graph_extractor.openai.AsyncOpenAI")
    async def test_returns_empty_list_on_connection_error(self, MockOpenAI):
        from extraction.graph_extractor import GraphExtractor

        mock_client = MockOpenAI.return_value
        mock_client.chat.completions.create = AsyncMock(
            side_effect=ConnectionError("API unreachable")
        )

        extractor = GraphExtractor()
        result = await extractor.extract("Some text about brakes")

        assert result == []
