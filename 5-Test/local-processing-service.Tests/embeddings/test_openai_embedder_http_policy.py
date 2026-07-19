"""Contract tests: OpenAIEmbedder must use policy-bound HTTP (plan T3/T5).

These tests define the unified model-provider HTTP contract before T5 wires
OpenAIEmbedder to ``create_model_provider_async_client``. They are expected to
fail (red) until construct-time validation and policy client injection land.
"""

from __future__ import annotations

from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from security.safe_http import EndpointPolicy, EndpointPolicyError

_UNSAFE_HTTP_ENDPOINTS = (
    "http://models.example.test:5272/v1",
    "http://127.0.0.1.example.test:5272/v1",
    "http://10.0.0.1:1234/v1",
    "http://192.168.1.1:1234/v1",
)

_POLICY_ALLOWED_ENDPOINTS = (
    "http://localhost:1234/v1",
    "http://127.0.0.1:1234/v1",
    "http://[::1]:1234/v1",
    "https://models.example.test/v1",
)


class _FakePolicyAsyncClient:
    """Minimal async client stand-in for policy-bound probe traffic."""

    def __init__(self) -> None:
        self.get = AsyncMock(
            return_value=MagicMock(
                status_code=200,
                raise_for_status=MagicMock(),
            )
        )

    async def __aenter__(self) -> _FakePolicyAsyncClient:
        return self

    async def __aexit__(self, exc_type, exc, tb) -> bool:
        return False


class TestOpenAIEmbedderConstructEndpointPolicy:
    @pytest.mark.parametrize("endpoint", _UNSAFE_HTTP_ENDPOINTS)
    def test_construct_when_http_is_not_literal_loopback_rejects(
        self, endpoint: str
    ) -> None:
        with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
            from embeddings.openai_embedder import OpenAIEmbedder

            with pytest.raises(EndpointPolicyError):
                OpenAIEmbedder(endpoint=endpoint)

    @pytest.mark.parametrize("endpoint", _UNSAFE_HTTP_ENDPOINTS)
    def test_construct_when_env_endpoint_is_not_literal_loopback_rejects(
        self,
        monkeypatch: pytest.MonkeyPatch,
        endpoint: str,
    ) -> None:
        monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", endpoint)

        with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
            from embeddings.openai_embedder import OpenAIEmbedder

            with pytest.raises(EndpointPolicyError):
                OpenAIEmbedder()

    @pytest.mark.parametrize("endpoint", _POLICY_ALLOWED_ENDPOINTS)
    def test_construct_when_endpoint_is_policy_allowed_succeeds(
        self, endpoint: str
    ) -> None:
        with patch("embeddings.openai_embedder.openai.AsyncOpenAI"):
            from embeddings.openai_embedder import OpenAIEmbedder

            embedder = OpenAIEmbedder(endpoint=endpoint)

        assert embedder._endpoint == endpoint.rstrip("/")


def _configure_bare_httpx_mock(mock_bare_httpx: MagicMock) -> None:
    """Return a fully-awaitable stand-in so red-state bare-httpx calls do not warn."""
    bare_client = MagicMock()
    bare_client.get = AsyncMock(
        return_value=MagicMock(status_code=200, raise_for_status=MagicMock())
    )
    mock_bare_httpx.return_value.__aenter__ = AsyncMock(return_value=bare_client)
    mock_bare_httpx.return_value.__aexit__ = AsyncMock(return_value=False)


class TestOpenAIEmbedderPolicyClientWiring:
    def test_create_client_passes_policy_http_client_to_openai(self) -> None:
        policy_client = MagicMock()

        with patch(
            "embeddings.openai_embedder.create_model_provider_async_client",
            create=True,
            return_value=policy_client,
        ) as mock_factory:
            with patch("embeddings.openai_embedder.openai.AsyncOpenAI") as mock_openai:
                from embeddings.openai_embedder import OpenAIEmbedder

                embedder = OpenAIEmbedder(endpoint="http://127.0.0.1:1234/v1")
                embedder._create_client()

        mock_factory.assert_called_once_with(
            EndpointPolicy.LOOPBACK_HTTP,
            timeout=embedder._request_timeout_seconds,
        )
        mock_openai.assert_called_once_with(
            base_url="http://127.0.0.1:1234/v1",
            api_key="local",
            http_client=policy_client,
        )

    async def test_check_status_uses_policy_client_not_bare_httpx(self) -> None:
        fake_policy_client = _FakePolicyAsyncClient()

        with patch("httpx.AsyncClient") as mock_bare_httpx:
            _configure_bare_httpx_mock(mock_bare_httpx)
            with patch(
                "embeddings.openai_embedder.create_model_provider_async_client",
                create=True,
                return_value=fake_policy_client,
            ) as mock_factory:
                from embeddings.openai_embedder import OpenAIEmbedder

                embedder = OpenAIEmbedder(endpoint="http://127.0.0.1:1234/v1")
                status = await embedder.check_status()

        assert status == "connected"
        mock_bare_httpx.assert_not_called()
        mock_factory.assert_called_once_with(
            EndpointPolicy.LOOPBACK_HTTP,
            timeout=embedder._health_timeout_seconds,
        )
        fake_policy_client.get.assert_awaited_once_with("http://127.0.0.1:1234/v1")
