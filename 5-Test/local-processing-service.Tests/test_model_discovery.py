"""Unit tests for embedding provider discovery."""

from unittest.mock import patch

import httpx
import pytest

from embeddings.model_discovery import (
    ModelDiscoveryError,
    _normalize_endpoint,
    _parse_ollama_payload,
    _parse_openai_payload,
    discover_embedding_models,
    discover_embedding_models_sync,
)
from security.safe_http import EndpointPolicy, RedirectBlockedError


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
    def __init__(self, responses, **kwargs):
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
    def __init__(self, responses, **kwargs):
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

    assert _parse_ollama_payload({"models": [{"name": "qwen"}, {"model": "qwen"}]}) == [
        "qwen"
    ]


@pytest.mark.parametrize(
    "endpoint",
    [
        "http://models.example.test:5272",
        "http://127.0.0.1.example.test:5272",
        "http://10.1.2.3:5272",
        "http://172.16.0.1:5272",
        "http://192.168.1.10:5272",
        "http://169.254.169.254:5272",
        "http://[fe80::1]:5272",
        "https://user:password@models.example.test",
        "https://models.example.test/#fragment",
        "https:///missing-authority",
        "https://:443",
        "https://models.example.test:invalid",
    ],
    ids=[
        "remote-http",
        "loopback-lookalike",
        "private-10",
        "private-172",
        "private-192",
        "link-local-ipv4",
        "link-local-ipv6",
        "credentials",
        "fragment",
        "missing-authority",
        "empty-host",
        "invalid-port",
    ],
)
def test_normalize_endpoint_rejects_unsafe_or_malformed_authorities(endpoint):
    """T9: only literal loopback hosts may use HTTP model-discovery endpoints."""
    with pytest.raises(ValueError, match="(?i)(endpoint|url|http|host|authority)"):
        _normalize_endpoint(endpoint)


@pytest.mark.parametrize(
    "endpoint",
    [
        "http://localhost:5272",
        "http://127.0.0.1:5272",
        "http://127.255.255.255:5272",
        "http://[::1]:5272",
        "https://models.example.test:443",
    ],
    ids=[
        "localhost",
        "loopback-ipv4",
        "loopback-ipv4-upper",
        "loopback-ipv6",
        "remote-https",
    ],
)
def test_normalize_endpoint_allows_only_literal_loopback_http_or_remote_https(endpoint):
    assert _normalize_endpoint(endpoint) == endpoint


async def test_discover_embedding_models_prefers_openai_compatible_payload():
    responses = {
        "http://localhost:5272/models": _FakeResponse(
            {"data": [{"id": "qwen3-embedding"}, {"id": "gpt-4o-mini"}]}
        )
    }

    with patch(
        "embeddings.model_discovery.httpx.AsyncClient",
        side_effect=lambda **kwargs: _FakeAsyncClient(responses, **kwargs),
    ) as client_constructor:
        result = await discover_embedding_models("http://localhost:5272")

    assert result.provider == "openai-compatible"
    assert result.endpoint == "http://localhost:5272"
    assert result.models == ["qwen3-embedding", "gpt-4o-mini"]
    assert client_constructor.call_args.kwargs.get("verify", True) is True
    assert client_constructor.call_args.kwargs.get("follow_redirects") is False


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
        side_effect=lambda **kwargs: _FakeSyncClient(responses, **kwargs),
    ) as client_constructor:
        result = discover_embedding_models_sync("http://localhost:11434")

    assert result.provider == "ollama"
    assert result.endpoint == "http://localhost:11434"
    assert result.models == ["qwen3-embedding", "qwen3:4b"]
    assert client_constructor.call_args.kwargs.get("verify", True) is True
    assert client_constructor.call_args.kwargs.get("follow_redirects") is False


def test_discover_embedding_models_sync_accepts_pasted_openai_model_list_url():
    responses = {
        "http://localhost:1234/v1/models": _FakeResponse(
            {"data": [{"id": "text-embedding-qwen"}]}
        )
    }

    with patch(
        "embeddings.model_discovery.httpx.Client",
        side_effect=lambda **kwargs: _FakeSyncClient(responses, **kwargs),
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
        side_effect=lambda **kwargs: _FakeSyncClient(responses, **kwargs),
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
        side_effect=lambda **kwargs: _FakeAsyncClient(responses, **kwargs),
    ):
        with pytest.raises(ModelDiscoveryError):
            await discover_embedding_models("https://example.com")


@pytest.mark.parametrize(
    ("endpoint", "expected_policy"),
    [
        ("http://localhost:5272", EndpointPolicy.LOOPBACK_HTTP),
        ("https://models.example.test", EndpointPolicy.PUBLIC_HTTPS),
    ],
    ids=["local-http", "remote-https"],
)
async def test_discover_embedding_models_uses_policy_specific_safe_transport(
    endpoint, expected_policy
):
    """T9: cleartext is local-only; HTTPS discovery uses the public transport."""
    responses = {
        f"{endpoint}/models": _FakeResponse({"data": [{"id": "embedding-model"}]})
    }
    safe_client = _FakeAsyncClient(responses)

    with patch(
        "embeddings.model_discovery.create_model_discovery_async_client",
        return_value=safe_client,
    ) as create_client:
        result = await discover_embedding_models(endpoint)

    assert result.models == ["embedding-model"]
    assert create_client.call_args.args[0] is expected_policy


async def test_discover_embedding_models_when_safe_transport_blocks_redirect_does_not_try_second_connection():
    """T9: a redirect is terminal instead of a provider-probing fallback."""
    calls = []

    class RedirectBlockedClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def get(self, url, headers=None):
            calls.append(url)
            request = httpx.Request("GET", url)
            raise RedirectBlockedError("Redirects are not allowed", request=request)

    with patch(
        "embeddings.model_discovery.create_model_discovery_async_client",
        return_value=RedirectBlockedClient(),
    ):
        with pytest.raises(RedirectBlockedError):
            await discover_embedding_models("https://models.example.test")

    assert calls == ["https://models.example.test/v1/models"]
