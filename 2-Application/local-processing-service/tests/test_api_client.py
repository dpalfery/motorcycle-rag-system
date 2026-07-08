"""Unit tests for the local processor API client."""

import base64
import json
import logging
from unittest.mock import MagicMock, patch

import httpx
import pytest

from api.api_client import ApiClient


@pytest.fixture(autouse=True)
def _clear_env(monkeypatch):
    for name in [
        "PYTHON_UPLOAD_JOB_SECRET",
        "MCR_LOCAL_PROCESSOR_TENANT_ID",
        "MCR_LOCAL_PROCESSOR_CLIENT_ID",
        "MCR_API_SCOPE",
        "MCR_API_BASE_URL",
    ]:
        monkeypatch.delenv(name, raising=False)


def test_defaults_to_api_dev_https_port(monkeypatch):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")

    with patch("api.api_client.msal.ConfidentialClientApplication", return_value=MagicMock()):
        client = ApiClient()

    assert client.is_configured() is True
    assert client._base_url == "https://localhost:7215"


def test_sets_api_base_url_even_when_upload_secret_missing(monkeypatch):
    monkeypatch.setenv("MCR_API_BASE_URL", "https://localhost:7215")

    client = ApiClient()

    assert client.is_configured() is False
    assert client._base_url == "https://localhost:7215"


async def test_upload_artifact_raises_when_upload_secret_missing(monkeypatch):
    monkeypatch.setenv("MCR_API_BASE_URL", "https://localhost:7215")

    client = ApiClient()

    with pytest.raises(RuntimeError, match="PYTHON_UPLOAD_JOB_SECRET"):
        await client.upload_artifact(
            b"{}",
            "upload-1",
            "graph-entities",
            "application/json",
        )


async def test_download_source_with_access_token_works_without_upload_secret(monkeypatch):
    monkeypatch.setenv("MCR_API_BASE_URL", "https://localhost:7215")

    client = ApiClient()

    class OkAsyncClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def get(self, url):
            assert "accessToken=token-123" in url
            response = httpx.Response(200, content=b"%PDF-1.4")
            response.request = httpx.Request("GET", url)
            return response

    with patch("api.api_client.httpx.AsyncClient", return_value=OkAsyncClient()):
        content = await client.download_source(
            "upload-1",
            "manual-pdf",
            access_token="token-123",
        )

    assert content == b"%PDF-1.4"


async def test_upload_artifact_logs_exception_details_for_blank_transport_errors(
    monkeypatch, caplog
):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")

    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {"access_token": "token"}

    class BlankTransportError(httpx.ConnectError):
        def __str__(self):
            return ""

    class FailingAsyncClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def post(self, *args, **kwargs):
            raise BlankTransportError("", request=httpx.Request("POST", "https://localhost:7215"))

    with patch("api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal):
        client = ApiClient()

    with patch("api.api_client.httpx.AsyncClient", return_value=FailingAsyncClient()):
        with pytest.raises(RuntimeError, match="Artifact upload failed after 2 attempts"):
            await client.upload_artifact(
                b"{}",
                "12345678-1234-1234-1234-123456789012",
                "graph-entities",
                "application/json",
            )

    warning_messages = [record.getMessage() for record in caplog.records if record.levelname == "WARNING"]
    assert any("BlankTransportError" in message for message in warning_messages)
    assert any("https://localhost:7215/api/ingestion/artifacts/upload" in message for message in warning_messages)


def test_get_token_diagnostics_extracts_expected_claims():
    payload = {
        "aud": "api://motorcyclerag-api",
        "azp": "d09d356d-62ac-4f38-b636-64169119ea25",
        "roles": ["File.Upload.All"],
        "tid": "tenant-id",
    }
    encoded_payload = base64.urlsafe_b64encode(
        json.dumps(payload).encode("utf-8")
    ).decode("utf-8").rstrip("=")
    token = f"header.{encoded_payload}.signature"

    diagnostics = ApiClient._get_token_diagnostics(token)

    assert diagnostics["aud"] == payload["aud"]
    assert diagnostics["azp"] == payload["azp"]
    assert diagnostics["roles"] == payload["roles"]


@pytest.mark.parametrize(
    "exc, expected_phrase",
    [
        (
            httpx.ConnectTimeout(
                "connect stall",
                request=httpx.Request("POST", "https://localhost:7215"),
            ),
            "Connect timeout",
        ),
        (
            httpx.ReadTimeout(
                "read stall",
                request=httpx.Request("POST", "https://localhost:7215"),
            ),
            "Read timeout",
        ),
        (
            httpx.TimeoutException(
                "overall stall",
                request=httpx.Request("POST", "https://localhost:7215"),
            ),
            "Overall timeout",
        ),
    ],
    ids=["connect-timeout", "read-timeout", "overall-timeout"],
)
async def test_upload_artifact_logs_named_timeout_phase(
    monkeypatch, caplog, exc, expected_phrase
):
    """T7: connect/read/overall timeouts each emit a distinct, named ERROR log."""
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    caplog.set_level(logging.ERROR, logger="api.api_client")

    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {"access_token": "token"}

    class TimeoutAsyncClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def post(self, *args, **kwargs):
            raise exc

    with patch("api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal):
        client = ApiClient()

    with patch("api.api_client.httpx.AsyncClient", return_value=TimeoutAsyncClient()):
        with pytest.raises(
            RuntimeError, match="Artifact upload failed after 2 attempts"
        ):
            await client.upload_artifact(
                b"{}",
                "12345678-1234-1234-1234-123456789012",
                "graph-entities",
                "application/json",
            )

    error_messages = [
        record.getMessage()
        for record in caplog.records
        if record.levelname == "ERROR"
    ]
    assert any(
        expected_phrase in message
        and "https://localhost:7215/api/ingestion/artifacts/upload" in message
        and "90.0s" in message
        for message in error_messages
    ), f"expected '{expected_phrase}' ERROR log; got {error_messages!r}"


async def test_upload_artifact_uses_reduced_timeout_and_two_attempts(monkeypatch):
    """T7: source constants are 90s/2 and httpx.AsyncClient is built with timeout=90s."""
    from api import api_client

    # Acceptance criterion 1: 90s timeout, 2 attempts (not 120s x 3).
    assert api_client._UPLOAD_TIMEOUT_SECONDS == 90.0
    assert api_client._UPLOAD_MAX_ATTEMPTS == 2

    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")

    class OkAsyncClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def post(self, url, *, headers=None, files=None):
            return httpx.Response(202, request=httpx.Request("POST", url))

    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {"access_token": "token"}

    with patch("api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal):
        client = ApiClient()

    with patch("api.api_client.httpx.AsyncClient", return_value=OkAsyncClient()) as mock_ctor:
        await client.upload_artifact(
            b"{}",
            "12345678-1234-1234-1234-123456789012",
            "graph-entities",
            "application/json",
        )

    # The per-attempt httpx client must be constructed with the reduced 90s timeout.
    assert mock_ctor.call_args.kwargs.get("timeout") == 90.0
    # A successful 202 must result in exactly one attempt (no retries needed).
    assert mock_ctor.call_count == 1
