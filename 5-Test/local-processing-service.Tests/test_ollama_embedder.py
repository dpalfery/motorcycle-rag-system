"""Unit tests for OllamaEmbedder — Ollama server calls fully mocked."""

import asyncio
from unittest.mock import AsyncMock, patch
from types import SimpleNamespace

import pytest

from embeddings.embedder import Embedder


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


class TestOllamaEmbedderInstantiation:
    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    def test_is_an_embedder(self, MockAsyncClient):
        from embeddings.ollama_embedder import OllamaEmbedder

        embedder = OllamaEmbedder()
        assert isinstance(embedder, Embedder)

    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    def test_instantiates_without_error(self, MockAsyncClient):
        from embeddings.ollama_embedder import OllamaEmbedder

        embedder = OllamaEmbedder()
        assert embedder._dims == 1536
        MockAsyncClient.assert_called_once()

    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    def test_invalid_timeout_env_falls_back_to_default(self, MockAsyncClient, monkeypatch):
        monkeypatch.setenv("EMBEDDING_REQUEST_TIMEOUT_SECONDS", "not-a-number")

        from embeddings.ollama_embedder import OllamaEmbedder

        embedder = OllamaEmbedder()

        assert embedder._request_timeout_seconds == 120.0
        assert embedder._health_timeout_seconds == 10.0


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
    @patch.dict("os.environ", {"OLLAMA_EMBEDDING_DIMS": "1536"}, clear=False)
    async def test_raises_value_error_on_wrong_dimensions(self, MockAsyncClient):
        """768-dim vector should raise ValueError when OLLAMA_EMBEDDING_DIMS=1536."""
        from embeddings.ollama_embedder import OllamaEmbedder

        mock_client = MockAsyncClient.return_value
        mock_response = SimpleNamespace(embeddings=[[0.1] * 768])
        mock_client.embed = AsyncMock(return_value=mock_response)

        embedder = OllamaEmbedder()
        with pytest.raises(ValueError, match="Expected 1536 dims"):
            await embedder.generate_embedding("test text")

    @patch("embeddings.ollama_embedder.asyncio.sleep", new_callable=AsyncMock)
    @patch("embeddings.ollama_embedder.asyncio.wait_for", new_callable=AsyncMock)
    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    async def test_retries_and_fails_on_timeout(
        self, MockAsyncClient, mock_wait_for, mock_sleep
    ):
        from embeddings.ollama_embedder import OllamaEmbedder

        mock_wait_for.side_effect = asyncio.TimeoutError

        embedder = OllamaEmbedder()
        with pytest.raises(RuntimeError, match="Ollama embedding failed after 3 retries"):
            await embedder.generate_embedding("test text")

        assert mock_wait_for.await_count == 3

    @patch("embeddings.ollama_embedder.asyncio.sleep", new_callable=AsyncMock)
    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    async def test_retries_transient_error_then_returns_embedding(self, MockAsyncClient, mock_sleep):
        from embeddings.ollama_embedder import OllamaEmbedder

        mock_client = MockAsyncClient.return_value
        mock_client.embed = AsyncMock(
            side_effect=[ConnectionError("temporary"), SimpleNamespace(embeddings=[[0.5] * 1536])]
        )

        result = await OllamaEmbedder().generate_embedding("test")

        assert result == [0.5] * 1536
        assert mock_client.embed.await_count == 2
        mock_sleep.assert_awaited_once_with(1)

    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    async def test_batch_runs_each_input(self, MockAsyncClient):
        from embeddings.ollama_embedder import OllamaEmbedder

        mock_client = MockAsyncClient.return_value
        mock_client.embed = AsyncMock(return_value=SimpleNamespace(embeddings=[[0.5] * 1536]))

        result = await OllamaEmbedder().generate_embeddings_batch(["one", "two"])

        assert result == [[0.5] * 1536, [0.5] * 1536]
        assert mock_client.embed.await_count == 2


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

    @patch("embeddings.ollama_embedder.asyncio.wait_for", new_callable=AsyncMock)
    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    async def test_returns_disconnected_on_health_timeout(
        self, MockAsyncClient, mock_wait_for
    ):
        from embeddings.ollama_embedder import OllamaEmbedder

        mock_wait_for.side_effect = asyncio.TimeoutError

        embedder = OllamaEmbedder()
        status = await embedder.check_ollama_status()

        assert status == "disconnected"

    @patch("embeddings.ollama_embedder.ollama.AsyncClient")
    async def test_check_status_delegates_and_cached_status_skips_second_request(self, MockAsyncClient):
        from embeddings.ollama_embedder import OllamaEmbedder

        mock_client = MockAsyncClient.return_value
        mock_client.list = AsyncMock(return_value={"models": []})
        embedder = OllamaEmbedder()

        assert await embedder.check_status() == "connected"
        assert await embedder.check_ollama_status() == "connected"
        assert mock_client.list.await_count == 1
