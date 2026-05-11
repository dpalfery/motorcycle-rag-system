"""Shared stage runner and checkpoint infrastructure for processing workflows.

This module provides the foundation for restartable, checkpointed processing
across both ManualIngestionProcessor and GraphSeedProcessor workflows.
"""

import asyncio
import json
import logging
import os
from abc import ABC, abstractmethod
from dataclasses import dataclass, field, asdict
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path
from typing import Any, Callable, Optional

logger = logging.getLogger(__name__)


class StageStatus(str, Enum):
    """Status of a processing stage."""
    PENDING = "pending"
    IN_PROGRESS = "in_progress"
    COMPLETED = "completed"
    FAILED = "failed"
    SKIPPED = "skipped"


class RunStatus(str, Enum):
    """Status of a processing run."""
    CREATED = "created"
    RUNNING = "running"
    COMPLETED = "completed"
    FAILED = "failed"
    CANCELLED = "cancelled"


@dataclass
class StageMetadata:
    """Metadata for a processing stage checkpoint."""
    stage_name: str
    status: StageStatus
    started_at: Optional[str] = None
    completed_at: Optional[str] = None
    artifact_path: Optional[str] = None
    artifact_hash: Optional[str] = None
    metadata_json: Optional[dict] = None
    error_detail: Optional[str] = None
    retry_count: int = 0

    def to_dict(self) -> dict:
        """Convert to dictionary for serialization."""
        return {
            "stage_name": self.stage_name,
            "status": self.status.value,
            "started_at": self.started_at,
            "completed_at": self.completed_at,
            "artifact_path": self.artifact_path,
            "artifact_hash": self.artifact_hash,
            "metadata": self.metadata_json,
            "error_detail": self.error_detail,
            "retry_count": self.retry_count,
        }

    @classmethod
    def from_dict(cls, data: dict) -> "StageMetadata":
        """Create from dictionary."""
        return cls(
            stage_name=data["stage_name"],
            status=StageStatus(data["status"]),
            started_at=data.get("started_at"),
            completed_at=data.get("completed_at"),
            artifact_path=data.get("artifact_path"),
            artifact_hash=data.get("artifact_hash"),
            metadata_json=data.get("metadata"),
            error_detail=data.get("error_detail"),
            retry_count=data.get("retry_count", 0),
        )


@dataclass
class RunMetadata:
    """Metadata for a processing run checkpoint."""
    run_id: str
    document_id: Optional[str]
    run_type: str
    status: RunStatus
    started_at: str
    completed_at: Optional[str] = None
    started_from_stage: str = "01-source"
    completed_stage: Optional[str] = None
    local_working_folder: Optional[str] = None
    processor_host: Optional[str] = None
    error_summary: Optional[str] = None
    stages: dict[str, StageMetadata] = field(default_factory=dict)

    def to_dict(self) -> dict:
        """Convert to dictionary for serialization."""
        return {
            "run_id": self.run_id,
            "document_id": self.document_id,
            "run_type": self.run_type,
            "status": self.status.value,
            "started_at": self.started_at,
            "completed_at": self.completed_at,
            "started_from_stage": self.started_from_stage,
            "completed_stage": self.completed_stage,
            "local_working_folder": self.local_working_folder,
            "processor_host": self.processor_host,
            "error_summary": self.error_summary,
            "stages": {
                stage_name: stage.to_dict()
                for stage_name, stage in self.stages.items()
            },
        }

    @classmethod
    def from_dict(cls, data: dict) -> "RunMetadata":
        """Create from dictionary."""
        return cls(
            run_id=data["run_id"],
            document_id=data.get("document_id"),
            run_type=data["run_type"],
            status=RunStatus(data["status"]),
            started_at=data["started_at"],
            completed_at=data.get("completed_at"),
            started_from_stage=data.get("started_from_stage", "01-source"),
            completed_stage=data.get("completed_stage"),
            local_working_folder=data.get("local_working_folder"),
            processor_host=data.get("processor_host"),
            error_summary=data.get("error_summary"),
            stages={
                stage_name: StageMetadata.from_dict(stage_data)
                for stage_name, stage_data in data.get("stages", {}).items()
            },
        )


class StageRunner:
    """Manages stage execution with checkpointing and resume capability.

    The StageRunner provides:
    - Stage execution with automatic checkpointing
    - Resume from last successful stage
    - Retry capability for failed stages
    - Local artifact persistence
    - Metadata tracking for provenance
    """

    # Standard stage names for processing workflows
    STAGE_SOURCE = "01-source"
    STAGE_PARSED_DOCUMENT = "02-parsed-document"
    STAGE_CHUNKS = "03-chunks"
    STAGE_EMBEDDINGS = "04-embeddings"
    STAGE_GRAPH_CANDIDATES = "05-graph-candidates"
    STAGE_UPLOAD_ARTIFACTS = "06-upload-artifacts"
    STAGE_COMPLETE = "07-complete"

    def __init__(
        self,
        run_id: str,
        working_dir: Path,
        stage_order: list[str],
    ):
        """Initialize the stage runner.

        Args:
            run_id: Unique identifier for this run
            working_dir: Base directory for stage artifacts
            stage_order: Ordered list of stage names to execute
        """
        self._run_id = run_id
        self._working_dir = Path(working_dir)
        self._stage_order = stage_order
        self._stages: dict[str, StageMetadata] = {}
        self._run_metadata: Optional[RunMetadata] = None

        # Ensure working directory exists
        self._working_dir.mkdir(parents=True, exist_ok=True)

        # Create stage directories
        for stage_name in stage_order:
            (self._working_dir / stage_name).mkdir(exist_ok=True)

    @property
    def run_id(self) -> str:
        """Get the run ID."""
        return self._run_id

    @property
    def working_dir(self) -> Path:
        """Get the working directory."""
        return self._working_dir

    def get_stage_dir(self, stage_name: str) -> Path:
        """Get the directory for a specific stage."""
        return self._working_dir / stage_name

    def load_checkpoint(self) -> Optional[RunMetadata]:
        """Load existing checkpoint from working directory.

        Returns:
            RunMetadata if checkpoint exists, None otherwise
        """
        checkpoint_path = self._working_dir / "run-metadata.json"
        if not checkpoint_path.exists():
            return None

        try:
            with open(checkpoint_path, "r", encoding="utf-8") as f:
                data = json.load(f)
            self._run_metadata = RunMetadata.from_dict(data)
            self._stages = self._run_metadata.stages
            logger.info(
                "Loaded checkpoint for run %s from %s",
                self._run_id,
                checkpoint_path,
            )
            return self._run_metadata
        except Exception as e:
            logger.warning(
                "Failed to load checkpoint from %s: %s",
                checkpoint_path,
                e,
            )
            return None

    def save_checkpoint(self, run_metadata: RunMetadata) -> None:
        """Save checkpoint to working directory.

        Args:
            run_metadata: Current run metadata to persist
        """
        checkpoint_path = self._working_dir / "run-metadata.json"
        self._run_metadata = run_metadata
        self._stages = run_metadata.stages

        try:
            with open(checkpoint_path, "w", encoding="utf-8") as f:
                json.dump(run_metadata.to_dict(), f, indent=2, default=str)
            logger.debug("Saved checkpoint for run %s", self._run_id)
        except Exception as e:
            logger.error(
                "Failed to save checkpoint to %s: %s",
                checkpoint_path,
                e,
            )

    def get_stage_status(self, stage_name: str) -> StageStatus:
        """Get the status of a stage.

        Args:
            stage_name: Name of the stage

        Returns:
            StageStatus of the stage
        """
        if stage_name not in self._stages:
            return StageStatus.PENDING
        return self._stages[stage_name].status

    def is_stage_complete(self, stage_name: str) -> bool:
        """Check if a stage has completed successfully.

        Args:
            stage_name: Name of the stage

        Returns:
            True if stage is completed, False otherwise
        """
        return self.get_stage_status(stage_name) == StageStatus.COMPLETED

    def get_next_stage(self) -> Optional[str]:
        """Get the next stage to execute based on checkpoint.

        Returns:
            Name of next stage, or None if all stages complete
        """
        for stage_name in self._stage_order:
            if not self.is_stage_complete(stage_name):
                return stage_name
        return None

    async def run_stage(
        self,
        stage_name: str,
        stage_func: Callable[[], Any],
        metadata: Optional[dict] = None,
        retry_on_failure: bool = False,
        max_retries: int = 3,
    ) -> Any:
        """Execute a stage with checkpointing.

        Args:
            stage_name: Name of the stage
            stage_func: Async function to execute for this stage
            metadata: Optional metadata to attach to stage
            retry_on_failure: Whether to retry on failure
            max_retries: Maximum number of retry attempts

        Returns:
            Result from stage_func

        Raises:
            Exception: If stage fails and retries exhausted
        """
        # Skip if already completed
        if self.is_stage_complete(stage_name):
            logger.info("Stage %s already completed, skipping", stage_name)
            return None

        stage_dir = self.get_stage_dir(stage_name)
        stage_metadata = self._stages.get(
            stage_name,
            StageMetadata(stage_name=stage_name, status=StageStatus.PENDING),
        )

        retry_count = stage_metadata.retry_count
        last_error = None

        while retry_count <= max_retries:
            try:
                # Update stage to in_progress
                stage_metadata.status = StageStatus.IN_PROGRESS
                stage_metadata.started_at = datetime.now(timezone.utc).isoformat()
                stage_metadata.retry_count = retry_count
                stage_metadata.error_detail = None
                self._stages[stage_name] = stage_metadata

                # Execute stage
                logger.info("Executing stage %s (attempt %d)", stage_name, retry_count + 1)
                result = await stage_func()

                # Mark stage as completed
                stage_metadata.status = StageStatus.COMPLETED
                stage_metadata.completed_at = datetime.now(timezone.utc).isoformat()
                stage_metadata.metadata_json = metadata
                self._stages[stage_name] = stage_metadata

                logger.info("Stage %s completed successfully", stage_name)
                return result

            except Exception as e:
                last_error = e
                logger.error(
                    "Stage %s failed (attempt %d): %s",
                    stage_name,
                    retry_count + 1,
                    e,
                )

                stage_metadata.error_detail = str(e)
                self._stages[stage_name] = stage_metadata

                if retry_on_failure and retry_count < max_retries:
                    retry_count += 1
                    stage_metadata.retry_count = retry_count
                    await asyncio.sleep(1)  # Brief delay before retry
                else:
                    # Mark stage as failed
                    stage_metadata.status = StageStatus.FAILED
                    stage_metadata.completed_at = datetime.now(timezone.utc).isoformat()
                    self._stages[stage_name] = stage_metadata
                    raise

        # This should not be reached, but just in case
        raise last_error if last_error else RuntimeError("Stage execution failed")

    async def run_pipeline(
        self,
        stages: dict[str, Callable[[], Any]],
        start_from: Optional[str] = None,
    ) -> dict[str, Any]:
        """Execute the complete pipeline of stages.

        Args:
            stages: Dictionary mapping stage names to async functions
            start_from: Optional stage name to start from (for resume)

        Returns:
            Dictionary mapping stage names to results

        Raises:
            Exception: If any stage fails
        """
        results: dict[str, Any] = {}

        # Determine starting point
        if start_from:
            start_index = self._stage_order.index(start_from)
            stages_to_run = self._stage_order[start_index:]
        else:
            stages_to_run = self._stage_order

        for stage_name in stages_to_run:
            if stage_name not in stages:
                logger.warning("Stage %s not provided, skipping", stage_name)
                continue

            result = await self.run_stage(
                stage_name,
                stages[stage_name],
            )
            results[stage_name] = result

        return results

    def get_run_summary(self) -> dict:
        """Get a summary of the current run state.

        Returns:
            Dictionary with run summary information
        """
        completed = sum(
            1 for stage in self._stages.values()
            if stage.status == StageStatus.COMPLETED
        )
        failed = sum(
            1 for stage in self._stages.values()
            if stage.status == StageStatus.FAILED
        )
        pending = sum(
            1 for stage in self._stages.values()
            if stage.status == StageStatus.PENDING
        )

        return {
            "run_id": self._run_id,
            "working_dir": str(self._working_dir),
            "total_stages": len(self._stage_order),
            "completed": completed,
            "failed": failed,
            "pending": pending,
            "next_stage": self.get_next_stage(),
            "stages": {
                name: stage.to_dict()
                for name, stage in self._stages.items()
            },
        }
