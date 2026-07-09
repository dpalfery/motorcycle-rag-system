"""Unit tests for TruncatingEmbedder — no network, pure slicing logic."""

import logging
from unittest.mock import AsyncMock, MagicMock, patch

from embeddings.embedder import Embedder
from embeddings.truncating_embedder import TruncatingEmbedder


class _FakeEmbedder(Embedder):
    """Deterministic stand-in embedder for unit tests."""

    def __init__(self, vector: list[float], status: str = "connected") -> None:
        self._vector = vector
        self._status = status
        self.calls: list[str] = []

    async def generate_embedding(self, text: str) -> list[float]:
        self.calls.append(text)
        # Return a fresh copy so slicing is observable.
        return list(self._vector)

    async def generate_embeddings_batch(self, texts: list[str]) -> list[list[float]]:
        for text in texts:
            self.calls.append(text)
        return [list(self._vector) for _ in texts]

    async def check_status(self) -> str:
        return self._status


def _make_vector(length: int) -> list[float]:
    return [float(i) for i in range(length)]


class TestTruncatingEmbedderGenerateEmbedding:
    async def test_truncates_to_target_dims(self):
        fake = _FakeEmbedder(_make_vector(2560))
        embedder = TruncatingEmbedder(fake, target_dims=1536)

        result = await embedder.generate_embedding("hello")

        assert len(result) == 1536
        assert result == _make_vector(1536)

    async def test_preserves_leading_elements(self):
        full = _make_vector(2560)
        fake = _FakeEmbedder(full)
        embedder = TruncatingEmbedder(fake, target_dims=1536)

        result = await embedder.generate_embedding("hello")

        # First-N slicing: the prefix must match the original prefix.
        assert result == full[:1536]

    async def test_no_op_when_vector_already_short_enough(self):
        fake = _FakeEmbedder(_make_vector(1536))
        embedder = TruncatingEmbedder(fake, target_dims=1536)

        result = await embedder.generate_embedding("hello")

        assert len(result) == 1536

    async def test_no_op_when_vector_shorter_than_target(self):
        fake = _FakeEmbedder(_make_vector(768))
        embedder = TruncatingEmbedder(fake, target_dims=1536)

        result = await embedder.generate_embedding("hello")

        assert len(result) == 768
        assert result == _make_vector(768)

    async def test_delegates_text_to_wrapped_embedder(self):
        fake = _FakeEmbedder(_make_vector(1536))
        embedder = TruncatingEmbedder(fake, target_dims=1536)

        await embedder.generate_embedding("some text")

        assert fake.calls == ["some text"]


class TestTruncatingEmbedderBatch:
    async def test_truncates_every_vector_in_batch(self):
        fake = _FakeEmbedder(_make_vector(2560))
        embedder = TruncatingEmbedder(fake, target_dims=1536)

        results = await embedder.generate_embeddings_batch(["a", "b", "c"])

        assert len(results) == 3
        assert all(len(vec) == 1536 for vec in results)
        assert all(vec == _make_vector(1536) for vec in results)

    async def test_empty_batch_returns_empty_list(self):
        fake = _FakeEmbedder(_make_vector(2560))
        embedder = TruncatingEmbedder(fake, target_dims=1536)

        results = await embedder.generate_embeddings_batch([])

        assert results == []

    async def test_batch_preserves_leading_elements(self):
        full = _make_vector(2560)
        fake = _FakeEmbedder(full)
        embedder = TruncatingEmbedder(fake, target_dims=1024)

        results = await embedder.generate_embeddings_batch(["a", "b"])

        assert all(vec == full[:1024] for vec in results)


class TestTruncatingEmbedderStatus:
    async def test_check_status_delegates_to_wrapped_embedder(self):
        fake = _FakeEmbedder(_make_vector(1536), status="disconnected")
        embedder = TruncatingEmbedder(fake, target_dims=1536)

        status = await embedder.check_status()

        assert status == "disconnected"


class TestTruncatingEmbedderDefaults:
    def test_default_target_dims_is_1536(self):
        embedder = TruncatingEmbedder(_FakeEmbedder(_make_vector(1536)))

        assert embedder._target_dims == 1536

    def test_custom_target_dims(self):
        embedder = TruncatingEmbedder(
            _FakeEmbedder(_make_vector(2560)), target_dims=768
        )

        assert embedder._target_dims == 768

    def test_is_an_embedder(self):
        embedder = TruncatingEmbedder(_FakeEmbedder(_make_vector(1536)))

        assert isinstance(embedder, Embedder)


class TestTruncatingEmbedderWithOpenAIEmbedder:
    """Integration tests proving the OpenAIEmbedder -> TruncatingEmbedder chain.

    The unit tests above only exercise the slicing logic with a fake embedder
    that always cooperates.  These tests prove the *production* flow that LM
    Studio triggers: the API ignores the requested dimensionality and returns
    a full-length vector.  Before the OpenAIEmbedder fix this chain raised
    ``ValueError`` before ``TruncatingEmbedder`` could ever act; these tests
    guard against that regression.
    """

    @staticmethod
    def _make_mock_response(dims: int):
        """Build a mock OpenAI embeddings response of *dims* dimensions."""
        embedding_obj = MagicMock()
        embedding_obj.embedding = [float(i % 100) / 100 for i in range(dims)]

        response = MagicMock()
        response.data = [embedding_obj]
        return response

    async def test_oversized_vector_is_truncated_end_to_end(
        self, monkeypatch, caplog
    ):
        # Simulate LM Studio returning a full 2560-dim Qwen3 vector even
        # though the index expects 1536.
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

            base = mod.OpenAIEmbedder()
            embedder = TruncatingEmbedder(base, target_dims=1536)

            with caplog.at_level(
                logging.WARNING, logger="embeddings.openai_embedder"
            ):
                result = await embedder.generate_embedding("motorcycle specs")

        # The chain must NOT raise -- OpenAIEmbedder returns the oversized
        # vector and TruncatingEmbedder slices it to the target dims.
        assert len(result) == 1536

        # First-N slice: the prefix must match the simulated server output.
        expected_prefix = [float(i % 100) / 100 for i in range(1536)]
        assert result == expected_prefix

        # OpenAIEmbedder must have warned that truncation is needed.
        warning_text = " ".join(r.message for r in caplog.records)
        assert "2560" in warning_text and "1536" in warning_text

    async def test_batch_oversized_vectors_are_truncated(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_PROVIDER_ENDPOINT", raising=False)
        monkeypatch.setenv("EMBEDDING_DIMS", "1536")

        mock_client = MagicMock()
        mock_client.embeddings.create = AsyncMock(
            side_effect=[
                self._make_mock_response(2560),
                self._make_mock_response(2560),
            ]
        )

        with patch(
            "embeddings.openai_embedder.openai.AsyncOpenAI",
            return_value=mock_client,
        ):
            from importlib import reload
            import embeddings.openai_embedder as mod

            reload(mod)

            base = mod.OpenAIEmbedder()
            embedder = TruncatingEmbedder(base, target_dims=1536)

            results = await embedder.generate_embeddings_batch(["a", "b"])

        assert len(results) == 2
        assert all(len(vec) == 1536 for vec in results)
