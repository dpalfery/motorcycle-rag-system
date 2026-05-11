"""Base processor class for workflow processors.

Provides common functionality for job tracking, status management,
and API client integration across ManualIngestionProcessor and GraphSeedProcessor.
"""

import asyncio
import logging
import uuid
from abc import ABC, abstractmethod
from datetime import datetime, timezone
from typing import Any, Optional

from api.api_client import ApiClient

logger = logging.getLogger(__name__)

_ACTIVE_JOB_STATUSES = {"queued", "processing", "running", "inprogress"}


class BaseProcessor(ABC):
    """Base class for processing workflows.

    Provides:
    - Job tracking with in-memory job registry
    - Status management
    - API client integration
    - Common job lifecycle methods
    """

    def __init__(self, api_client: ApiClient):
        """Initialize base processor.

        Args:
            api_client: API client for reporting status and artifacts
        """
        self._api_client = api_client
        self._jobs: dict[str, dict] = {}

    @abstractmethod
    async def process_async(
        self,
        upload_id: str,
        **kwargs,
    ) -> str:
        """Process a job asynchronously.

        Args:
            upload_id: Unique identifier for the upload
            **kwargs: Additional processor-specific arguments

        Returns:
            job_id: Unique identifier for the processing job
        """
        pass

    async def get_job_status(self, job_id: str) -> Optional[dict]:
        """Get status of a processing job.

        Args:
            job_id: Job identifier

        Returns:
            Job status dict or None if not found
        """
        return self._jobs.get(job_id)

    async def list_jobs(self) -> list[dict]:
        """List all known jobs.

        Returns:
            List of job dictionaries
        """
        return list(self._jobs.values())

    async def clear_terminal_jobs(self) -> int:
        """Clear completed and failed jobs from memory.

        Returns:
            Number of jobs cleared
        """
        terminal_job_ids = [
            job_id
            for job_id, job in self._jobs.items()
            if str(job.get("status", "")).strip().lower() not in _ACTIVE_JOB_STATUSES
        ]

        for job_id in terminal_job_ids:
            self._jobs.pop(job_id, None)

        return len(terminal_job_ids)

    def _create_job(
        self,
        upload_id: str,
        job_type: str,
        metadata: Optional[dict] = None,
    ) -> tuple[str, dict]:
        """Create a new job entry.

        Args:
            upload_id: Upload identifier
            job_type: Type of job (e.g., "manual-ingestion", "graph-seeding")
            metadata: Optional metadata dictionary

        Returns:
            Tuple of (job_id, job_dict)
        """
        job_id = str(uuid.uuid4())
        now = datetime.now(timezone.utc).isoformat()

        job_dict = {
            "job_id": job_id,
            "upload_id": upload_id,
            "job_type": job_type,
            "status": "processing",
            "message": f"{job_type} processing started",
            "progress": 0.0,
            "created_at": now,
            "updated_at": now,
        }

        if metadata:
            job_dict.update(metadata)

        self._jobs[job_id] = job_dict
        return job_id, job_dict

    def _update_job_status(
        self,
        job_id: str,
        status: str,
        message: Optional[str] = None,
        progress: Optional[float] = None,
        **kwargs,
    ) -> None:
        """Update job status.

        Args:
            job_id: Job identifier
            status: New status value
            message: Optional status message
            progress: Optional progress value (0-100)
            **kwargs: Additional fields to update
        """
        if job_id not in self._jobs:
            logger.warning("Job %s not found for status update", job_id)
            return

        job = self._jobs[job_id]
        job["status"] = status
        job["updated_at"] = datetime.now(timezone.utc).isoformat()

        if message is not None:
            job["message"] = message

        if progress is not None:
            job["progress"] = progress

        job.update(kwargs)

    def _mark_job_failed(
        self,
        job_id: str,
        error: str,
    ) -> None:
        """Mark a job as failed.

        Args:
            job_id: Job identifier
            error: Error message
        """
        self._update_job_status(
            job_id,
            status="failed",
            message=f"Processing failed: {error}",
            error=error,
        )
        logger.error("Job %s failed: %s", job_id, error)

    def _mark_job_completed(
        self,
        job_id: str,
        message: str = "Processing completed successfully",
        **kwargs,
    ) -> None:
        """Mark a job as completed.

        Args:
            job_id: Job identifier
            message: Completion message
            **kwargs: Additional fields to update
        """
        self._update_job_status(
            job_id,
            status="completed",
            message=message,
            progress=100.0,
            **kwargs,
        )
        logger.info("Job %s completed: %s", job_id, message)

    @property
    def api_client(self) -> ApiClient:
        """Get the API client."""
        return self._api_client

    @property
    def is_api_configured(self) -> bool:
        """Check if API client is configured."""
        return self._api_client.is_configured()
