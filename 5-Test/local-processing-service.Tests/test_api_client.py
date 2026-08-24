"""Unit tests for the local processor API client."""

import asyncio
import base64
import json
import logging
import time
from unittest.mock import AsyncMock, MagicMock, patch

import httpx
import pytest

from api.api_client import ApiClient
from security.safe_http import RedirectBlockedError


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
    monkeypatch.setenv("MCR_API_BASE_URL", "https://api.example.test")


def test_requires_explicit_public_api_base_url(monkeypatch):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    monkeypatch.delenv("MCR_API_BASE_URL")

    with pytest.raises(ValueError, match="(?i)(endpoint|url|required)"):
        ApiClient()


def test_sets_api_base_url_even_when_upload_secret_missing(monkeypatch):
    monkeypatch.setenv("MCR_API_BASE_URL", "https://api.example.test")

    client = ApiClient()

    assert client.is_configured() is False
    assert client._base_url == "https://api.example.test"


@pytest.mark.parametrize(
    "base_url",
    [
        "http://api.example.test",
        "http://localhost:7215",
        "http://127.0.0.1:7215",
        "http://[::1]:7215",
        "https://localhost:7215",
        "https://user:password@api.example.test",
        "https://api.example.test/#fragment",
        "https://api.example.test/?environment=development",
        "https:///missing-authority",
        "https://:443",
        "https://api.example.test:invalid",
        "https://10.1.2.3",
        "https://172.16.0.1",
        "https://192.168.1.10",
        "https://169.254.169.254",
        "https://[fe80::1]",
    ],
    ids=[
        "remote-http",
        "localhost-http",
        "loopback-ipv4-http",
        "loopback-ipv6-http",
        "localhost-https",
        "credentials",
        "fragment",
        "configured-query",
        "missing-authority",
        "empty-host",
        "invalid-port",
        "private-10",
        "private-172",
        "private-192",
        "link-local-ipv4",
        "link-local-ipv6",
    ],
)
def test_api_client_rejects_unsafe_configured_base_url(monkeypatch, base_url):
    """T9: configured API URLs are public HTTPS authorities without queries."""
    monkeypatch.setenv("MCR_API_BASE_URL", base_url)

    with pytest.raises(ValueError, match="(?i)(endpoint|url|https|host)"):
        ApiClient()


async def test_upload_artifact_raises_when_upload_secret_missing(monkeypatch):
    client = ApiClient()

    with pytest.raises(RuntimeError, match="PYTHON_UPLOAD_JOB_SECRET"):
        await client.upload_artifact(
            b"{}",
            "upload-1",
            "graph-entities",
            "application/json",
        )


async def test_download_source_with_access_token_works_without_upload_secret(
    monkeypatch,
):
    client = ApiClient()
    calls = []

    class OkAsyncClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def get(self, url, *, params=None, **kwargs):
            calls.append((url, params, kwargs))
            response = httpx.Response(200, content=b"%PDF-1.4")
            response.request = httpx.Request("GET", url)
            return response

    with patch(
        "api.api_client.httpx.AsyncClient", return_value=OkAsyncClient()
    ) as client_constructor:
        content = await client.download_source(
            "upload-1",
            "manual-pdf",
            access_token="token-123",
        )

    assert content == b"%PDF-1.4"
    assert calls == [
        (
            "https://api.example.test/api/ingestion/artifacts/source/access",
            {
                "uploadId": "upload-1",
                "documentType": "manual-pdf",
                "accessToken": "token-123",
            },
            {},
        )
    ]
    assert client_constructor.call_args.kwargs.get("verify", True) is True
    assert client_constructor.call_args.kwargs.get("follow_redirects") is False


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
            raise BlankTransportError(
                "", request=httpx.Request("POST", "https://localhost:7215")
            )

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    with patch("api.api_client.httpx.AsyncClient", return_value=FailingAsyncClient()):
        with pytest.raises(
            RuntimeError, match="Artifact upload failed after 2 attempts"
        ):
            await client.upload_artifact(
                b"{}",
                "12345678-1234-1234-1234-123456789012",
                "graph-entities",
                "application/json",
            )

    warning_messages = [
        record.getMessage()
        for record in caplog.records
        if record.levelname == "WARNING"
    ]
    assert any("BlankTransportError" in message for message in warning_messages)
    assert any(
        "https://api.example.test/api/ingestion/artifacts/upload" in message
        for message in warning_messages
    )


def test_get_token_diagnostics_extracts_expected_claims():
    payload = {
        "aud": "api://motorcyclerag-api",
        "azp": "d09d356d-62ac-4f38-b636-64169119ea25",
        "roles": ["File.Upload.All"],
        "tid": "tenant-id",
    }
    encoded_payload = (
        base64.urlsafe_b64encode(json.dumps(payload).encode("utf-8"))
        .decode("utf-8")
        .rstrip("=")
    )
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

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
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
        record.getMessage() for record in caplog.records if record.levelname == "ERROR"
    ]
    assert any(
        expected_phrase in message
        and "https://api.example.test/api/ingestion/artifacts/upload" in message
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

    post_calls = []

    class OkAsyncClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def post(self, url, *, headers=None, files=None, params=None):
            post_calls.append((url, params))
            return httpx.Response(202, request=httpx.Request("POST", url))

    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {"access_token": "token"}

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    with patch(
        "api.api_client.httpx.AsyncClient", return_value=OkAsyncClient()
    ) as mock_ctor:
        await client.upload_artifact(
            b"{}",
            "12345678-1234-1234-1234-123456789012",
            "graph-entities",
            "application/json",
        )

    # The per-attempt httpx client must be constructed with the reduced 90s timeout.
    assert mock_ctor.call_args.kwargs.get("timeout") == 90.0
    assert mock_ctor.call_args.kwargs.get("verify", True) is True
    assert mock_ctor.call_args.kwargs.get("follow_redirects") is False
    # A successful 202 must result in exactly one attempt (no retries needed).
    assert mock_ctor.call_count == 1
    assert post_calls == [
        (
            "https://api.example.test/api/ingestion/artifacts/upload",
            {
                "uploadId": "12345678-1234-1234-1234-123456789012",
                "artifactType": "graph-entities",
            },
        )
    ]


async def test_upload_artifact_logs_warning_when_202_body_status_is_not_stored(
    monkeypatch, caplog
):
    """T5: a 202 whose body ``status`` is not ``stored`` (e.g. the API's
    ``stored-not-indexed`` when no ingestion job was found) must be logged at
    warning level naming the uploadId and the returned status, without
    raising and without retrying — the artifact upload itself did succeed."""
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    caplog.set_level(logging.WARNING, logger="api.api_client")

    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {"access_token": "token"}

    upload_id = "12345678-1234-1234-1234-123456789012"

    class NotIndexedAsyncClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def post(self, url, *, headers=None, files=None, params=None):
            return httpx.Response(
                202,
                json={"status": "stored-not-indexed", "uploadId": upload_id},
                request=httpx.Request("POST", url),
            )

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    with patch(
        "api.api_client.httpx.AsyncClient", return_value=NotIndexedAsyncClient()
    ) as mock_ctor:
        await client.upload_artifact(
            b"{}", upload_id, "search-chunks", "application/x-ndjson"
        )

    # No retry: the HTTP upload itself succeeded, so a single attempt suffices.
    assert mock_ctor.call_count == 1

    warnings = [
        record.getMessage()
        for record in caplog.records
        if record.levelname == "WARNING"
    ]
    assert any(
        upload_id in message and "stored-not-indexed" in message
        for message in warnings
    ), f"expected a WARNING naming the uploadId and returned status; got {warnings!r}"


async def test_upload_artifact_does_not_warn_when_202_body_status_is_stored(
    monkeypatch, caplog
):
    """T5 regression guard: the happy-path body ``status: stored`` must not
    trip the new not-indexed warning, and behaves exactly as today."""
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    caplog.set_level(logging.WARNING, logger="api.api_client")

    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {"access_token": "token"}

    upload_id = "12345678-1234-1234-1234-123456789012"

    class StoredAsyncClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def post(self, url, *, headers=None, files=None, params=None):
            return httpx.Response(
                202,
                json={"status": "stored", "uploadId": upload_id},
                request=httpx.Request("POST", url),
            )

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    with patch(
        "api.api_client.httpx.AsyncClient", return_value=StoredAsyncClient()
    ) as mock_ctor:
        await client.upload_artifact(
            b"{}", upload_id, "search-chunks", "application/x-ndjson"
        )

    assert mock_ctor.call_count == 1

    warnings = [
        record.getMessage()
        for record in caplog.records
        if record.levelname == "WARNING"
    ]
    assert warnings == []


async def test_upload_artifact_handles_202_with_non_dict_json_body(
    monkeypatch, caplog
):
    """T5 edge case: a 202 whose response body is valid JSON but not a dict
    (e.g., a bare list, string, or number) must not raise AttributeError,
    must not log a warning, and must still return successfully — the HTTP
    upload itself did succeed."""
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    caplog.set_level(logging.WARNING, logger="api.api_client")

    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {"access_token": "token"}

    upload_id = "12345678-1234-1234-1234-123456789012"

    class NonDictBodyAsyncClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def post(self, url, *, headers=None, files=None, params=None):
            # Return a 202 with a JSON body that is a list, not a dict
            return httpx.Response(
                202,
                content=b'["item1", "item2"]',
                request=httpx.Request("POST", url),
            )

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    with patch(
        "api.api_client.httpx.AsyncClient", return_value=NonDictBodyAsyncClient()
    ) as mock_ctor:
        # Should not raise AttributeError even though body.get() is called on a list
        await client.upload_artifact(
            b"{}", upload_id, "search-chunks", "application/x-ndjson"
        )

    assert mock_ctor.call_count == 1

    warnings = [
        record.getMessage()
        for record in caplog.records
        if record.levelname == "WARNING"
    ]
    # No warning should be logged for a non-dict body (it's not a status signal)
    assert warnings == []


def test_get_token_returns_access_token_and_translates_msal_error(monkeypatch):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    mock_msal = MagicMock()

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    mock_msal.acquire_token_for_client.return_value = {"access_token": "token"}
    assert client._get_token() == "token"

    mock_msal.acquire_token_for_client.return_value = {
        "error_description": "invalid secret"
    }
    with pytest.raises(RuntimeError, match="invalid secret"):
        client._get_token()


async def test_acquire_token_async_caches_and_refreshes_token(monkeypatch):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {
        "access_token": "fresh",
        "expires_in": 600,
    }

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    first = await client._acquire_token_async()
    second = await client._acquire_token_async()

    assert (first, second) == ("fresh", "fresh")
    mock_msal.acquire_token_for_client.assert_called_once()

    client._token_expires_at = 0
    mock_msal.acquire_token_for_client.return_value = {"error_description": "denied"}
    with pytest.raises(RuntimeError, match="denied"):
        await client._acquire_token_async()


@pytest.mark.parametrize(
    "token, expected_key",
    [
        ("not-a-jwt", "token_format"),
        ("header.!!!!.signature", "decode_error"),
    ],
)
def test_get_token_diagnostics_handles_invalid_tokens(token, expected_key):
    assert expected_key in ApiClient._get_token_diagnostics(token)


async def test_download_source_requires_configuration_without_access_token(
    monkeypatch,
):
    client = ApiClient()

    with pytest.raises(RuntimeError, match="not configured"):
        await client.download_source("upload", "manual-pdf")


async def test_download_source_uses_bearer_token_and_handles_error(monkeypatch):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {
        "access_token": "token",
        "expires_in": 600,
    }

    class DownloadClient:
        def __init__(self, response):
            self.response = response
            self.calls = []

        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def get(self, url, **kwargs):
            self.calls.append((url, kwargs))
            return self.response

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    ok = DownloadClient(
        httpx.Response(
            200,
            content=b"source",
            request=httpx.Request("GET", "https://api.example/source"),
        )
    )
    with patch(
        "api.api_client.httpx.AsyncClient", return_value=ok
    ) as client_constructor:
        assert await client.download_source("upload", "manual-pdf") == b"source"
    assert ok.calls[0][1]["headers"] == {"Authorization": "Bearer token"}
    assert ok.calls[0][0] == "https://api.example.test/api/ingestion/artifacts/source"
    assert ok.calls[0][1]["params"] == {
        "uploadId": "upload",
        "documentType": "manual-pdf",
    }
    assert client_constructor.call_args.kwargs.get("verify", True) is True
    assert client_constructor.call_args.kwargs.get("follow_redirects") is False

    denied = DownloadClient(
        httpx.Response(
            403,
            text="forbidden",
            request=httpx.Request("GET", "https://api.example/source"),
        )
    )
    with patch("api.api_client.httpx.AsyncClient", return_value=denied):
        with pytest.raises(RuntimeError, match="HTTP 403"):
            await client.download_source("upload", "manual-pdf")


async def test_upload_artifact_rejects_client_error_without_retry(monkeypatch):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {
        "access_token": "header.eyJhdWQiOiAidGVzdCJ9.signature",
        "expires_in": 600,
    }

    class DeniedClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def post(self, url, **kwargs):
            return httpx.Response(
                403,
                text="forbidden",
                request=httpx.Request("POST", url),
            )

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    with patch("api.api_client.httpx.AsyncClient", return_value=DeniedClient()):
        with pytest.raises(RuntimeError, match="HTTP 403 - forbidden"):
            await client.upload_artifact(
                b"{}", "upload", "graph-entities", "application/json"
            )


async def test_report_stage_skips_unconfigured_and_reports_payload(monkeypatch):
    unconfigured = ApiClient()
    with patch("api.api_client.httpx.AsyncClient") as constructor:
        await unconfigured.report_stage(
            "12345678-1234-1234-1234-123456789012", "parsing"
        )
        await unconfigured.report_stage("", "parsing")
    constructor.assert_not_called()

    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {
        "access_token": "token",
        "expires_in": 600,
    }

    class ReportClient:
        def __init__(self):
            self.kwargs = None

        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def patch(self, url, **kwargs):
            self.kwargs = kwargs
            return httpx.Response(200, request=httpx.Request("PATCH", url))

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        configured = ApiClient()
    report = ReportClient()
    with patch(
        "api.api_client.httpx.AsyncClient", return_value=report
    ) as client_constructor:
        await configured.report_stage(
            "12345678-1234-1234-1234-123456789012",
            "embedding",
            chunks_processed=2,
            total_chunks=3,
            failure_reason="late",
        )

    assert report.kwargs["json"] == {
        "stage": "embedding",
        "chunksProcessed": 2,
        "totalChunks": 3,
        "failureReason": "late",
    }
    assert client_constructor.call_args.kwargs.get("verify", True) is True
    assert client_constructor.call_args.kwargs.get("follow_redirects") is False


async def test_report_stage_swallows_transport_errors(monkeypatch):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=MagicMock()
    ):
        client = ApiClient()
    client._acquire_token_async = AsyncMock(side_effect=RuntimeError("offline"))

    await client.report_stage("12345678-1234-1234-1234-123456789012", "failed")

    client._acquire_token_async.assert_awaited_once()


async def test_report_stage_when_job_id_contains_controls_logs_reversible_sanitized_value(
    monkeypatch, caplog
):
    """T9: API-client sinks must not allow a job id to forge a log record."""
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    caplog.set_level(logging.WARNING, logger="api.api_client")

    class FailedReportClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def patch(self, url, **kwargs):
            return httpx.Response(500, request=httpx.Request("PATCH", url))

    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {"access_token": "token"}
    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    with patch("api.api_client.httpx.AsyncClient", return_value=FailedReportClient()):
        await client.report_stage("job-1\r\nforged-warning", "failed")

    messages = [record.getMessage() for record in caplog.records]
    assert any("job-1\\r\\nforged-warning" in message for message in messages)
    assert all("job-1\r\nforged-warning" not in message for message in messages)


async def test_download_source_with_access_token_raises_on_redirect(monkeypatch):
    client = ApiClient()

    class RedirectClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def get(self, url, *, params=None, **kwargs):
            response = httpx.Response(
                302,
                headers={"location": "https://evil.example"},
                request=httpx.Request("GET", url),
            )
            return response

    with patch("api.api_client.httpx.AsyncClient", return_value=RedirectClient()):
        with pytest.raises(RedirectBlockedError):
            await client.download_source(
                "upload-1", "manual-pdf", access_token="token-123"
            )


async def test_download_source_with_access_token_raises_on_non_success(monkeypatch):
    client = ApiClient()

    class NotFoundClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def get(self, url, *, params=None, **kwargs):
            return httpx.Response(
                404, text="missing", request=httpx.Request("GET", url)
            )

    with patch("api.api_client.httpx.AsyncClient", return_value=NotFoundClient()):
        with pytest.raises(RuntimeError, match="HTTP 404"):
            await client.download_source(
                "upload-1", "manual-pdf", access_token="token-123"
            )


async def test_download_source_without_access_token_raises_on_redirect(monkeypatch):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {
        "access_token": "token",
        "expires_in": 600,
    }

    class RedirectClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def get(self, url, **kwargs):
            return httpx.Response(
                302,
                headers={"location": "https://evil.example"},
                request=httpx.Request("GET", url),
            )

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    with patch("api.api_client.httpx.AsyncClient", return_value=RedirectClient()):
        with pytest.raises(RedirectBlockedError):
            await client.download_source("upload", "manual-pdf")


def test_get_token_raises_when_client_not_configured(monkeypatch):
    client = ApiClient()

    with pytest.raises(RuntimeError, match="not configured"):
        client._get_token()


async def test_acquire_token_async_returns_cache_hit_found_after_lock_acquired(
    monkeypatch,
):
    """Covers the double-checked-locking re-check: the outer check sees a
    stale token, but the token became valid again by the time the lock was
    acquired (e.g. a concurrent refresh completed in between)."""
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    mock_msal = MagicMock()
    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    client._token_cache = "cached-token"
    client._token_expires_at = 100.0

    with patch(
        "api.api_client.time.monotonic", side_effect=[50.0, 30.0]
    ):
        result = await client._acquire_token_async()

    assert result == "cached-token"
    mock_msal.acquire_token_for_client.assert_not_called()


async def test_acquire_token_async_raises_when_msal_app_missing_after_lock(
    monkeypatch,
):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=MagicMock()
    ):
        client = ApiClient()

    client._token_cache = None
    client._token_expires_at = 0.0
    client._msal_app = None

    with pytest.raises(RuntimeError, match="not configured"):
        await client._acquire_token_async()


async def test_upload_artifact_retries_on_redirect_and_eventually_fails(monkeypatch):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {"access_token": "token"}

    class RedirectAsyncClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def post(self, url, **kwargs):
            return httpx.Response(
                302,
                headers={"location": "https://evil.example"},
                request=httpx.Request("POST", url),
            )

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    with patch(
        "api.api_client.httpx.AsyncClient", return_value=RedirectAsyncClient()
    ):
        with pytest.raises(
            RuntimeError, match="Artifact upload failed after 2 attempts"
        ):
            await client.upload_artifact(
                b"{}",
                "12345678-1234-1234-1234-123456789012",
                "graph-entities",
                "application/json",
            )


async def test_upload_artifact_retries_on_server_error_and_eventually_fails(
    monkeypatch,
):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {"access_token": "token"}

    post_calls = []

    class ServerErrorAsyncClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def post(self, url, **kwargs):
            post_calls.append(url)
            return httpx.Response(
                500, text="boom", request=httpx.Request("POST", url)
            )

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    with patch(
        "api.api_client.httpx.AsyncClient", return_value=ServerErrorAsyncClient()
    ):
        with pytest.raises(
            RuntimeError, match="Artifact upload failed after 2 attempts: .*boom"
        ):
            await client.upload_artifact(
                b"{}",
                "12345678-1234-1234-1234-123456789012",
                "graph-entities",
                "application/json",
            )

    assert len(post_calls) == 2


async def test_report_stage_logs_warning_on_non_success_response(monkeypatch, caplog):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    caplog.set_level(logging.WARNING, logger="api.api_client")
    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {"access_token": "token"}

    class ServerErrorReportClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def patch(self, url, **kwargs):
            return httpx.Response(500, request=httpx.Request("PATCH", url))

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    with patch(
        "api.api_client.httpx.AsyncClient", return_value=ServerErrorReportClient()
    ):
        await client.report_stage(
            "12345678-1234-1234-1234-123456789012", "failed"
        )

    warnings = [
        record.getMessage() for record in caplog.records if record.levelname == "WARNING"
    ]
    assert any("Stage report failed" in message for message in warnings)


async def test_report_stage_swallows_redirect_response(monkeypatch, caplog):
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    caplog.set_level(logging.WARNING, logger="api.api_client")
    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {"access_token": "token"}

    class RedirectReportClient:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def patch(self, url, **kwargs):
            return httpx.Response(
                302,
                headers={"location": "https://evil.example"},
                request=httpx.Request("PATCH", url),
            )

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    with patch(
        "api.api_client.httpx.AsyncClient", return_value=RedirectReportClient()
    ):
        await client.report_stage("12345678-1234-1234-1234-123456789012", "failed")

    warnings = [
        record.getMessage() for record in caplog.records if record.levelname == "WARNING"
    ]
    assert any("Stage report failed" in message for message in warnings)


@pytest.mark.parametrize(
    "processor_job_id",
    [
        "not-a-uuid",
        "../escape",
        "12345678-1234-1234-1234-123456789012/extra",
        "https://evil.example/status",
        "12345678-1234-1234-1234-123456789012@evil.example",
    ],
    ids=[
        "plain-text",
        "path-traversal",
        "path-suffix",
        "absolute-url",
        "userinfo-lookalike",
    ],
)
async def test_report_stage_when_job_id_is_not_uuid_skips_outbound_request(
    monkeypatch, processor_job_id
):
    """T3: non-UUID job ids never become stage-report path segments."""
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {
        "access_token": "token",
        "expires_in": 600,
    }

    class ReportClient:
        def __init__(self):
            self.urls = []

        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def patch(self, url, **kwargs):
            self.urls.append(url)
            return httpx.Response(200, request=httpx.Request("PATCH", str(url)))

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    report = ReportClient()
    with patch(
        "api.api_client.create_api_https_async_client", return_value=report
    ) as create_client:
        await client.report_stage(processor_job_id, "embedding")

    create_client.assert_not_called()
    assert report.urls == []


@pytest.mark.parametrize(
    ("api_base_url", "expected_path"),
    [
        (
            "https://api.example.test:8443",
            "/api/ingestion/jobs/by-run/12345678-1234-1234-1234-123456789012/status",
        ),
        (
            "https://api.example.test:8443/base",
            "/base/api/ingestion/jobs/by-run/12345678-1234-1234-1234-123456789012/status",
        ),
    ],
)
async def test_report_stage_builds_typed_url_from_validated_base_and_canonical_uuid(
    monkeypatch,
    api_base_url,
    expected_path,
):
    """T3/T4 contract: authority from validated base; UUID path via httpx.URL join.

    Path-prefixed bases must keep their prefix (directory-base join), matching
    ``model_discovery._join_discovery_path`` semantics.
    """
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "secret")
    monkeypatch.setenv("MCR_API_BASE_URL", api_base_url)
    mock_msal = MagicMock()
    mock_msal.acquire_token_for_client.return_value = {
        "access_token": "token",
        "expires_in": 600,
    }

    class ReportClient:
        def __init__(self):
            self.url = None

        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        async def patch(self, url, **kwargs):
            self.url = url
            return httpx.Response(200, request=httpx.Request("PATCH", str(url)))

    with patch(
        "api.api_client.msal.ConfidentialClientApplication", return_value=mock_msal
    ):
        client = ApiClient()

    report = ReportClient()
    raw_job_id = "{12345678-1234-1234-1234-123456789012}"
    canonical_job_id = "12345678-1234-1234-1234-123456789012"
    with patch("api.api_client.create_api_https_async_client", return_value=report):
        await client.report_stage(raw_job_id, "embedding")

    assert isinstance(report.url, httpx.URL)
    assert report.url.scheme == "https"
    assert report.url.host == "api.example.test"
    assert report.url.port == 8443
    assert report.url.username is None or report.url.username == ""
    assert report.url.password is None or report.url.password == ""
    assert report.url.fragment is None or report.url.fragment == ""
    assert bytes(report.url.query) == b""
    assert report.url.path == expected_path
    # Path segment must be the canonical UUID, never the braced/raw form.
    assert raw_job_id not in report.url.path
    assert canonical_job_id in report.url.path
    assert report.url.host == httpx.URL(client._base_url).host
    assert report.url.scheme == httpx.URL(client._base_url).scheme
    assert report.url.port == httpx.URL(client._base_url).port
