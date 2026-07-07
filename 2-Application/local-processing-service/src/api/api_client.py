"""MSAL M2M client for uploading processed artifacts to the MotorcycleRAG API."""

import asyncio
import base64
import json
import logging
import os
import time

import httpx
import msal

logger = logging.getLogger(__name__)

_DEFAULT_TENANT_ID = "0f8f8a52-f135-43af-af88-e0b54ca9ff91"
_DEFAULT_CLIENT_ID = "d09d356d-62ac-4f38-b636-64169119ea25"
_DEFAULT_SCOPE = "api://motorcyclerag-api/.default"
_DEFAULT_BASE_URL = "https://localhost:7215"


class ApiClient:
    """Uploads processed artifacts to the MotorcycleRAG API via MSAL client credentials."""

    def __init__(self) -> None:
        self._base_url = os.environ.get("MCR_API_BASE_URL", _DEFAULT_BASE_URL).rstrip("/")
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
            tenant_id,
            client_id,
            self._base_url,
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
            url = (
                f"{self._base_url}/api/ingestion/artifacts/source/access"
                f"?uploadId={upload_id}&documentType={document_type}"
                f"&accessToken={access_token}"
            )
            async with httpx.AsyncClient(timeout=300.0, verify=False) as client:
                response = await client.get(url)
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
        url = (
            f"{self._base_url}/api/ingestion/artifacts/source"
            f"?uploadId={upload_id}&documentType={document_type}"
        )
        headers = {"Authorization": f"Bearer {token}"}
        async with httpx.AsyncClient(timeout=300.0, verify=False) as client:
            response = await client.get(url, headers=headers)
        if not response.is_success:
            raise RuntimeError(
                f"Source download failed: HTTP {response.status_code} - {response.text[:500]}"
            )
        return response.content

    def _get_token(self) -> str:
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
        """POST processed artifact bytes to the API."""
        if not self._configured:
            raise RuntimeError(
                "ApiClient is not configured; cannot upload processed artifacts. "
                "Set PYTHON_UPLOAD_JOB_SECRET before starting the local processor."
            )

        token = await self._acquire_token_async()
        filename = "chunks.jsonl" if artifact_type == "search-chunks" else "entities.json"
        url = (
            f"{self._base_url}/api/ingestion/artifacts/upload"
            f"?uploadId={upload_id}&artifactType={artifact_type}"
        )
        headers = {"Authorization": f"Bearer {token}"}
        last_exc: Exception | None = None

        logger.info(
            "Starting artifact upload for upload %s (artifact_type=%s, bytes=%d, url=%s)",
            upload_id,
            artifact_type,
            len(data),
            url,
        )

        for attempt in range(3):
            try:
                logger.info(
                    "Artifact upload attempt %d for upload %s (artifact_type=%s)",
                    attempt + 1,
                    upload_id,
                    artifact_type,
                )
                async with httpx.AsyncClient(timeout=120.0, verify=False) as client:
                    response = await client.post(
                        url,
                        headers=headers,
                        files={"file": (filename, data, content_type)},
                    )
                logger.info(
                    "Artifact upload attempt %d for upload %s returned HTTP %d",
                    attempt + 1,
                    upload_id,
                    response.status_code,
                )
                if response.status_code < 500:
                    if response.is_success or response.status_code == 202:
                        logger.info(
                            "Uploaded %s artifact for upload %s.", artifact_type, upload_id
                        )
                        return
                    response_text = response.text.strip()
                    detail = f"HTTP {response.status_code}"
                    if response_text:
                        detail = f"{detail} - {response_text[:500]}"
                    if response.status_code in (401, 403):
                        logger.error(
                            "Artifact upload authorization failed for %s. Token diagnostics: %s",
                            url,
                            self._get_token_diagnostics(token),
                        )
                    raise RuntimeError(f"Artifact upload failed: {detail}")
                response_text = response.text.strip()
                detail = f"Artifact upload server error: HTTP {response.status_code}"
                if response_text:
                    detail = f"{detail} - {response_text[:500]}"
                last_exc = RuntimeError(detail)
            except httpx.HTTPError as exc:
                last_exc = exc

            wait = 2 ** attempt
            logger.warning(
                "Artifact upload attempt %d failed for %s, retrying in %ds: %s: %r",
                attempt + 1,
                url,
                wait,
                type(last_exc).__name__,
                last_exc,
            )
            await asyncio.sleep(wait)

        raise RuntimeError(f"Artifact upload failed after 3 attempts: {last_exc}")

    async def report_stage(
        self,
        processor_job_id: str,
        stage: str,
        chunks_processed: int = 0,
        total_chunks: int = 0,
        failure_reason: str | None = None,
    ) -> None:
        """Report pipeline stage to the .NET API so jobs can be resumed.

        Calls PATCH /api/ingestion/jobs/by-run/{processorJobId}/status.
        The .NET API matches processor_job_id to DocIngestionRunId.
        Never raises — stage reporting failures are logged but do not halt the pipeline.
        """
        if not self.is_configured() or not processor_job_id:
            return

        url = f"{self._base_url}/api/ingestion/jobs/by-run/{processor_job_id}/status"
        payload = {
            "stage": stage,
            "chunksProcessed": chunks_processed,
            "totalChunks": total_chunks,
        }
        if failure_reason:
            payload["failureReason"] = failure_reason

        try:
            token = await self._acquire_token_async()
            headers = {"Authorization": f"Bearer {token}"}
            async with httpx.AsyncClient(timeout=30.0, verify=False) as client:
                response = await client.patch(url, json=payload, headers=headers)
                if response.status_code < 400:
                    logger.debug("Reported stage %s for processor job %s", stage, processor_job_id)
                else:
                    logger.warning(
                        "Stage report failed for processor job %s: HTTP %s",
                        processor_job_id,
                        response.status_code,
                    )
        except Exception as exc:
            logger.warning("Stage report failed for processor job %s: %s", processor_job_id, exc)
