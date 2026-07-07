"""Unit tests for DeepInfraEmbedder."""

from unittest.mock import AsyncMock, MagicMock, patch

import pytest


class TestDeepInfraEmbedderInstantiation:
    def test_raises_if_api_key_missing(self, monkeypatch):
        """Instantiation without DEEPINFRA_API_KEY raises ValueError."""
        monkeypatch.delenv("DEEPINFRA_API_KEY", raising=False)

        from embeddings.deepinfra_embedder import DeepInfraEmbedder

        with pytest.raises(
            ValueError, match="DEEPINFRA_API_KEY environment variable is required"
        ):
            DeepInfraEmbedder()

    def test_instantiates_with_api_key(self, monkeypatch):
        """Instantiation with DEEPINFRA_API_KEY set succeeds and dims is None by default."""
        monkeypatch.setenv("DEEPINFRA_API_KEY", "test-key")

        with patch("embeddings.deepinfra_embedder.openai.AsyncOpenAI"):
            from importlib import reload
            import embeddings.deepinfra_embedder as mod

            reload(mod)

            embedder = mod.DeepInfraEmbedder()

        assert embedder._dims is None


class TestDeepInfraEmbedderGenerateEmbedding:
    def _make_mock_response(self, dims: int = 3584):
        """Build a mock openai embeddings response with *dims* floats."""
        embedding_obj = MagicMock()
        embedding_obj.embedding = [0.1] * dims

        response = MagicMock()
        response.data = [embedding_obj]
        return response

    async def test_returns_3584_floats(self, monkeypatch):
        """generate_embedding returns a list of 3584 floats when the API responds correctly."""
        monkeypatch.setenv("DEEPINFRA_API_KEY", "test-key")

        mock_client = MagicMock()
        mock_client.embeddings.create = AsyncMock(
            return_value=self._make_mock_response(3584)
        )

        with patch(
            "embeddings.deepinfra_embedder.openai.AsyncOpenAI", return_value=mock_client
        ):
            from importlib import reload
            import embeddings.deepinfra_embedder as mod

            reload(mod)

            embedder = mod.DeepInfraEmbedder()
            result = await embedder.generate_embedding("test text")

        assert isinstance(result, list)
        assert len(result) == 3584
        assert all(isinstance(v, float) for v in result)

    async def test_raises_on_wrong_dimensions(self, monkeypatch):
        """generate_embedding raises ValueError when API returns wrong number of dims."""
        monkeypatch.setenv("DEEPINFRA_API_KEY", "test-key")
        monkeypatch.setenv("DEEPINFRA_EMBEDDING_DIMS", "3584")

        mock_client = MagicMock()
        mock_client.embeddings.create = AsyncMock(
            return_value=self._make_mock_response(512)  # wrong dims
        )

        with patch(
            "embeddings.deepinfra_embedder.openai.AsyncOpenAI", return_value=mock_client
        ):
            from importlib import reload
            import embeddings.deepinfra_embedder as mod

            reload(mod)

            embedder = mod.DeepInfraEmbedder()

            with pytest.raises(ValueError, match="Expected 3584 dims, got 512"):
                await embedder.generate_embedding("test text")

    async def test_retries_on_transient_error(self, monkeypatch):
        """generate_embedding retries up to 3 times; succeeds on 3rd attempt."""
        monkeypatch.setenv("DEEPINFRA_API_KEY", "test-key")

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
            "embeddings.deepinfra_embedder.openai.AsyncOpenAI", return_value=mock_client
        ):
            with patch(
                "embeddings.deepinfra_embedder.asyncio.sleep", new_callable=AsyncMock
            ):
                from importlib import reload
                import embeddings.deepinfra_embedder as mod

                reload(mod)

                embedder = mod.DeepInfraEmbedder()
                result = await embedder.generate_embedding("test text")

        assert len(result) == 3584
        assert call_count == 3


class TestDeepInfraEmbedderBatch:
    async def test_batch_returns_multiple_embeddings(self, monkeypatch):
        """generate_embeddings_batch(["a","b"]) returns a list of 2 embeddings."""
        monkeypatch.setenv("DEEPINFRA_API_KEY", "test-key")

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
            "embeddings.deepinfra_embedder.openai.AsyncOpenAI", return_value=mock_client
        ):
            from importlib import reload
            import embeddings.deepinfra_embedder as mod

            reload(mod)

            embedder = mod.DeepInfraEmbedder()
            results = await embedder.generate_embeddings_batch(["a", "b"])

        assert isinstance(results, list)
        assert len(results) == 2
        assert all(len(emb) == 3584 for emb in results)
