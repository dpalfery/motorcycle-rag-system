"""Unit tests for embedder_factory.py."""

from unittest.mock import MagicMock, patch

import pytest

from embeddings.embedder import Embedder
from embeddings.embedder_factory import get_embedder, reset_embedder
from embeddings.model_discovery import ModelDiscoveryResult
from embeddings.ollama_embedder import OllamaEmbedder
from embeddings.openai_embedder import OpenAIEmbedder


@pytest.fixture(autouse=True)
def clear_singleton():
    """Reset the singleton before and after every test."""
    reset_embedder()
    yield
    reset_embedder()


class TestGetEmbedderBackendSelection:
    def test_default_backend_is_ollama(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)

        with patch("embeddings.ollama_embedder.ollama.AsyncClient"):
            result = get_embedder()

        assert isinstance(result, OllamaEmbedder)

    def test_openai_backend(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_BACKEND", "openai")

        with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
            result = get_embedder()

        assert isinstance(result, OpenAIEmbedder)

    def test_unknown_backend_raises(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_BACKEND", "bogus")

        with pytest.raises(ValueError, match="Unknown EMBEDDING_BACKEND"):
            get_embedder()

    def test_provider_endpoint_requires_explicit_model_when_multiple_models_are_discovered(self, monkeypatch):
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

    def test_provider_endpoint_discovers_openai_compatible(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://127.0.0.1:1234")

        with patch(
            "embeddings.embedder_factory.discover_embedding_models_sync",
            return_value=ModelDiscoveryResult(
                provider="openai-compatible",
                endpoint="http://127.0.0.1:1234/v1",
                models=["qwen3-embedding"],
            ),
        ):
            with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
                result = get_embedder()

        assert isinstance(result, OpenAIEmbedder)

    def test_provider_endpoint_discovers_ollama(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://127.0.0.1:11434")

        with patch(
            "embeddings.embedder_factory.discover_embedding_models_sync",
            return_value=ModelDiscoveryResult(
                provider="ollama",
                endpoint="http://127.0.0.1:11434",
                models=["qwen3-embedding"],
            ),
        ):
            with patch("embeddings.ollama_embedder.ollama.AsyncClient"):
                result = get_embedder()

        assert isinstance(result, OllamaEmbedder)


class TestGetEmbedderSingleton:
    def test_singleton_returns_same_instance(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)

        with patch("embeddings.ollama_embedder.ollama.AsyncClient"):
            first = get_embedder()
            second = get_embedder()

        assert first is second

    def test_reset_clears_singleton(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)

        with patch("embeddings.ollama_embedder.ollama.AsyncClient"):
            first = get_embedder()
            reset_embedder()
            second = get_embedder()

        assert first is not second

    def test_get_embedder_returns_embedder_type(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)

        with patch("embeddings.ollama_embedder.ollama.AsyncClient"):
            result = get_embedder()

        assert isinstance(result, Embedder)
