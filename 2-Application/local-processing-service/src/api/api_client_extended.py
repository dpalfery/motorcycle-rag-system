"""Extended API client with run and stage reporting methods.

This module extends the base ApiClient with methods for:
- Creating manual runs
- Updating run status
- Starting/completing/failing stages
- Registering finalized artifacts

Note: This should be merged into api_client.py once API contracts are finalized.
"""

import asyncio
import logging
import httpx
from typing import Optional

logger = logging.getLogger(__name__)


class ApiClientExtended:
    """Extended API client with run and stage reporting methods.

    This class provides methods for:
    - Creating manual processing runs
    - Updating run status
    - Managing stage lifecycle (start/complete/fail)
    - Registering finalized artifacts

    All methods are designed to work with the API contracts defined in:
    - POST /api/manual-runs
    - PATCH /api/manual-runs/{run_id}
    - POST /api/manual-runs/{run_id}/stages/{stage_name}/start
    - POST /api/manual-runs/{run_id}/stages/{stage_name}/complete
    - POST /api/manual-runs/{run_id}/stages/{stage_name}/fail
    - POST /api/manual-runs/{run_id}/artifacts
    """

    def __init__(self, base_client):
        """Initialize extended API client.

        Args:
            base_client: Base ApiClient instance
        """
        self._base_client = base_client

    @property
    def is_configured(self) -> bool:
        """Check if API client is configured."""
        return self._base_client.is_configured()

    async def create_manual_run(
        self,
        document_id: str,
        run_type: str = "manual-ingestion",
        started_from_stage: str = "01-source",
    ) -> dict:
        """Create a new processing run for a document.

        Args:
            document_id: Document identifier
            run_type: Type of run (e.g., "manual-ingestion", "graph-seeding")
            started_from_stage: Stage to start from (for resume)

        Returns:
            Run creation response from API

        Raises:
            RuntimeError: If API call fails or client not configured
        """
        if not self.is_configured:
            logger.warning(
                "ApiClient not configured — skipping run creation for %s.",
                document_id,
            )
            return {"run_id": None}

        token = await asyncio.to_thread(self._base_client._get_token)
        base_url = self._base_client._base_url
        url = f"{base_url}/api/manual-runs"
        headers = {"Authorization": f"Bearer {token}"}

        try:
            async with httpx.AsyncClient(timeout=60.0) as client:
                response = await client.post(
                    url,
                    headers=headers,
                    json={
                        "documentId": document_id,
                        "runType": run_type,
                        "startedFromStage": started_from_stage,
                    },
                )

            if response.status_code >= 400:
                error_msg = response.text.strip() if response.text else f"HTTP {response.status_code}"
                raise RuntimeError(f"Failed to create run: {error_msg}")

            return response.json()

        except httpx.HTTPError as exc:
            raise RuntimeError(f"HTTP error creating run: {exc}")

    async def update_run_status(
        self,
        run_id: str,
        status: str,
        completed_stage: Optional[str] = None,
        error_summary: Optional[str] = None,
    ) -> None:
        """Update run status.

        Args:
            run_id: Run identifier
            status: New status (e.g., "running", "completed", "failed")
            completed_stage: Optional last completed stage
            error_summary: Optional error summary

        Raises:
            RuntimeError: If API call fails or client not configured
        """
        if not self.is_configured:
            logger.warning(
                "ApiClient not configured — skipping run status update for %s.",
                run_id,
            )
            return

        token = await asyncio.to_thread(self._base_client._get_token)
        base_url = self._base_client._base_url
        url = f"{base_url}/api/manual-runs/{run_id}"
        headers = {"Authorization": f"Bearer {token}"}

        payload = {"status": status}
        if completed_stage is not None:
            payload["completedStage"] = completed_stage
        if error_summary is not None:
            payload["errorSummary"] = error_summary

        try:
            async with httpx.AsyncClient(timeout=60.0) as client:
                response = await client.patch(
                    url,
                    headers=headers,
                    json=payload,
                )

            if response.status_code >= 400:
                error_msg = response.text.strip() if response.text else f"HTTP {response.status_code}"
                raise RuntimeError(f"Failed to update run status: {error_msg}")

            logger.info("Updated run %s status to %s", run_id, status)

        except httpx.HTTPError as exc:
            raise RuntimeError(f"HTTP error updating run status: {exc}")

    async def start_stage(
        self,
        run_id: str,
        stage_name: str,
    ) -> dict:
        """Mark a stage as started.

        Args:
            run_id: Run identifier
            stage_name: Name of stage (e.g., "01-source", "02-parsed-document")

        Returns:
            Stage start response from API

        Raises:
            RuntimeError: If API call fails or client not configured
        """
        if not self.is_configured:
            logger.warning(
                "ApiClient not configured — skipping stage start for %s.",
                stage_name,
            )
            return {"stage_id": None}

        token = await asyncio.to_thread(self._base_client._get_token)
        base_url = self._base_client._base_url
        url = f"{base_url}/api/manual-runs/{run_id}/stages/{stage_name}/start"
        headers = {"Authorization": f"Bearer {token}"}

        try:
            async with httpx.AsyncClient(timeout=60.0) as client:
                response = await client.post(
                    url,
                    headers=headers,
                )

            if response.status_code >= 400:
                error_msg = response.text.strip() if response.text else f"HTTP {response.status_code}"
                raise RuntimeError(f"Failed to start stage: {error_msg}")

            logger.info("Started stage %s for run %s", stage_name, run_id)
            return response.json()

        except httpx.HTTPError as exc:
            raise RuntimeError(f"HTTP error starting stage: {exc}")

    async def complete_stage(
        self,
        run_id: str,
        stage_name: str,
        artifact_path: Optional[str] = None,
        artifact_hash: Optional[str] = None,
        metadata: Optional[dict] = None,
    ) -> dict:
        """Mark a stage as completed.

        Args:
            run_id: Run identifier
            stage_name: Name of stage
            artifact_path: Optional path to stage artifact
            artifact_hash: Optional hash of stage artifact
            metadata: Optional stage metadata

        Returns:
            Stage completion response from API

        Raises:
            RuntimeError: If API call fails or client not configured
        """
        if not self.is_configured:
            logger.warning(
                "ApiClient not configured — skipping stage completion for %s.",
                stage_name,
            )
            return {"stage_id": None}

        token = await asyncio.to_thread(self._base_client._get_token)
        base_url = self._base_client._base_url
        url = f"{base_url}/api/manual-runs/{run_id}/stages/{stage_name}/complete"
        headers = {"Authorization": f"Bearer {token}"}

        payload = {}
        if artifact_path is not None:
            payload["artifactPath"] = artifact_path
        if artifact_hash is not None:
            payload["artifactHash"] = artifact_hash
        if metadata is not None:
            payload["metadata"] = metadata

        try:
            async with httpx.AsyncClient(timeout=60.0) as client:
                response = await client.post(
                    url,
                    headers=headers,
                    json=payload,
                )

            if response.status_code >= 400:
                error_msg = response.text.strip() if response.text else f"HTTP {response.status_code}"
                raise RuntimeError(f"Failed to complete stage: {error_msg}")

            logger.info("Completed stage %s for run %s", stage_name, run_id)
            return response.json()

        except httpx.HTTPError as exc:
            raise RuntimeError(f"HTTP error completing stage: {exc}")

    async def fail_stage(
        self,
        run_id: str,
        stage_name: str,
        error_detail: str,
    ) -> dict:
        """Mark a stage as failed.

        Args:
            run_id: Run identifier
            stage_name: Name of stage
            error_detail: Error details

        Returns:
            Stage failure response from API

        Raises:
            RuntimeError: If API call fails or client not configured
        """
        if not self.is_configured:
            logger.warning(
                "ApiClient not configured — skipping stage failure for %s.",
                stage_name,
            )
            return {"stage_id": None}

        token = await asyncio.to_thread(self._base_client._get_token)
        base_url = self._base_client._base_url
        url = f"{base_url}/api/manual-runs/{run_id}/stages/{stage_name}/fail"
        headers = {"Authorization": f"Bearer {token}"}

        try:
            async with httpx.AsyncClient(timeout=60.0) as client:
                response = await client.post(
                    url,
                    headers=headers,
                    json={"errorDetail": error_detail},
                )

            if response.status_code >= 400:
                error_msg = response.text.strip() if response.text else f"HTTP {response.status_code}"
                raise RuntimeError(f"Failed to fail stage: {error_msg}")

            logger.warning("Failed stage %s for run %s: %s", stage_name, run_id, error_detail)
            return response.json()

        except httpx.HTTPError as exc:
            raise RuntimeError(f"HTTP error failing stage: {exc}")

    async def register_artifacts(
        self,
        run_id: str,
        chunk_count: Optional[int] = None,
        vector_count: Optional[int] = None,
        graph_entity_count: Optional[int] = None,
        graph_relation_count: Optional[int] = None,
        metadata: Optional[dict] = None,
    ) -> dict:
        """Register finalized artifacts for a run.

        Args:
            run_id: Run identifier
            chunk_count: Optional number of chunks
            vector_count: Optional number of vectors
            graph_entity_count: Optional number of graph entities
            graph_relation_count: Optional number of graph relations
            metadata: Optional additional metadata

        Returns:
            Artifact registration response from API

        Raises:
            RuntimeError: If API call fails or client not configured
        """
        if not self.is_configured:
            logger.warning(
                "ApiClient not configured — skipping artifact registration for %s.",
                run_id,
            )
            return {"artifact_id": None}

        token = await asyncio.to_thread(self._base_client._get_token)
        base_url = self._base_client._base_url
        url = f"{base_url}/api/manual-runs/{run_id}/artifacts"
        headers = {"Authorization": f"Bearer {token}"}

        payload = {}
        if chunk_count is not None:
            payload["chunkCount"] = chunk_count
        if vector_count is not None:
            payload["vectorCount"] = vector_count
        if graph_entity_count is not None:
            payload["graphEntityCount"] = graph_entity_count
        if graph_relation_count is not None:
            payload["graphRelationCount"] = graph_relation_count
        if metadata is not None:
            payload["metadata"] = metadata

        try:
            async with httpx.AsyncClient(timeout=60.0) as client:
                response = await client.post(
                    url,
                    headers=headers,
                    json=payload,
                )

            if response.status_code >= 400:
                error_msg = response.text.strip() if response.text else f"HTTP {response.status_code}"
                raise RuntimeError(f"Failed to register artifacts: {error_msg}")

            logger.info("Registered artifacts for run %s", run_id)
            return response.json()

        except httpx.HTTPError as exc:
            raise RuntimeError(f"HTTP error registering artifacts: {exc}")
