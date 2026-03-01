"""Azure Blob Storage writer with DefaultAzureCredential (primary) and connection string fallback."""

import os
import json
import logging
import asyncio

from azure.storage.blob import BlobServiceClient
from azure.identity import DefaultAzureCredential
from azure.core.exceptions import ResourceExistsError

logger = logging.getLogger(__name__)


class BlobWriter:
    """Reads and writes blobs to Azure Blob Storage.

    Authentication priority:
      1. DefaultAzureCredential via AZURE_STORAGE_ACCOUNT_URL (production / managed identity)
      2. AZURE_STORAGE_CONNECTION_STRING (local-dev fallback only)
      3. None — service starts but blob operations will fail gracefully
    """

    def __init__(self) -> None:
        account_url = os.getenv("AZURE_STORAGE_ACCOUNT_URL")
        conn_str = os.getenv("AZURE_STORAGE_CONNECTION_STRING")

        if account_url:
            credential = DefaultAzureCredential()
            self._client = BlobServiceClient(
                account_url=account_url, credential=credential
            )
            logger.info(
                "BlobWriter initialised with DefaultAzureCredential (account_url)."
            )
        elif conn_str:
            logger.warning(
                "Using AZURE_STORAGE_CONNECTION_STRING (local dev fallback). "
                "Use AZURE_STORAGE_ACCOUNT_URL with managed identity in production."
            )
            self._client = BlobServiceClient.from_connection_string(conn_str)
        else:
            self._client = None
            logger.warning(
                "No AZURE_STORAGE_ACCOUNT_URL or AZURE_STORAGE_CONNECTION_STRING configured. "
                "Blob operations will be unavailable."
            )

    # ------------------------------------------------------------------
    # Health
    # ------------------------------------------------------------------

    def is_connected(self) -> bool:
        """Return True when the storage account is reachable. Never raises."""
        if self._client is None:
            return False
        try:
            self._client.get_service_properties()
            return True
        except Exception:
            return False

    # ------------------------------------------------------------------
    # Downloads
    # ------------------------------------------------------------------

    async def download_blob(self, container: str, blob_name: str) -> bytes:
        """Download a blob and return its content as bytes."""
        if self._client is None:
            raise RuntimeError("BlobWriter has no storage client configured.")

        def _download() -> bytes:
            container_client = self._client.get_container_client(container)
            blob_client = container_client.get_blob_client(blob_name)
            return blob_client.download_blob().readall()

        return await asyncio.to_thread(_download)

    # ------------------------------------------------------------------
    # Uploads
    # ------------------------------------------------------------------

    async def upload_jsonl(
        self, container: str, blob_path: str, records: list[dict]
    ) -> None:
        """Upload *records* as newline-delimited JSON Lines (no array wrapper)."""
        if self._client is None:
            raise RuntimeError("BlobWriter has no storage client configured.")

        # Build JSON Lines: one JSON object per line, no array wrapper
        jsonl_content = "\n".join(json.dumps(r) for r in records)
        data = jsonl_content.encode("utf-8")

        await self._upload_bytes(container, blob_path, data)
        logger.info(
            "Uploaded %d records as JSONL to %s/%s", len(records), container, blob_path
        )

    async def upload_json(
        self, container: str, blob_path: str, data: dict | list
    ) -> None:
        """Upload a single JSON object or array."""
        if self._client is None:
            raise RuntimeError("BlobWriter has no storage client configured.")

        payload = json.dumps(data).encode("utf-8")
        await self._upload_bytes(container, blob_path, payload)
        logger.info("Uploaded JSON to %s/%s", container, blob_path)

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    async def _upload_bytes(self, container: str, blob_path: str, data: bytes) -> None:
        """Ensure container exists then upload raw bytes."""

        def _upload() -> None:
            # Auto-create container if it doesn't exist
            container_client = self._client.get_container_client(container)
            try:
                container_client.create_container()
            except ResourceExistsError:
                pass  # Container already exists — expected

            blob_client = container_client.get_blob_client(blob_path)
            blob_client.upload_blob(data, overwrite=True)

        await asyncio.to_thread(_upload)
