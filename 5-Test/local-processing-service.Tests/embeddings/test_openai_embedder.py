"""Unit tests for OpenAIEmbedder — server calls fully mocked."""

import asyncio
import logging
from unittest.mock import AsyncMock, MagicMock, patch

import pytest


class TestOpenAIEmbedderInstantiation:
    def test_instantiates_with_defaults(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_PROVIDER_ENDPOINT", raising=False)
        monkeypatch.delenv("EMBEDDING_MODEL", raising=False)
        monkeypatch.delenv("EMBEDDING_DIMS", raising=False)

        with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
            from importlib import reload
            import embeddings.openai_embedder as mod

            reload(mod)

            embedder = mod.OpenAIEmbedder()

        assert embedder._dims == 1536
        assert embedder._endpoint == "http://localhost:1234/v1"
        assert embedder._model == "qwen3-embedding"
        assert embedder._health_timeout_seconds == 10.0

    def test_invalid_timeout_env_falls_back_to_default(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_REQUEST_TIMEOUT_SECONDS", "-1")

        with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
            from importlib import reload
            import embeddings.openai_embedder as mod

            reload(mod)

            embedder = mod.OpenAIEmbedder()

        assert embedder._request_timeout_seconds == 120.0

    def test_instantiates_with_custom_endpoint(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://myserver:9999")

        with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
            from importlib import reload
            import embeddings.openai_embedder as mod

            reload(mod)

            embedder = mod.OpenAIEmbedder()

        assert embedder._endpoint == "http://myserver:9999"

    def test_preserves_explicit_v1_suffix(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://127.0.0.1:1234/v1")

        with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
            from importlib import reload
            import embeddings.openai_embedder as mod

            reload(mod)

            embedder = mod.OpenAIEmbedder()

        assert embedder._endpoint == "http://127.0.0.1:1234/v1"

    def test_normalizes_model_list_url_to_base_v1_endpoint(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://127.0.0.1:1234/v1/models")

        with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
            from importlib import reload
            import embeddings.openai_embedder as mod

            reload(mod)

            embedder = mod.OpenAIEmbedder()

        assert embedder._endpoint == "http://127.0.0.1:1234/v1"

    def test_preserves_non_root_v1_path(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "https://api.example.com/v1/openai")

        with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
            from importlib import reload
            import embeddings.openai_embedder as mod

            reload(mod)

            embedder = mod.OpenAIEmbedder()

        assert embedder._endpoint == "https://api.example.com/v1/openai"

    def test_instantiates_with_constructor_params(self):
        with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
            from embeddings.openai_embedder import OpenAIEmbedder

            embedder = OpenAIEmbedder(
                endpoint="http://custom:8080/v1",
                model="custom-model",
                api_key="sk-test",
                dims=768,
                request_timeout_seconds=30.0,
                health_timeout_seconds=5.0,
            )

        assert embedder._endpoint == "http://custom:8080/v1"
        assert embedder._model == "custom-model"
        assert embedder._api_key == "sk-test"
        assert embedder._dims == 768
        assert embedder._request_timeout_seconds == 30.0
        assert embedder._health_timeout_seconds == 5.0


class TestOpenAIEmbedderGenerateEmbedding:
    def _make_mock_response(self, dims: int = 1536):
        embedding_obj = MagicMock()
        embedding_obj.embedding = [0.1] * dims

        response = MagicMock()
        response.data = [embedding_obj]
        return response

    async def test_returns_1536_floats(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_PROVIDER_ENDPOINT", raising=False)

        mock_client = MagicMock()
        mock_client.embeddings.create = AsyncMock(
            return_value=self._make_mock_response(1536)
        )

        with patch(
            "embeddings.openai_embedder.openai.AsyncOpenAI",
            return_value=mock_client,
        ):
            from importlib import reload
            import embeddings.openai_embedder as mod

            reload(mod)

            embedder = mod.OpenAIEmbedder()
            result = await embedder.generate_embedding("test text")

        assert isinstance(result, list)
        assert len(result) == 1536
        assert all(isinstance(v, float) for v in result)

    async def test_raises_on_wrong_dimensions(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_PROVIDER_ENDPOINT", raising=False)
        monkeypatch.setenv("EMBEDDING_DIMS", "1536")

        mock_client = MagicMock()
        mock_client.embeddings.create = AsyncMock(
            return_value=self._make_mock_response(512)
        )

        with patch(
            "embeddings.openai_embedder.openai.AsyncOpenAI",
            return_value=mock_client,
        ):
            from importlib import reload
            import embeddings.openai_embedder as mod

            reload(mod)

            embedder = mod.OpenAIEmbedder()

            with pytest.raises(ValueError, match="at least 1536 dims, got 512"):
                await embedder.generate_embedding("test text")

    async def test_accepts_oversized_vector_with_warning(
        self, monkeypatch, caplog
    ):
        # Oversized vectors must NOT raise: LM Studio ignores the requested
        # dimensionality and returns full-length vectors (e.g. 2560 dims).
        # OpenAIEmbedder returns them so TruncatingEmbedder can slice them.
        monkeypatch.delenv("EMBEDDING_PROVIDER_ENDPOINT", raising=False)
        monkeypatch.setenv("EMBEDDING_DIMS", "1536")

        mock_client = MagicMock()
        mock_client.embeddings.create = AsyncMock(
            return_value=self._make_mock_response(2560)
        )

        with patch(
            "embeddings.openai_embedder.openai.AsyncOpenAI",
            return_value=mock_client,
        ):
            from importlib import reload
            import embeddings.openai_embedder as mod

            reload(mod)

            embedder = mod.OpenAIEmbedder()
            with caplog.at_level(
                logging.WARNING, logger="embeddings.openai_embedder"
            ):
                result = await embedder.generate_embedding("test text")

        # The oversized vector is returned unchanged for downstream slicing.
        assert len(result) == 2560
        warning_text = " ".join(r.message for r in caplog.records)
        assert "2560" in warning_text and "1536" in warning_text

    async def test_retries_on_transient_error(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_PROVIDER_ENDPOINT", raising=False)

        good_response = self._make_mock_response(1536)
        call_count = 0

        async def flaky_create(**kwargs):
            nonlocal call_count
            call_count += 1
            if call_count < 3:
                raise Exception("transient network error")
            return good_response

        mock_client = MagicMock()
        mock_client.embeddings.create = flaky_create

        with patch(
            "embeddings.openai_embedder.openai.AsyncOpenAI",
            return_value=mock_client,
        ):
            with patch(
                "embeddings.openai_embedder.asyncio.sleep",
                new_callable=AsyncMock,
            ):
                from importlib import reload
                import embeddings.openai_embedder as mod

                reload(mod)

                embedder = mod.OpenAIEmbedder()
                result = await embedder.generate_embedding("test text")

        assert len(result) == 1536
        assert call_count == 3

    async def test_retries_and_fails_on_timeout(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_PROVIDER_ENDPOINT", raising=False)

        mock_client = MagicMock()
        mock_client.embeddings.create = MagicMock()

        with patch(
            "embeddings.openai_embedder.openai.AsyncOpenAI",
            return_value=mock_client,
        ):
            with patch(
                "embeddings.openai_embedder.asyncio.wait_for",
                new_callable=AsyncMock,
            ) as mock_wait_for:
                mock_wait_for.side_effect = asyncio.TimeoutError
                with patch(
                    "embeddings.openai_embedder.asyncio.sleep",
                    new_callable=AsyncMock,
                ):
                    from importlib import reload
                    import embeddings.openai_embedder as mod

                    reload(mod)

                    embedder = mod.OpenAIEmbedder()
                    with pytest.raises(
                        RuntimeError,
                        match="OpenAI embedding failed after 3 retries",
                    ):
                        await embedder.generate_embedding("test text")

        assert mock_wait_for.await_count == 3

    async def test_check_status_returns_disconnected_on_timeout(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_PROVIDER_ENDPOINT", raising=False)

        with patch(
            "embeddings.openai_embedder.openai.AsyncOpenAI",
        ):
            with patch(
                "embeddings.openai_embedder.httpx.AsyncClient",
            ) as mock_httpx_client:
                mock_client_instance = MagicMock()
                mock_client_instance.get = AsyncMock(
                    side_effect=asyncio.TimeoutError
                )
                mock_httpx_client.return_value.__aenter__.return_value = (
                    mock_client_instance
                )

                from importlib import reload
                import embeddings.openai_embedder as mod

                reload(mod)

                embedder = mod.OpenAIEmbedder()
                status = await embedder.check_status()

        assert status == "disconnected"


class TestOpenAIEmbedderBatch:
    async def test_batch_returns_multiple_embeddings(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_PROVIDER_ENDPOINT", raising=False)

        def make_response():
            embedding_obj = MagicMock()
            embedding_obj.embedding = [0.1] * 1536
            response = MagicMock()
            response.data = [embedding_obj]
            return response

        mock_client = MagicMock()
        mock_client.embeddings.create = AsyncMock(
            side_effect=[make_response(), make_response()]
        )

        with patch(
            "embeddings.openai_embedder.openai.AsyncOpenAI",
            return_value=mock_client,
        ):
            from importlib import reload
            import embeddings.openai_embedder as mod

            reload(mod)

            embedder = mod.OpenAIEmbedder()
            results = await embedder.generate_embeddings_batch(["a", "b"])

        assert isinstance(results, list)
        assert len(results) == 2
        assert all(len(emb) == 1536 for emb in results)


class TestOpenAIEmbedderHealth:
    async def test_check_status_returns_connected_and_caches_the_success(self):
        with patch("embeddings.openai_embedder.httpx.AsyncClient") as mock_httpx_client:
            response = MagicMock()
            response.raise_for_status = MagicMock()
            client = MagicMock()
            client.get = AsyncMock(return_value=response)
            mock_httpx_client.return_value.__aenter__.return_value = client

            from embeddings.openai_embedder import OpenAIEmbedder

            embedder = OpenAIEmbedder()
            assert await embedder.check_status() == "connected"
            assert await embedder.check_status() == "connected"

        assert client.get.await_count == 1


class TestOpenAIEmbedderCleanup:
    async def test_generate_embedding_closes_a_synchronous_client_close_method(self):
        response = MagicMock()
        response.data = [MagicMock(embedding=[0.1] * 2)]
        client = MagicMock()
        client.embeddings.create = AsyncMock(return_value=response)
        client.close = MagicMock(return_value=None)

        with patch("embeddings.openai_embedder.openai.AsyncOpenAI", return_value=client):
            from embeddings.openai_embedder import OpenAIEmbedder

            result = await OpenAIEmbedder(dims=2).generate_embedding("test")

        assert result == [0.1, 0.1]
        client.close.assert_called_once()
