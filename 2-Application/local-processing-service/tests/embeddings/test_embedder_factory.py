"""Unit tests for embedder_factory.py."""

from unittest.mock import MagicMock, patch

import pytest

from embeddings.embedder_factory import get_embedder, reset_embedder
from embeddings.deepinfra_embedder import DeepInfraEmbedder
from embeddings.foundry_local_embedder import AzureFoundryLocalEmbedder
from embeddings.model_discovery import ModelDiscoveryResult
from embeddings.ollama_embedder import OllamaEmbedder


@pytest.fixture(autouse=True)
def clear_singleton():
    """Reset the singleton before and after every test."""
    reset_embedder()
    yield
    reset_embedder()


class TestGetEmbedderBackendSelection:
    def test_default_backend_is_ollama(self, monkeypatch):
        """With no EMBEDDING_BACKEND env var, get_embedder() returns an OllamaEmbedder."""
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)

        with patch("embeddings.ollama_embedder.ollama.AsyncClient"):
            result = get_embedder()

        assert isinstance(result, OllamaEmbedder)

    def test_foundry_local_backend(self, monkeypatch):
        """With EMBEDDING_BACKEND=foundry_local, returns AzureFoundryLocalEmbedder."""
        monkeypatch.setenv("EMBEDDING_BACKEND", "foundry_local")

        with patch("embeddings.foundry_local_embedder.openai.AsyncOpenAI"):
            result = get_embedder()

        assert isinstance(result, AzureFoundryLocalEmbedder)

    def test_deepinfra_backend(self, monkeypatch):
        """With EMBEDDING_BACKEND=deepinfra and DEEPINFRA_API_KEY set, returns DeepInfraEmbedder."""
        monkeypatch.setenv("EMBEDDING_BACKEND", "deepinfra")
        monkeypatch.setenv("DEEPINFRA_API_KEY", "test-key")

        with patch("embeddings.deepinfra_embedder.openai.AsyncOpenAI"):
            result = get_embedder()

        assert isinstance(result, DeepInfraEmbedder)

    def test_unknown_backend_raises(self, monkeypatch):
        """With EMBEDDING_BACKEND=bogus, a ValueError is raised."""
        monkeypatch.setenv("EMBEDDING_BACKEND", "bogus")

        with pytest.raises(ValueError, match="Unknown EMBEDDING_BACKEND"):
            get_embedder()

    def test_provider_endpoint_requires_explicit_model_when_multiple_models_are_discovered(self, monkeypatch):
        """A provider endpoint with multiple models requires EMBEDDING_MODEL to avoid arbitrary selection."""
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://127.0.0.1:1234")
        monkeypatch.delenv("EMBEDDING_MODEL", raising=False)

        with patch(
            "embeddings.embedder_factory.discover_embedding_models_sync",
            return_value=ModelDiscoveryResult(
                provider="openai-compatible",
                endpoint="http://127.0.0.1:1234/v1",
                models=["text-embedding-qwen", "gpt-4o-mini"],
            ),
        ):
            with pytest.raises(ValueError, match="Set EMBEDDING_MODEL explicitly"):
                get_embedder()


class TestGetEmbedderSingleton:
    def test_singleton_returns_same_instance(self, monkeypatch):
        """Calling get_embedder() twice returns the identical object."""
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)

        with patch("embeddings.ollama_embedder.ollama.AsyncClient"):
            first = get_embedder()
            second = get_embedder()

        assert first is second

    def test_reset_clears_singleton(self, monkeypatch):
        """After reset_embedder(), the next call creates a fresh instance."""
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)

        with patch("embeddings.ollama_embedder.ollama.AsyncClient"):
            first = get_embedder()
            reset_embedder()
            second = get_embedder()

        assert first is not second
