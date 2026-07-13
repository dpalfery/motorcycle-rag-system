"""Unit tests for embedding provider discovery."""

from unittest.mock import patch

import pytest

from embeddings.model_discovery import (
    ModelDiscoveryError,
    _normalize_endpoint,
    _parse_ollama_payload,
    _parse_openai_payload,
    discover_embedding_models,
    discover_embedding_models_sync,
)


class _FakeResponse:
    def __init__(self, payload, status_code=200):
        self._payload = payload
        self.status_code = status_code

    def raise_for_status(self):
        if self.status_code >= 400:
            raise RuntimeError(f"HTTP {self.status_code}")

    def json(self):
        return self._payload


class _FakeAsyncClient:
    def __init__(self, responses, timeout=None):
        self._responses = responses

    async def __aenter__(self):
        return self

    async def __aexit__(self, exc_type, exc, tb):
        return False

    async def get(self, url, headers=None):
        response = self._responses.get(url)
        if isinstance(response, Exception):
            raise response
        if response is None:
            raise RuntimeError(f"Unexpected URL {url}")
        return response


class _FakeSyncClient:
    def __init__(self, responses, timeout=None):
        self._responses = responses

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb):
        return False

    def get(self, url, headers=None):
        response = self._responses.get(url)
        if isinstance(response, Exception):
            raise response
        if response is None:
            raise RuntimeError(f"Unexpected URL {url}")
        return response


def test_model_payload_parsers_reject_malformed_payloads_and_remove_duplicates():
    with pytest.raises(ValueError, match="endpoint is required"):
        _normalize_endpoint(" / ")
    with pytest.raises(ValueError, match="JSON object"):
        _parse_openai_payload([])
    with pytest.raises(ValueError, match="data array"):
        _parse_openai_payload({})
    with pytest.raises(ValueError, match="models array"):
        _parse_ollama_payload({})

    assert _parse_ollama_payload({"models": [{"name": "qwen"}, {"model": "qwen"}]}) == ["qwen"]


async def test_discover_embedding_models_prefers_openai_compatible_payload():
    responses = {
        "http://localhost:5272/models": _FakeResponse(
            {"data": [{"id": "qwen3-embedding"}, {"id": "gpt-4o-mini"}]}
        )
    }

    with patch(
        "embeddings.model_discovery.httpx.AsyncClient",
        side_effect=lambda timeout=None: _FakeAsyncClient(responses, timeout=timeout),
    ):
        result = await discover_embedding_models("http://localhost:5272")

    assert result.provider == "openai-compatible"
    assert result.endpoint == "http://localhost:5272"
    assert result.models == ["qwen3-embedding", "gpt-4o-mini"]


def test_discover_embedding_models_sync_falls_back_to_ollama():
    responses = {
        "http://localhost:11434/models": RuntimeError("not openai-compatible"),
        "http://localhost:11434/v1/models": RuntimeError("not openai-compatible"),
        "http://localhost:11434/api/tags": _FakeResponse(
            {"models": [{"model": "qwen3-embedding"}, {"model": "qwen3:4b"}]}
        ),
    }

    with patch(
        "embeddings.model_discovery.httpx.Client",
        side_effect=lambda timeout=None: _FakeSyncClient(responses, timeout=timeout),
    ):
        result = discover_embedding_models_sync("http://localhost:11434")

    assert result.provider == "ollama"
    assert result.endpoint == "http://localhost:11434"
    assert result.models == ["qwen3-embedding", "qwen3:4b"]


def test_discover_embedding_models_sync_accepts_pasted_openai_model_list_url():
    responses = {
        "http://localhost:1234/v1/models": _FakeResponse(
            {"data": [{"id": "text-embedding-qwen"}]}
        )
    }

    with patch(
        "embeddings.model_discovery.httpx.Client",
        side_effect=lambda timeout=None: _FakeSyncClient(responses, timeout=timeout),
    ):
        result = discover_embedding_models_sync("http://localhost:1234/v1/models")

    assert result.provider == "openai-compatible"
    assert result.endpoint == "http://localhost:1234/v1"
    assert result.models == ["text-embedding-qwen"]


def test_discover_embedding_models_sync_accepts_pasted_ollama_tags_url():
    responses = {
        "http://localhost:11434/models": RuntimeError("not openai-compatible"),
        "http://localhost:11434/v1/models": RuntimeError("not openai-compatible"),
        "http://localhost:11434/api/tags": _FakeResponse(
            {"models": [{"model": "qwen3-embedding"}]}
        ),
    }

    with patch(
        "embeddings.model_discovery.httpx.Client",
        side_effect=lambda timeout=None: _FakeSyncClient(responses, timeout=timeout),
    ):
        result = discover_embedding_models_sync("http://localhost:11434/api/tags")

    assert result.provider == "ollama"
    assert result.endpoint == "http://localhost:11434"
    assert result.models == ["qwen3-embedding"]


async def test_discover_embedding_models_raises_when_provider_is_unknown():
    responses = {
        "https://example.com/models": RuntimeError("not found"),
        "https://example.com/v1/models": RuntimeError("not found"),
        "https://example.com/api/tags": RuntimeError("not found"),
    }

    with patch(
        "embeddings.model_discovery.httpx.AsyncClient",
        side_effect=lambda timeout=None: _FakeAsyncClient(responses, timeout=timeout),
    ):
        with pytest.raises(ModelDiscoveryError):
            await discover_embedding_models("https://example.com")
