"""Unit tests for OllamaEmbedder — Ollama server calls fully mocked."""

from unittest.mock import AsyncMock, MagicMock, patch, PropertyMock
from types import SimpleNamespace

import pytest


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


class TestOllamaEmbedderInstantiation:
    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    def test_instantiates_without_error(self, MockAsyncClient):
        from embeddings.ollama_embedder import OllamaEmbedder

        embedder = OllamaEmbedder()
        assert embedder._dims is None
        MockAsyncClient.assert_called_once()


class TestGenerateEmbedding:
    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    async def test_returns_3584_floats(self, MockAsyncClient):
        from embeddings.ollama_embedder import OllamaEmbedder

        mock_client = MockAsyncClient.return_value
        # embed returns object with .embeddings attribute
        mock_response = SimpleNamespace(embeddings=[[0.5] * 3584])
        mock_client.embed = AsyncMock(return_value=mock_response)

        embedder = OllamaEmbedder()
        result = await embedder.generate_embedding("test text")

        assert isinstance(result, list)
        assert len(result) == 3584
        assert all(isinstance(v, float) for v in result)


    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    @patch.dict("os.environ", {"OLLAMA_EMBEDDING_DIMS": "3584"}, clear=False)
    async def test_raises_value_error_on_wrong_dimensions(self, MockAsyncClient):
        """768-dim vector should raise ValueError when OLLAMA_EMBEDDING_DIMS=3584."""
        from embeddings.ollama_embedder import OllamaEmbedder

        mock_client = MockAsyncClient.return_value
        mock_response = SimpleNamespace(embeddings=[[0.1] * 768])
        mock_client.embed = AsyncMock(return_value=mock_response)

        embedder = OllamaEmbedder()
        with pytest.raises(ValueError, match="Expected 3584 dims"):
            await embedder.generate_embedding("test text")


class TestCheckOllamaStatus:
    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    async def test_returns_connected_on_ok(self, MockAsyncClient):
        from embeddings.ollama_embedder import OllamaEmbedder

        mock_client = MockAsyncClient.return_value
        mock_client.list = AsyncMock(return_value={"models": []})

        embedder = OllamaEmbedder()
        status = await embedder.check_ollama_status()
        assert status == "connected"

    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    async def test_returns_disconnected_on_error(self, MockAsyncClient):
        from embeddings.ollama_embedder import OllamaEmbedder

        mock_client = MockAsyncClient.return_value
        mock_client.list = AsyncMock(side_effect=ConnectionError("no server"))

        embedder = OllamaEmbedder()
        status = await embedder.check_ollama_status()
        assert status == "disconnected"
