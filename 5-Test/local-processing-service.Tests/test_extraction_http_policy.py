"""Contract tests: extractors must validate endpoints and use policy HTTP (plan T3/T5).

MetadataExtractor and GraphExtractor reject non-policy endpoints at construct
time. MetadataExtractor probes via a short-timeout policy client, separate
from the long-lived chat client.
"""

from __future__ import annotations

from unittest.mock import AsyncMock, MagicMock, call, patch

import pytest

from security.safe_http import EndpointPolicy, EndpointPolicyError

_UNSAFE_GRAPH_ENDPOINTS = (
    "http://models.example.test:5272/v1",
    "http://127.0.0.1.example.test:5272/v1",
    "http://10.0.0.1:9999/v1",
    "http://192.168.1.1:9999/v1",
)


class _FakePolicyAsyncClient:
    def __init__(self) -> None:
        self.get = AsyncMock(
            return_value=MagicMock(
                status_code=200,
                raise_for_status=MagicMock(),
                json=MagicMock(return_value={"data": [{"id": "test-model"}]}),
            )
        )

    async def __aenter__(self) -> _FakePolicyAsyncClient:
        return self

    async def __aexit__(self, exc_type, exc, tb) -> bool:
        return False


def _configure_bare_httpx_mock(mock_bare_httpx: MagicMock) -> None:
    bare_client = MagicMock()
    bare_client.get = AsyncMock(
        return_value=MagicMock(
            status_code=200,
            raise_for_status=MagicMock(),
            json=MagicMock(return_value={"data": [{"id": "test-model"}]}),
        )
    )
    mock_bare_httpx.return_value.__aenter__ = AsyncMock(return_value=bare_client)
    mock_bare_httpx.return_value.__aexit__ = AsyncMock(return_value=False)


class TestMetadataExtractorEndpointPolicy:
    @pytest.mark.parametrize("endpoint", _UNSAFE_GRAPH_ENDPOINTS)
    def test_construct_when_http_is_not_literal_loopback_rejects(
        self,
        monkeypatch: pytest.MonkeyPatch,
        endpoint: str,
    ) -> None:
        monkeypatch.setenv("GRAPH_EXTRACTION_ENDPOINT", endpoint)

        from extraction.metadata_extractor import MetadataExtractor

        with pytest.raises(EndpointPolicyError):
            MetadataExtractor()

    def test_construct_when_public_https_endpoint_is_allowed(
        self, monkeypatch: pytest.MonkeyPatch
    ) -> None:
        monkeypatch.setenv(
            "GRAPH_EXTRACTION_ENDPOINT", "https://models.example.test/v1"
        )

        from extraction.metadata_extractor import MetadataExtractor

        extractor = MetadataExtractor()

        assert extractor._endpoint == "https://models.example.test/v1"

    async def test_check_connectivity_uses_policy_client_not_bare_httpx(
        self,
        monkeypatch: pytest.MonkeyPatch,
    ) -> None:
        monkeypatch.setenv("GRAPH_EXTRACTION_ENDPOINT", "http://127.0.0.1:9999/v1")
        fake_policy_client = _FakePolicyAsyncClient()

        with patch("httpx.AsyncClient") as mock_bare_httpx:
            _configure_bare_httpx_mock(mock_bare_httpx)
            with patch(
                "extraction.metadata_extractor.create_model_provider_async_client",
                create=True,
                return_value=fake_policy_client,
            ) as mock_factory:
                with patch("extraction.metadata_extractor.openai.AsyncOpenAI"):
                    from extraction.metadata_extractor import (
                        MetadataExtractor,
                        _CHAT_HTTP_TIMEOUT,
                        _PROBE_HTTP_TIMEOUT,
                    )

                    extractor = MetadataExtractor()
                    await extractor.check_connectivity()

        mock_bare_httpx.assert_not_called()
        # Construct keeps the long chat client; probe uses a short-lived client.
        mock_factory.assert_has_calls(
            [
                call(EndpointPolicy.LOOPBACK_HTTP, timeout=_CHAT_HTTP_TIMEOUT),
                call(EndpointPolicy.LOOPBACK_HTTP, timeout=_PROBE_HTTP_TIMEOUT),
            ]
        )
        assert mock_factory.call_count == 2
        assert 5.0 <= _PROBE_HTTP_TIMEOUT <= 10.0
        fake_policy_client.get.assert_awaited_once_with(
            "http://127.0.0.1:9999/v1/models"
        )


class TestGraphExtractorEndpointPolicy:
    @pytest.mark.parametrize("endpoint", _UNSAFE_GRAPH_ENDPOINTS)
    def test_construct_when_http_is_not_literal_loopback_rejects(
        self,
        monkeypatch: pytest.MonkeyPatch,
        endpoint: str,
    ) -> None:
        monkeypatch.setenv("GRAPH_EXTRACTION_ENDPOINT", endpoint)

        from extraction.graph_extractor import GraphExtractor

        with pytest.raises(EndpointPolicyError):
            GraphExtractor()

    def test_construct_when_public_https_endpoint_is_allowed(
        self, monkeypatch: pytest.MonkeyPatch
    ) -> None:
        monkeypatch.setenv(
            "GRAPH_EXTRACTION_ENDPOINT", "https://models.example.test/v1"
        )

        from extraction.graph_extractor import GraphExtractor

        extractor = GraphExtractor()

        assert extractor._endpoint == "https://models.example.test/v1"
