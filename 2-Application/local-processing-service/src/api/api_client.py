"""MSAL M2M client for uploading processed artifacts to the MotorcycleRAG API."""

import asyncio
import base64
import json
import logging
import os

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
        self._base_url = os.environ.get("MCR_API_BASE_URL", _DEFAULT_BASE_URL).rstrip("/")
        self._msal_app = msal.ConfidentialClientApplication(
            client_id,
            authority=authority,
            client_credential=secret,
        )
        self._configured = True
        logger.info(
            "ApiClient initialised (tenant=%s, client=%s, base_url=%s).",
            tenant_id,
            client_id,
            self._base_url,
        )

    def is_configured(self) -> bool:
        return self._configured

    def _get_token(self) -> str:
        result = self._msal_app.acquire_token_for_client(scopes=self._scope)
        if "access_token" not in result:
            raise RuntimeError(
                f"MSAL token acquisition failed: {result.get('error_description', result)}"
            )
        return result["access_token"]

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
                async with httpx.AsyncClient(timeout=120.0) as client:
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
