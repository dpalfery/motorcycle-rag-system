"""MSAL M2M client for uploading processed artifacts to the MotorcycleRAG API."""

import asyncio
import base64
import json
import logging
import os
import time
import uuid

import httpx
import msal

from security.log_sanitizer import sanitize_log_value
from security.safe_http import (
    create_api_https_async_client,
    require_non_redirect_success,
    validate_api_base_url,
)

logger = logging.getLogger(__name__)

_DEFAULT_TENANT_ID = "0f8f8a52-f135-43af-af88-e0b54ca9ff91"
_DEFAULT_CLIENT_ID = "d09d356d-62ac-4f38-b636-64169119ea25"
_DEFAULT_SCOPE = "api://motorcyclerag-api/.default"

# T7: total HTTP timeout for a single artifact-upload attempt, in seconds.
# Previously 120s; reduced to 90s so a stalled upload fails fast instead of
# producing a multi-minute perceived freeze.
_UPLOAD_TIMEOUT_SECONDS = 90.0

# T7: maximum number of upload attempts (1 initial try + retries). Previously
# 3; reduced to 2 to bound the worst-case perceived wait (90s x 2 + backoff).
_UPLOAD_MAX_ATTEMPTS = 2


def _safe_log_value(value: object) -> str:
    """Convert a potentially untrusted value into a log-safe representation."""
    return sanitize_log_value(str(value))


class ApiClient:
    """Uploads processed artifacts to the MotorcycleRAG API via MSAL client credentials."""

    def __init__(self) -> None:
        base_url = os.environ.get("MCR_API_BASE_URL")
        if not base_url:
            raise ValueError("MCR_API_BASE_URL is required")
        self._base_url = str(validate_api_base_url(base_url))
        secret = os.environ.get("PYTHON_UPLOAD_JOB_SECRET", "").strip()
        if not secret:
            self._configured = False
            self._msal_app = None
            logger.warning(
                "PYTHON_UPLOAD_JOB_SECRET not set — artifact upload via API is disabled. "
                "Source download via access token is still available when the API issues one."
            )
            return

        tenant_id = os.environ.get("MCR_LOCAL_PROCESSOR_TENANT_ID", _DEFAULT_TENANT_ID)
        client_id = os.environ.get("MCR_LOCAL_PROCESSOR_CLIENT_ID", _DEFAULT_CLIENT_ID)
        scope = os.environ.get("MCR_API_SCOPE", _DEFAULT_SCOPE)
        authority = f"https://login.microsoftonline.com/{tenant_id}"

        self._scope = [scope]
        self._msal_app = msal.ConfidentialClientApplication(
            client_id,
            authority=authority,
            client_credential=secret,
        )
        self._configured = True
        self._token_cache: str | None = None
        self._token_expires_at: float = 0.0
        self._token_lock = asyncio.Lock()
        logger.info(
            "ApiClient initialised (tenant=%s, client=%s, base_url=%s).",
            _safe_log_value(tenant_id),
            _safe_log_value(client_id),
            _safe_log_value(self._base_url),
        )

    def is_configured(self) -> bool:
        return self._configured

    async def download_source(
        self,
        upload_id: str,
        document_type: str,
        access_token: str | None = None,
    ) -> bytes:
        """Download an ingestion source file from the MotorcycleRAG API."""
        if access_token:
            url = f"{self._base_url}/api/ingestion/artifacts/source/access"
            params = {
                "uploadId": upload_id,
                "documentType": document_type,
                "accessToken": access_token,
            }
            async with create_api_https_async_client(timeout=300.0) as client:
                response = await client.get(url, params=params)
            if response.is_redirect:
                require_non_redirect_success(response)
            if not response.is_success:
                raise RuntimeError(
                    f"Source download failed: HTTP {response.status_code} - {response.text[:500]}"
                )
            return response.content

        if not self._configured:
            raise RuntimeError(
                "ApiClient is not configured — cannot download source via API."
            )

        token = await self._acquire_token_async()
        url = f"{self._base_url}/api/ingestion/artifacts/source"
        params = {"uploadId": upload_id, "documentType": document_type}
        headers = {"Authorization": f"Bearer {token}"}
        async with create_api_https_async_client(timeout=300.0) as client:
            response = await client.get(url, headers=headers, params=params)
        if response.is_redirect:
            require_non_redirect_success(response)
        if not response.is_success:
            raise RuntimeError(
                f"Source download failed: HTTP {response.status_code} - {response.text[:500]}"
            )
        return response.content

    def _get_token(self) -> str:
        if self._msal_app is None:
            raise RuntimeError("ApiClient is not configured for token acquisition")

        result = self._msal_app.acquire_token_for_client(scopes=self._scope)
        if "access_token" not in result:
            raise RuntimeError(
                f"MSAL token acquisition failed: {result.get('error_description', result)}"
            )
        return result["access_token"]

    async def _acquire_token_async(self) -> str:
        """Return a valid MSAL access token, acquiring or refreshing only when needed.

        Caches the token in-process with an asyncio.Lock so concurrent callers
        (including fire-and-forget report_stage tasks) do not contend for the
        default thread pool.  MSAL token lifetime is typically 3600 s; a 60 s
        buffer prevents expiry mid-request.
        """
        if self._token_cache and time.monotonic() < self._token_expires_at - 60:
            return self._token_cache

        async with self._token_lock:
            if self._token_cache and time.monotonic() < self._token_expires_at - 60:
                return self._token_cache

            if self._msal_app is None:
                raise RuntimeError("ApiClient is not configured for token acquisition")

            result = await asyncio.to_thread(
                self._msal_app.acquire_token_for_client, scopes=self._scope
            )
            if "access_token" not in result:
                raise RuntimeError(
                    f"MSAL token acquisition failed: {result.get('error_description', result)}"
                )

            self._token_cache = result["access_token"]
            self._token_expires_at = time.monotonic() + result.get("expires_in", 3600)
            return self._token_cache

    @staticmethod
    def _get_token_diagnostics(token: str) -> dict[str, object]:
        try:
            parts = token.split(".")
            if len(parts) < 2:
                return {"token_format": "invalid"}

            payload = parts[1]
            padding = "=" * (-len(payload) % 4)
            decoded = base64.urlsafe_b64decode(payload + padding)
            claims = json.loads(decoded.decode("utf-8"))
            return {
                "aud": claims.get("aud"),
                "azp": claims.get("azp"),
                "appid": claims.get("appid"),
                "roles": claims.get("roles", []),
                "scp": claims.get("scp"),
                "iss": claims.get("iss"),
                "tid": claims.get("tid"),
            }
        except Exception as exc:
            return {"decode_error": f"{type(exc).__name__}: {exc}"}

    async def upload_artifact(
        self,
        data: bytes,
        upload_id: str,
        artifact_type: str,
        content_type: str,
    ) -> None:
        """POST processed artifact bytes to the API.

        Retries transient failures up to ``_UPLOAD_MAX_ATTEMPTS`` times with
        exponential backoff (``2 ** attempt``). Each attempt is bounded by
        ``_UPLOAD_TIMEOUT_SECONDS``. Non-transient responses (including
        4xx) are not retried. Timeout exceptions are logged by phase
        (connect/read/overall) so a stalled upload is diagnosable.
        """
        if not self._configured:
            raise RuntimeError(
                "ApiClient is not configured; cannot upload processed artifacts. "
                "Set PYTHON_UPLOAD_JOB_SECRET before starting the local processor."
            )

        token = await self._acquire_token_async()
        filename = (
            "chunks.jsonl" if artifact_type == "search-chunks" else "entities.json"
        )
        url = f"{self._base_url}/api/ingestion/artifacts/upload"
        params = {"uploadId": upload_id, "artifactType": artifact_type}
        headers = {"Authorization": f"Bearer {token}"}
        last_exc: Exception | None = None

        logger.info(
            "Starting artifact upload for upload %s (artifact_type=%s, bytes=%d, url=%s)",
            _safe_log_value(upload_id),  # codeql[py/log-injection]
            _safe_log_value(artifact_type),
            len(data),
            _safe_log_value(url),
        )

        for attempt in range(_UPLOAD_MAX_ATTEMPTS):
            try:
                logger.info(
                    "Artifact upload attempt %d for upload %s (artifact_type=%s)",
                    attempt + 1,
                    _safe_log_value(upload_id),  # codeql[py/log-injection]
                    _safe_log_value(artifact_type),
                )
                async with create_api_https_async_client(
                    timeout=_UPLOAD_TIMEOUT_SECONDS,
                ) as client:
                    response = await client.post(
                        url,
                        headers=headers,
                        files={"file": (filename, data, content_type)},
                        params=params,
                    )
                logger.info(
                    "Artifact upload attempt %d for upload %s returned HTTP %d",
                    attempt + 1,
                    _safe_log_value(upload_id),  # codeql[py/log-injection]
                    response.status_code,
                )
                if response.is_redirect:
                    require_non_redirect_success(response)
                if response.status_code < 500:
                    if response.is_success or response.status_code == 202:
                        logger.info(
                            "Uploaded %s artifact for upload %s.",
                            _safe_log_value(artifact_type),
                            _safe_log_value(upload_id),  # codeql[py/log-injection]
                        )
                        return
                    response_text = response.text.strip()
                    detail = f"HTTP {response.status_code}"
                    if response_text:
                        detail = f"{detail} - {response_text[:500]}"
                    if response.status_code in (401, 403):
                        logger.error(
                            "Artifact upload authorization failed for %s. Token diagnostics: %s",
                            _safe_log_value(url),
                            _safe_log_value(repr(self._get_token_diagnostics(token))),
                        )
                    raise RuntimeError(f"Artifact upload failed: {detail}")
                response_text = response.text.strip()
                detail = f"Artifact upload server error: HTTP {response.status_code}"
                if response_text:
                    detail = f"{detail} - {response_text[:500]}"
                last_exc = RuntimeError(detail)
            except httpx.HTTPError as exc:
                last_exc = exc
                # Named timeout logging (T7): a hang must be observable, not silent.
                # Distinguish the stalled phase so operators can tell whether the
                # server was unreachable (connect), stalled mid-transfer (read), or
                # hit another timeout budget (overall). isinstance ordering matters:
                # Connect/Read are subclasses of TimeoutException, so check them first.
                if isinstance(exc, httpx.ConnectTimeout):
                    logger.error(
                        "Connect timeout on artifact upload attempt %d for %s: the "
                        "API did not establish a connection within %.1fs "
                        "(network/DNS/server-down). Exception %s: %r",
                        attempt + 1,
                        _safe_log_value(url),
                        _UPLOAD_TIMEOUT_SECONDS,
                        type(exc).__name__,
                        _safe_log_value(repr(exc)),
                    )
                elif isinstance(exc, httpx.ReadTimeout):
                    logger.error(
                        "Read timeout on artifact upload attempt %d for %s: the API "
                        "accepted the request but stopped sending data within %.1fs "
                        "(likely a stalled indexing call on the server). "
                        "Exception %s: %r",
                        attempt + 1,
                        _safe_log_value(url),
                        _UPLOAD_TIMEOUT_SECONDS,
                        type(exc).__name__,
                        _safe_log_value(repr(exc)),
                    )
                elif isinstance(exc, httpx.TimeoutException):
                    logger.error(
                        "Overall timeout on artifact upload attempt %d for %s: "
                        "request exceeded the %.1fs budget. Exception %s: %r",
                        attempt + 1,
                        _safe_log_value(url),
                        _UPLOAD_TIMEOUT_SECONDS,
                        type(exc).__name__,
                        _safe_log_value(repr(exc)),
                    )
                # Non-timeout transport errors (ConnectError, ReadError, ...) are
                # summarized by the retry warning below to preserve the retry trail.

            wait = 2**attempt
            logger.warning(
                "Artifact upload attempt %d failed for %s, retrying in %ds: %s: %r",
                attempt + 1,
                _safe_log_value(url),
                wait,
                type(last_exc).__name__,
                _safe_log_value(repr(last_exc)),
            )
            await asyncio.sleep(wait)

        raise RuntimeError(
            f"Artifact upload failed after {_UPLOAD_MAX_ATTEMPTS} attempts: {last_exc}"
        )

    async def report_stage(
        self,
        processor_job_id: str,
        stage: str,
        chunks_processed: int = 0,
        total_chunks: int = 0,
        failure_reason: str | None = None,
        metadata_json: str | None = None,
    ) -> None:
        """Report pipeline stage to the .NET API so jobs can be resumed.

        Calls PATCH /api/ingestion/jobs/by-run/{processorJobId}/status.
        The .NET API matches processor_job_id to DocIngestionRunId.
        Never raises — stage reporting failures are logged but do not halt the pipeline.
        """
        if not self.is_configured() or not processor_job_id:
            return
        try:
            # processor_job_id is always str(uuid.uuid4()) (base_processor.py); reject
            # anything else before it becomes a path segment in an outbound URL.
            # Use the canonical UUID string so braced/urn forms cannot alter the path.
            job_id = str(uuid.UUID(processor_job_id))
        except ValueError:
            logger.warning(
                "Stage report skipped: processor_job_id is not a UUID: %s",
                _safe_log_value(processor_job_id),
            )
            return

        # Authority comes only from the validated API base; path segment is the
        # canonical UUID. Typed join keeps host/scheme/port out of string concat.
        # Normalize to a directory base first: httpx.URL.join follows RFC 3986 and
        # replaces the final path segment when the base has no trailing slash
        # (e.g. .../base + api/... must become .../base/api/..., not .../api/...).
        base_url = httpx.URL(self._base_url)
        directory_base = base_url.copy_with(
            path=f"{base_url.path.rstrip('/')}/"
        )
        url = directory_base.join(
            f"api/ingestion/jobs/by-run/{job_id}/status"
        )
        payload = {
            "stage": stage,
            "chunksProcessed": chunks_processed,
            "totalChunks": total_chunks,
        }
        if failure_reason:
            payload["failureReason"] = failure_reason
        if metadata_json:
            payload["metadataJson"] = metadata_json

        try:
            token = await self._acquire_token_async()
            headers = {"Authorization": f"Bearer {token}"}
            async with create_api_https_async_client(timeout=30.0) as client:
                response = await client.patch(url, json=payload, headers=headers)
                if response.is_redirect:
                    require_non_redirect_success(response)
                if response.is_success:
                    logger.debug(
                        "Reported stage %s for processor job %s",
                        _safe_log_value(stage),
                        _safe_log_value(processor_job_id),  # codeql[py/log-injection]
                    )
                else:
                    logger.warning(
                        "Stage report failed for processor job %s: HTTP %s",
                        _safe_log_value(processor_job_id),  # codeql[py/log-injection]
                        response.status_code,
                    )
        except Exception as exc:
            logger.warning(
                "Stage report failed for processor job %s: %s",
                _safe_log_value(processor_job_id),  # codeql[py/log-injection]
                _safe_log_value(repr(exc)),
            )
