"""Unit tests for embedding provider discovery."""

from unittest.mock import patch

import httpx
import pytest

from embeddings.model_discovery import (
    ModelDiscoveryError,
    _async_get_json,
    _normalize_endpoint,
    _ollama_candidate_urls,
    _openai_candidate_urls,
    _parse_ollama_payload,
    _parse_openai_payload,
    _sync_get_json,
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


def _response_key(url: object) -> str:
    """Normalize typed httpx.URL and string keys for fake response lookup."""
    return str(url)


class _FakeAsyncClient:
    def __init__(self, responses, **kwargs):
        self._responses = {_response_key(key): value for key, value in responses.items()}

    async def __aenter__(self):
        return self

    async def __aexit__(self, exc_type, exc, tb):
        return False

    async def get(self, url, headers=None):
        response = self._responses.get(_response_key(url))
        if isinstance(response, Exception):
            raise response
        if response is None:
            raise RuntimeError(f"Unexpected URL {url}")
        return response


class _FakeSyncClient:
    def __init__(self, responses, **kwargs):
        self._responses = {_response_key(key): value for key, value in responses.items()}

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb):
        return False

    def get(self, url, headers=None):
        response = self._responses.get(_response_key(url))
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

    assert len(calls) == 1
    assert isinstance(calls[0], httpx.URL)
    assert str(calls[0]) == "https://models.example.test/v1/models"


@pytest.mark.parametrize(
    ("endpoint", "expected_scheme", "expected_host", "expected_port"),
    [
        ("http://localhost:5272", "http", "localhost", 5272),
        ("http://127.0.0.1:11434", "http", "127.0.0.1", 11434),
        ("http://[::1]:11434", "http", "::1", 11434),
        ("https://models.example.test", "https", "models.example.test", None),
    ],
    ids=["localhost-http", "loopback-ipv4-http", "loopback-ipv6-http", "public-https"],
)
def test_openai_candidate_urls_are_typed_urls_preserving_validated_authority(
    endpoint, expected_scheme, expected_host, expected_port
):
    """T3/T4: discovery candidates are absolute httpx.URL values under safe_http policy.

    Public HTTPS and literal-loopback HTTP remain allowed (D4). Fails until candidate
    construction returns typed URLs built from the validated endpoint authority.
    """
    candidates = _openai_candidate_urls(endpoint)

    assert candidates
    assert all(isinstance(url, httpx.URL) for url in candidates)
    for url in candidates:
        assert url.scheme == expected_scheme
        assert url.host == expected_host
        assert url.port == expected_port
        assert url.username is None or url.username == ""
        assert url.password is None or url.password == ""
        assert url.fragment is None or url.fragment == ""
        assert url.path.endswith("/models")


@pytest.mark.parametrize(
    ("endpoint", "expected_scheme", "expected_host"),
    [
        ("http://localhost:11434", "http", "localhost"),
        ("https://models.example.test/v1", "https", "models.example.test"),
    ],
    ids=["loopback-http", "public-https"],
)
def test_ollama_candidate_urls_are_typed_urls_preserving_validated_authority(
    endpoint, expected_scheme, expected_host
):
    """T3/T4: Ollama probe URLs keep only the policy-validated authority."""
    candidates = _ollama_candidate_urls(endpoint)

    assert len(candidates) == 1
    url = candidates[0]
    assert isinstance(url, httpx.URL)
    assert url.scheme == expected_scheme
    assert url.host == expected_host
    assert url.path.endswith("/api/tags")


async def test_async_get_json_revalidates_url_against_discovery_policy_before_get():
    """T3/T4: only policy-validated absolute URLs may reach AsyncClient.get."""
    validated = httpx.URL("http://127.0.0.1:5272/models")
    get_urls: list[object] = []

    class CaptureClient:
        async def get(self, url, headers=None):
            get_urls.append(url)
            return _FakeResponse({"data": []})

    with patch(
        "embeddings.model_discovery.validate_model_discovery_endpoint",
        return_value=(validated, EndpointPolicy.LOOPBACK_HTTP),
    ) as validate:
        payload = await _async_get_json(
            CaptureClient(), "http://127.0.0.1:5272/models"
        )

    validate.assert_called()
    assert payload == {"data": []}
    assert len(get_urls) == 1
    assert isinstance(get_urls[0], httpx.URL)
    assert get_urls[0] == validated


def test_sync_get_json_revalidates_url_against_discovery_policy_before_get():
    """T3/T4: sync discovery path has the same call-site validation as async."""
    validated = httpx.URL("https://models.example.test/v1/models")
    get_urls: list[object] = []

    class CaptureClient:
        def get(self, url, headers=None):
            get_urls.append(url)
            return _FakeResponse({"data": []})

    with patch(
        "embeddings.model_discovery.validate_model_discovery_endpoint",
        return_value=(validated, EndpointPolicy.PUBLIC_HTTPS),
    ) as validate:
        payload = _sync_get_json(CaptureClient(), "https://models.example.test/v1/models")

    validate.assert_called()
    assert payload == {"data": []}
    assert len(get_urls) == 1
    assert isinstance(get_urls[0], httpx.URL)
    assert get_urls[0] == validated


@pytest.mark.parametrize(
    ("endpoint", "expected_policy"),
    [
        ("http://localhost:5272", EndpointPolicy.LOOPBACK_HTTP),
        ("http://127.0.0.1:5272", EndpointPolicy.LOOPBACK_HTTP),
        ("https://models.example.test", EndpointPolicy.PUBLIC_HTTPS),
    ],
    ids=["localhost-http", "loopback-ipv4-http", "public-https"],
)
async def test_discover_embedding_models_still_allows_public_https_and_loopback_http(
    endpoint, expected_policy
):
    """T3/D4: operator probes remain allowed for public HTTPS and literal loopback HTTP."""
    success_url = f"{endpoint.rstrip('/')}/models"
    responses = {success_url: _FakeResponse({"data": [{"id": "embedding-model"}]})}
    safe_client = _FakeAsyncClient(responses)

    with patch(
        "embeddings.model_discovery.create_model_discovery_async_client",
        return_value=safe_client,
    ) as create_client:
        result = await discover_embedding_models(endpoint)

    assert create_client.call_args.args[0] is expected_policy
    assert result.provider == "openai-compatible"
    assert result.models == ["embedding-model"]
