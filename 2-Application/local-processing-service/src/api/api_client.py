"""MSAL M2M client for uploading processed artifacts to the MotorcycleRAG API."""

import asyncio
import logging
import os

import httpx
import msal

logger = logging.getLogger(__name__)

_DEFAULT_TENANT_ID = "0f8f8a52-f135-43af-af88-e0b54ca9ff91"
_DEFAULT_CLIENT_ID = "d09d356d-62ac-4f38-b636-64169119ea25"
_DEFAULT_SCOPE = "api://motorcyclerag-api/.default"


class ApiClient:
    """Uploads processed artifacts to the MotorcycleRAG API via MSAL client credentials."""

    def __init__(self) -> None:
        secret = os.environ.get("PYTHON_UPLOAD_JOB_SECRET", "").strip()
        if not secret:
            self._configured = False
            logger.warning(
                "PYTHON_UPLOAD_JOB_SECRET not set — artifact upload via API is disabled. "
                "Processed files will not be sent to the API."
            )
            return

        tenant_id = os.environ.get("MCR_LOCAL_PROCESSOR_TENANT_ID", _DEFAULT_TENANT_ID)
        client_id = os.environ.get("MCR_LOCAL_PROCESSOR_CLIENT_ID", _DEFAULT_CLIENT_ID)
        scope = os.environ.get("MCR_API_SCOPE", _DEFAULT_SCOPE)
        authority = f"https://login.microsoftonline.com/{tenant_id}"

        self._scope = [scope]
        self._base_url = os.environ.get("MCR_API_BASE_URL", "https://localhost:5001").rstrip("/")
        self._msal_app = msal.ConfidentialClientApplication(
            client_id,
            authority=authority,
            client_credential=secret,
        )
        self._configured = True
        logger.info("ApiClient initialised (tenant=%s, client=%s).", tenant_id, client_id)

    def is_configured(self) -> bool:
        return self._configured

    def _get_token(self) -> str:
        result = self._msal_app.acquire_token_for_client(scopes=self._scope)
        if "access_token" not in result:
            raise RuntimeError(
                f"MSAL token acquisition failed: {result.get('error_description', result)}"
            )
        return result["access_token"]

    async def upload_artifact(
        self,
        data: bytes,
        upload_id: str,
        artifact_type: str,
        content_type: str,
    ) -> None:
        """POST processed artifact bytes to the API. No-op if not configured."""
        if not self._configured:
            logger.warning(
                "ApiClient not configured — skipping artifact upload for %s.", upload_id
            )
            return

        token = await asyncio.to_thread(self._get_token)
        filename = "chunks.jsonl" if artifact_type == "search-chunks" else "entities.json"
        url = (
            f"{self._base_url}/api/ingestion/artifacts/upload"
            f"?uploadId={upload_id}&artifactType={artifact_type}"
        )
        headers = {"Authorization": f"Bearer {token}"}
        last_exc: Exception | None = None

        for attempt in range(3):
            try:
                async with httpx.AsyncClient(timeout=120.0) as client:
                    response = await client.post(
                        url,
                        headers=headers,
                        files={"file": (filename, data, content_type)},
                    )
                if response.status_code < 500:
                    if response.is_success or response.status_code == 202:
                        logger.info(
                            "Uploaded %s artifact for upload %s.", artifact_type, upload_id
                        )
                        return
                    raise RuntimeError(
                        f"Artifact upload failed: HTTP {response.status_code}"
                    )
                last_exc = RuntimeError(f"Artifact upload server error: HTTP {response.status_code}")
            except httpx.TransportError as exc:
                last_exc = exc

            wait = 2 ** attempt
            logger.warning(
                "Artifact upload attempt %d failed, retrying in %ds: %s",
                attempt + 1,
                wait,
                last_exc,
            )
            await asyncio.sleep(wait)

        raise RuntimeError(f"Artifact upload failed after 3 attempts: {last_exc}")
