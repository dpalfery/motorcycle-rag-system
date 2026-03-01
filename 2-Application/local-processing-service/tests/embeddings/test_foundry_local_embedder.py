"""Unit tests for AzureFoundryLocalEmbedder — Foundry Local server calls fully mocked."""

from unittest.mock import AsyncMock, MagicMock, patch

import pytest


class TestAzureFoundryLocalEmbedderInstantiation:
    def test_instantiates_with_defaults(self, monkeypatch):
        """Instantiates without env vars, uses default endpoint/model, dims == 3584."""
        monkeypatch.delenv("AZURE_FOUNDRY_LOCAL_ENDPOINT", raising=False)
        monkeypatch.delenv("AZURE_FOUNDRY_LOCAL_EMBEDDING_MODEL", raising=False)

        with patch("embeddings.foundry_local_embedder.openai.AsyncOpenAI"):
            from importlib import reload
            import embeddings.foundry_local_embedder as mod

            reload(mod)

            embedder = mod.AzureFoundryLocalEmbedder()

        assert embedder._dims == 3584
        assert embedder._endpoint == "http://localhost:5272"
        assert embedder._model == "qwen3-embedding"

    def test_instantiates_with_custom_endpoint(self, monkeypatch):
        """With AZURE_FOUNDRY_LOCAL_ENDPOINT set, uses that URL."""
        monkeypatch.setenv("AZURE_FOUNDRY_LOCAL_ENDPOINT", "http://myserver:9999")

        with patch("embeddings.foundry_local_embedder.openai.AsyncOpenAI"):
            from importlib import reload
            import embeddings.foundry_local_embedder as mod

            reload(mod)

            embedder = mod.AzureFoundryLocalEmbedder()

        assert embedder._endpoint == "http://myserver:9999"


class TestAzureFoundryLocalEmbedderGenerateEmbedding:
    def _make_mock_response(self, dims: int = 3584):
        """Build a mock openai embeddings response with *dims* floats."""
        embedding_obj = MagicMock()
        embedding_obj.embedding = [0.1] * dims

        response = MagicMock()
        response.data = [embedding_obj]
        return response

    async def test_returns_3584_floats(self, monkeypatch):
        """generate_embedding returns a list of 3584 floats when the API responds correctly."""
        monkeypatch.delenv("AZURE_FOUNDRY_LOCAL_ENDPOINT", raising=False)

        mock_client = MagicMock()
        mock_client.embeddings.create = AsyncMock(
            return_value=self._make_mock_response(3584)
        )

        with patch(
            "embeddings.foundry_local_embedder.openai.AsyncOpenAI",
            return_value=mock_client,
        ):
            from importlib import reload
            import embeddings.foundry_local_embedder as mod

            reload(mod)

            embedder = mod.AzureFoundryLocalEmbedder()
            result = await embedder.generate_embedding("test text")

        assert isinstance(result, list)
        assert len(result) == 3584
        assert all(isinstance(v, float) for v in result)

    async def test_raises_on_wrong_dimensions(self, monkeypatch):
        """generate_embedding raises ValueError when API returns wrong number of dims."""
        monkeypatch.delenv("AZURE_FOUNDRY_LOCAL_ENDPOINT", raising=False)

        mock_client = MagicMock()
        mock_client.embeddings.create = AsyncMock(
            return_value=self._make_mock_response(512)  # wrong dims
        )

        with patch(
            "embeddings.foundry_local_embedder.openai.AsyncOpenAI",
            return_value=mock_client,
        ):
            from importlib import reload
            import embeddings.foundry_local_embedder as mod

            reload(mod)

            embedder = mod.AzureFoundryLocalEmbedder()

            with pytest.raises(ValueError, match="Expected 3584 dims, got 512"):
                await embedder.generate_embedding("test text")

    async def test_retries_on_transient_error(self, monkeypatch):
        """generate_embedding retries up to 3 times; succeeds on 3rd attempt."""
        monkeypatch.delenv("AZURE_FOUNDRY_LOCAL_ENDPOINT", raising=False)

        good_response = self._make_mock_response(3584)
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
            "embeddings.foundry_local_embedder.openai.AsyncOpenAI",
            return_value=mock_client,
        ):
            with patch(
                "embeddings.foundry_local_embedder.asyncio.sleep",
                new_callable=AsyncMock,
            ):
                from importlib import reload
                import embeddings.foundry_local_embedder as mod

                reload(mod)

                embedder = mod.AzureFoundryLocalEmbedder()
                result = await embedder.generate_embedding("test text")

        assert len(result) == 3584
        assert call_count == 3


class TestAzureFoundryLocalEmbedderBatch:
    async def test_batch_returns_multiple_embeddings(self, monkeypatch):
        """generate_embeddings_batch(["a","b"]) returns a list of 2 embeddings."""
        monkeypatch.delenv("AZURE_FOUNDRY_LOCAL_ENDPOINT", raising=False)

        def make_response():
            embedding_obj = MagicMock()
            embedding_obj.embedding = [0.1] * 3584
            response = MagicMock()
            response.data = [embedding_obj]
            return response

        mock_client = MagicMock()
        mock_client.embeddings.create = AsyncMock(
            side_effect=[make_response(), make_response()]
        )

        with patch(
            "embeddings.foundry_local_embedder.openai.AsyncOpenAI",
            return_value=mock_client,
        ):
            from importlib import reload
            import embeddings.foundry_local_embedder as mod

            reload(mod)

            embedder = mod.AzureFoundryLocalEmbedder()
            results = await embedder.generate_embeddings_batch(["a", "b"])

        assert isinstance(results, list)
        assert len(results) == 2
        assert all(len(emb) == 3584 for emb in results)
