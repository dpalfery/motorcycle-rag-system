"""Azure AI Search direct-upload client (merge_or_upload, batches of 100)."""

import logging
import os
from typing import Any

from azure.core.credentials import AzureKeyCredential
from azure.identity import DefaultAzureCredential
from azure.search.documents import SearchClient

logger = logging.getLogger(__name__)

_BATCH_SIZE = 100


class AzureSearchDirectUploader:
    """Pushes document dicts to Azure AI Search via merge_or_upload.

    Reads from env:
        AZURE_SEARCH_ENDPOINT – https://<service>.search.windows.net (REQUIRED)
        AZURE_SEARCH_INDEX    – index name (default: motorcycle-index)
        AZURE_SEARCH_KEY      – admin key; if absent, DefaultAzureCredential is used

    If AZURE_SEARCH_ENDPOINT is not set:
        - logs a WARNING
        - upload() is a no-op (returns immediately, does NOT raise)
        - This enables graceful degradation when running without Azure Search configured.
    """

    def __init__(self) -> None:
        endpoint = os.getenv("AZURE_SEARCH_ENDPOINT", "")
        self._enabled = bool(endpoint)

        if not self._enabled:
            logger.warning(
                "AZURE_SEARCH_ENDPOINT not set — direct Azure AI Search upload disabled. "
                "Documents will only be written to blob storage."
            )
            self._client = None
            return

        index_name = os.getenv("AZURE_SEARCH_INDEX", "motorcycle-index")
        api_key = os.getenv("AZURE_SEARCH_KEY", "")

        credential = (
            AzureKeyCredential(api_key) if api_key else DefaultAzureCredential()
        )
        self._client = SearchClient(
            endpoint=endpoint,
            index_name=index_name,
            credential=credential,
        )
        logger.info(
            "AzureSearchDirectUploader initialised: endpoint=%s index=%s auth=%s",
            endpoint,
            index_name,
            "api_key" if api_key else "DefaultAzureCredential",
        )

    def upload(self, documents: list[dict[str, Any]]) -> None:
        """Upload documents to Azure AI Search in batches of 100.

        Uses merge_or_upload_documents (idempotent upserts).
        Logs errors per batch but does NOT raise — allows pipeline to continue.

        If the uploader is disabled (AZURE_SEARCH_ENDPOINT not set), this is a no-op.
        """
        if not self._enabled:
            return

        total = len(documents)
        uploaded = 0

        for i in range(0, total, _BATCH_SIZE):
            batch = documents[i : i + _BATCH_SIZE]
            try:
                result = self._client.merge_or_upload_documents(documents=batch)
                succeeded = sum(1 for r in result if r.succeeded)
                uploaded += succeeded
                logger.info(
                    "Uploaded batch %d/%d: %d/%d documents succeeded",
                    i // _BATCH_SIZE + 1,
                    (total + _BATCH_SIZE - 1) // _BATCH_SIZE,
                    succeeded,
                    len(batch),
                )
            except Exception as exc:
                logger.error(
                    "Failed to upload batch %d-%d to Azure AI Search: %s",
                    i,
                    i + len(batch),
                    exc,
                )

        logger.info(
            "Azure AI Search upload complete: %d/%d documents uploaded", uploaded, total
        )
