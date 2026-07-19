"""Unit tests for embedder_factory.py and LazyEmbedder deferred init."""

from unittest.mock import AsyncMock, patch

import pytest

from embeddings.embedder import Embedder
from embeddings.embedder_factory import get_embedder, reset_embedder
from embeddings.lazy_embedder import LazyEmbedder
from embeddings.model_discovery import ModelDiscoveryResult
from embeddings.ollama_embedder import OllamaEmbedder
from embeddings.openai_embedder import OpenAIEmbedder
from embeddings.truncating_embedder import TruncatingEmbedder


@pytest.fixture(autouse=True)
def clear_singleton(monkeypatch):
    """Reset the singleton and clear provider endpoint before/after every test."""
    reset_embedder()
    monkeypatch.delenv("EMBEDDING_PROVIDER_ENDPOINT", raising=False)
    yield
    reset_embedder()


def _health_endpoint_and_model(embedder: Embedder) -> tuple[object, object]:
    """Mirror ``_build_health_response`` getattr / unwrap pattern."""
    inner = getattr(embedder, "_embedder", embedder)
    embedding_endpoint = (
        getattr(inner, "_endpoint", None)
        or getattr(inner, "_host", None)
        or getattr(inner, "_base_url", None)
    )
    embedding_model = getattr(inner, "_model", None)
    return embedding_endpoint, embedding_model


def _resolve(lazy: LazyEmbedder) -> Embedder:
    resolved = lazy._try_resolve()
    assert resolved is not None
    return resolved


class TestGetEmbedderBackendSelection:
    def test_default_backend_wraps_ollama(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)

        lazy = get_embedder()
        assert isinstance(lazy, LazyEmbedder)

        with patch("embeddings.ollama_embedder.ollama.AsyncClient"):
            resolved = _resolve(lazy)

        assert isinstance(resolved, TruncatingEmbedder)
        assert isinstance(resolved._embedder, OllamaEmbedder)

    def test_openai_backend_wraps_openai(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_BACKEND", "openai")

        lazy = get_embedder()
        assert isinstance(lazy, LazyEmbedder)

        with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
            resolved = _resolve(lazy)

        assert isinstance(resolved, TruncatingEmbedder)
        assert isinstance(resolved._embedder, OpenAIEmbedder)

    def test_unknown_backend_defers_error_until_resolve(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_BACKEND", "bogus")

        lazy = get_embedder()
        assert isinstance(lazy, LazyEmbedder)
        assert lazy._resolved is None

        with pytest.raises(RuntimeError, match="failed to initialise"):
            lazy._require_resolved()

    def test_provider_endpoint_requires_explicit_model_when_multiple_models_are_discovered(
        self, monkeypatch
    ):
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://127.0.0.1:1234")
        monkeypatch.delenv("EMBEDDING_MODEL", raising=False)

        lazy = get_embedder()
        assert isinstance(lazy, LazyEmbedder)

        with patch(
            "embeddings.embedder_factory.discover_embedding_models_sync",
            return_value=ModelDiscoveryResult(
                provider="openai-compatible",
                endpoint="http://127.0.0.1:1234/v1",
                models=["text-embedding-qwen", "gpt-4o-mini"],
            ),
        ):
            with pytest.raises(RuntimeError, match="Set EMBEDDING_MODEL explicitly"):
                lazy._require_resolved()

    def test_provider_endpoint_discovers_openai_compatible(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://127.0.0.1:1234")

        lazy = get_embedder()
        assert isinstance(lazy, LazyEmbedder)

        with patch(
            "embeddings.embedder_factory.discover_embedding_models_sync",
            return_value=ModelDiscoveryResult(
                provider="openai-compatible",
                endpoint="http://127.0.0.1:1234/v1",
                models=["qwen3-embedding"],
            ),
        ):
            with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
                resolved = _resolve(lazy)

        assert isinstance(resolved, TruncatingEmbedder)
        assert isinstance(resolved._embedder, OpenAIEmbedder)

    def test_provider_endpoint_discovers_ollama(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://127.0.0.1:11434")

        lazy = get_embedder()
        assert isinstance(lazy, LazyEmbedder)

        with patch(
            "embeddings.embedder_factory.discover_embedding_models_sync",
            return_value=ModelDiscoveryResult(
                provider="ollama",
                endpoint="http://127.0.0.1:11434",
                models=["qwen3-embedding"],
            ),
        ):
            with patch("embeddings.ollama_embedder.ollama.AsyncClient"):
                resolved = _resolve(lazy)

        assert isinstance(resolved, TruncatingEmbedder)
        assert isinstance(resolved._embedder, OllamaEmbedder)


class TestGetEmbedderTruncation:
    def test_default_target_dims_is_1536(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)
        monkeypatch.delenv("OLLAMA_EMBEDDING_DIMS", raising=False)

        lazy = get_embedder()
        with patch("embeddings.ollama_embedder.ollama.AsyncClient"):
            resolved = _resolve(lazy)

        assert isinstance(resolved, TruncatingEmbedder)
        assert resolved._target_dims == 1536

    def test_ollama_reads_ollama_embedding_dims(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)
        monkeypatch.setenv("OLLAMA_EMBEDDING_DIMS", "768")

        lazy = get_embedder()
        with patch("embeddings.ollama_embedder.ollama.AsyncClient"):
            resolved = _resolve(lazy)

        assert isinstance(resolved, TruncatingEmbedder)
        assert resolved._target_dims == 768

    def test_openai_reads_embedding_dims(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_BACKEND", "openai")
        monkeypatch.setenv("EMBEDDING_DIMS", "1024")

        lazy = get_embedder()
        with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
            resolved = _resolve(lazy)

        assert isinstance(resolved, TruncatingEmbedder)
        assert resolved._target_dims == 1024


class TestGetEmbedderSingleton:
    def test_singleton_returns_same_instance(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)

        first = get_embedder()
        second = get_embedder()

        assert isinstance(first, LazyEmbedder)
        assert first is second

    def test_reset_clears_singleton(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)

        first = get_embedder()
        reset_embedder()
        second = get_embedder()

        assert first is not second
        assert isinstance(second, LazyEmbedder)

    def test_get_embedder_returns_embedder_type(self, monkeypatch):
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)

        result = get_embedder()

        assert isinstance(result, Embedder)
        assert isinstance(result, LazyEmbedder)


class TestLazyEmbedderDeferredInit:
    """T1 acceptance: discovery must not run at get_embedder() / construction."""

    def test_get_embedder_does_not_call_discovery_when_endpoint_set(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://127.0.0.1:1234")
        monkeypatch.setenv("EMBEDDING_MODEL", "qwen3-embedding")

        with patch(
            "embeddings.embedder_factory.discover_embedding_models_sync",
            side_effect=RuntimeError("discovery must not run at construction"),
        ) as discover:
            lazy = get_embedder()

        assert isinstance(lazy, LazyEmbedder)
        assert lazy._resolved is None
        discover.assert_not_called()

    @pytest.mark.asyncio
    async def test_discovery_failure_check_status_disconnected_and_embed_raises(
        self, monkeypatch
    ):
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://127.0.0.1:1234")
        monkeypatch.setenv("EMBEDDING_MODEL", "qwen3-embedding")

        with patch(
            "embeddings.embedder_factory.discover_embedding_models_sync",
            side_effect=ConnectionError("provider unreachable"),
        ) as discover:
            lazy = get_embedder()
            assert isinstance(lazy, LazyEmbedder)
            discover.assert_not_called()

            status = await lazy.check_status()
            assert status == "disconnected"
            discover.assert_called_once()

            with pytest.raises(RuntimeError, match="failed to initialise"):
                await lazy.generate_embedding("hello")

            with pytest.raises(RuntimeError, match="failed to initialise"):
                await lazy.generate_embeddings_batch(["a", "b"])

    @pytest.mark.asyncio
    async def test_happy_path_resolves_to_truncating_openai_wrapper(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://127.0.0.1:1234")
        monkeypatch.setenv("EMBEDDING_MODEL", "qwen3-embedding")

        with patch(
            "embeddings.embedder_factory.discover_embedding_models_sync",
            return_value=ModelDiscoveryResult(
                provider="openai-compatible",
                endpoint="http://127.0.0.1:1234/v1",
                models=["qwen3-embedding"],
            ),
        ):
            with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
                with patch.object(
                    TruncatingEmbedder,
                    "check_status",
                    new_callable=AsyncMock,
                    return_value="connected",
                ):
                    lazy = get_embedder()
                    assert lazy._resolved is None

                    status = await lazy.check_status()
                    assert status == "connected"

        assert isinstance(lazy._resolved, TruncatingEmbedder)
        assert isinstance(lazy._resolved._embedder, OpenAIEmbedder)

    def test_health_attrs_visible_before_resolve(self, monkeypatch):
        endpoint = "http://127.0.0.1:1234"
        model = "qwen3-embedding"
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", endpoint)
        monkeypatch.setenv("EMBEDDING_MODEL", model)

        with patch(
            "embeddings.embedder_factory.discover_embedding_models_sync",
            side_effect=AssertionError("must not discover for health getattr"),
        ):
            lazy = get_embedder()

        assert lazy._resolved is None
        # Property returns self before resolve so configured attrs stay visible.
        assert lazy._embedder is lazy

        reported_endpoint, reported_model = _health_endpoint_and_model(lazy)
        assert reported_endpoint == endpoint
        assert reported_model == model

    def test_health_attrs_unwrap_to_concrete_after_resolve(self, monkeypatch):
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://127.0.0.1:1234")
        monkeypatch.setenv("EMBEDDING_MODEL", "configured-model")

        lazy = get_embedder()
        with patch(
            "embeddings.embedder_factory.discover_embedding_models_sync",
            return_value=ModelDiscoveryResult(
                provider="openai-compatible",
                endpoint="http://127.0.0.1:1234/v1",
                models=["qwen3-embedding"],
            ),
        ):
            with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
                resolved = _resolve(lazy)

        assert isinstance(resolved, TruncatingEmbedder)
        # After resolve, LazyEmbedder._embedder unwraps past TruncatingEmbedder.
        assert isinstance(lazy._embedder, OpenAIEmbedder)
        assert lazy._embedder is resolved._embedder

        reported_endpoint, reported_model = _health_endpoint_and_model(lazy)
        assert reported_endpoint == "http://127.0.0.1:1234/v1"
        assert reported_model == "configured-model"

    def test_ollama_host_visible_before_resolve_without_provider_endpoint(
        self, monkeypatch
    ):
        monkeypatch.delenv("EMBEDDING_BACKEND", raising=False)
        monkeypatch.setenv("OLLAMA_BASE_URL", "http://127.0.0.1:11434")
        monkeypatch.setenv("EMBEDDING_MODEL", "nomic-embed")

        lazy = get_embedder()
        assert lazy._resolved is None

        reported_endpoint, reported_model = _health_endpoint_and_model(lazy)
        assert reported_endpoint == "http://127.0.0.1:11434"
        assert reported_model == "nomic-embed"
