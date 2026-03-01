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
        assert embedder._dims == 1536
        MockAsyncClient.assert_called_once()


class TestGenerateEmbedding:
    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    async def test_returns_1536_floats(self, MockAsyncClient):
        from embeddings.ollama_embedder import OllamaEmbedder

        mock_client = MockAsyncClient.return_value
        # embed returns object with .embeddings attribute
        mock_response = SimpleNamespace(embeddings=[[0.5] * 1536])
        mock_client.embed = AsyncMock(return_value=mock_response)

        embedder = OllamaEmbedder()
        result = await embedder.generate_embedding("test text")

        assert isinstance(result, list)
        assert len(result) == 1536
        assert all(isinstance(v, float) for v in result)

    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    async def test_truncates_larger_vectors_to_1536(self, MockAsyncClient):
        """Matryoshka truncation: 3584 dims → 1536."""
        from embeddings.ollama_embedder import OllamaEmbedder

        mock_client = MockAsyncClient.return_value
        mock_response = SimpleNamespace(embeddings=[[0.1] * 3584])
        mock_client.embed = AsyncMock(return_value=mock_response)

        embedder = OllamaEmbedder()
        result = await embedder.generate_embedding("test text")
        assert len(result) == 1536

    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    async def test_raises_value_error_on_wrong_dimensions(self, MockAsyncClient):
        """768-dim vector (less than 1536) should raise ValueError."""
        from embeddings.ollama_embedder import OllamaEmbedder

        mock_client = MockAsyncClient.return_value
        mock_response = SimpleNamespace(embeddings=[[0.1] * 768])
        mock_client.embed = AsyncMock(return_value=mock_response)

        embedder = OllamaEmbedder()
        with pytest.raises(ValueError, match="Expected 1536 dims"):
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
