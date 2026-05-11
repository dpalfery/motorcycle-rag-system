"""Graph seeding processor for CSV-based motorcycle spec import.

Processes CSV files containing motorcycle specifications into graph nodes
and edges without using LLM or embeddings - purely deterministic processing.
"""

import asyncio
import hashlib
import json
import logging
import os
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional

import pandas as pd

from api.api_client import ApiClient
from infrastructure.base_processor import BaseProcessor
from infrastructure.stage_runner import (
    StageRunner,
    StageStatus,
    RunStatus,
    RunMetadata,
)
from storage.blob_writer import BlobWriter

logger = logging.getLogger(__name__)

# Deterministic UUID namespace — stable across runs so re-processing is idempotent.
_NAMESPACE = uuid.UUID("6ba7b810-9dad-11d1-80b4-00c04fd430c8")

# Spec columns included in bike-node descriptions (lowercased for lookup).
_DESCRIPTION_COLS = [
    "category",
    "engine type",
    "displacement ccm",
    "power hp",
    "torque nm",
    "top speed km/h",
    "gearbox",
    "cooling system",
]

# Stage names for graph seeding
GRAPH_SEEDING_STAGES = [
    StageRunner.STAGE_SOURCE,
    StageRunner.STAGE_GRAPH_CANDIDATES,
    StageRunner.STAGE_UPLOAD_ARTIFACTS,
    StageRunner.STAGE_COMPLETE,
]


def _node_id(seed: str) -> str:
    """Return a deterministic UUID string derived from *seed*."""
    return str(uuid.uuid5(_NAMESPACE, seed))


def _split_make(model_field: str) -> tuple[str, str]:
    """Return (make, rest_of_model) by splitting on first space.

    Examples:
        "Aprilia RS 660"  -> ("Aprilia", "RS 660")
        "AJP PR7"         -> ("AJP", "PR7")
        "Honda"           -> ("Honda", "Honda")
    """
    parts = model_field.strip().split(None, 1)
    if len(parts) == 1:
        return parts[0], parts[0]
    return parts[0], parts[1]


def _build_description(row: pd.Series, lower_col_map: dict[str, str]) -> str:
    """Build a concise spec description from selected columns."""
    parts = []
    for col_lower in _DESCRIPTION_COLS:
        orig = lower_col_map.get(col_lower)
        if orig is None:
            continue
        val = row.get(orig)
        if val is not None and pd.notna(val) and str(val).strip() not in ("", "0"):
            parts.append(f"{orig}: {val}")
    return "; ".join(parts)


class GraphSeedProcessor(BaseProcessor):
    """Processor for CSV-based motorcycle spec graph seeding.

    This processor:
    - Reads CSV from blob storage or local file
    - Converts specs to graph nodes (Motorcycle, Category, EngineType)
    - Creates relationships (BELONGS_TO, HAS_ENGINE_TYPE)
    - Uploads graph artifacts via API
    - Maintains restartable checkpointed state
    """

    def __init__(
        self,
        blob_writer: BlobWriter,
        api_client: ApiClient,
        working_base_dir: Optional[Path] = None,
    ):
        """Initialize graph seed processor.

        Args:
            blob_writer: Blob storage writer for downloading source
            api_client: API client for reporting and uploads
            working_base_dir: Base directory for working files (default: ./working)
        """
        super().__init__(api_client)

        self._blob_writer = blob_writer

        # Working directory for stage artifacts
        if working_base_dir is None:
            working_base_dir = Path("./working")
        self._working_base_dir = Path(working_base_dir)

        # Active stage runners keyed by job_id
        self._stage_runners: dict[str, StageRunner] = {}

    async def process_async(
        self,
        upload_id: str,
        blob_container: Optional[str] = None,
        local_file_path: Optional[str] = None,
    ) -> str:
        """Process a graph seeding job asynchronously.

        Args:
            upload_id: Upload identifier from API
            blob_container: Blob container containing source CSV
            local_file_path: Local path to CSV file (alternative to blob)

        Returns:
            job_id: Unique identifier for this job
        """
        # Validate inputs
        if not blob_container and not local_file_path:
            raise ValueError("Either blob_container or local_file_path must be provided")

        # Create job entry
        job_id, job_dict = self._create_job(
            upload_id=upload_id,
            job_type="graph-seeding",
            metadata={
                "blob_container": blob_container,
                "local_file_path": local_file_path,
            },
        )

        # Create working directory for this job
        job_working_dir = self._working_base_dir / job_id
        job_working_dir.mkdir(parents=True, exist_ok=True)

        # Create stage runner
        stage_runner = StageRunner(
            run_id=job_id,
            working_dir=job_working_dir,
            stage_order=GRAPH_SEEDING_STAGES,
        )
        self._stage_runners[job_id] = stage_runner

        # Load existing checkpoint if available
        checkpoint = stage_runner.load_checkpoint()
        if checkpoint:
            logger.info("Resuming job %s from checkpoint", job_id)
            self._update_job_status(
                job_id,
                status="processing",
                message=f"Resuming from stage: {checkpoint.completed_stage or 'start'}",
            )
        else:
            # Initialize new run metadata
            run_metadata = RunMetadata(
                run_id=job_id,
                document_id=upload_id,
                run_type="graph-seeding",
                status=RunStatus.RUNNING,
                started_at=datetime.now(timezone.utc).isoformat(),
                started_from_stage=StageRunner.STAGE_SOURCE,
                local_working_folder=str(job_working_dir),
                processor_host=os.getenv("HOSTNAME", "localhost"),
            )
            stage_runner.save_checkpoint(run_metadata)

        # Start background processing
        asyncio.create_task(
            self._process_graph_seeding(
                job_id=job_id,
                upload_id=upload_id,
                blob_container=blob_container,
                local_file_path=local_file_path,
            )
        )

        return job_id

    async def _process_graph_seeding(
        self,
        job_id: str,
        upload_id: str,
        blob_container: Optional[str],
        local_file_path: Optional[str],
    ) -> None:
        """Background coroutine that processes graph seeding through all stages.

        Args:
            job_id: Job identifier
            upload_id: Upload identifier
            blob_container: Blob container name
            local_file_path: Local file path
        """
        stage_runner = self._stage_runners.get(job_id)
        if not stage_runner:
            logger.error("Stage runner not found for job %s", job_id)
            self._mark_job_failed(job_id, "Stage runner not initialized")
            return

        try:
            # Define stage functions
            stages = {
                StageRunner.STAGE_SOURCE: lambda: self._stage_download_source(
                    job_id, blob_container, local_file_path
                ),
                StageRunner.STAGE_GRAPH_CANDIDATES: lambda: self._stage_build_graph(
                    job_id, upload_id
                ),
                StageRunner.STAGE_UPLOAD_ARTIFACTS: lambda: self._stage_upload_artifacts(
                    job_id, upload_id
                ),
                StageRunner.STAGE_COMPLETE: lambda: self._stage_complete(job_id),
            }

            # Execute pipeline from next stage
            next_stage = stage_runner.get_next_stage()
            if next_stage is None:
                logger.info("All stages already completed for job %s", job_id)
                self._mark_job_completed(job_id, "All stages already completed")
                return

            # Run pipeline
            results = await stage_runner.run_pipeline(
                stages=stages,
                start_from=next_stage,
            )

            # Update final run metadata
            checkpoint = stage_runner.load_checkpoint()
            if checkpoint:
                checkpoint.status = RunStatus.COMPLETED
                checkpoint.completed_at = datetime.now(timezone.utc).isoformat()
                checkpoint.completed_stage = StageRunner.STAGE_COMPLETE
                stage_runner.save_checkpoint(checkpoint)

            self._mark_job_completed(
                job_id,
                message="Graph seeding completed successfully",
            )

        except Exception as e:
            logger.exception("Graph seeding failed for job %s", job_id)
            self._mark_job_failed(job_id, str(e))

            # Update checkpoint with failure
            checkpoint = stage_runner.load_checkpoint()
            if checkpoint:
                checkpoint.status = RunStatus.FAILED
                checkpoint.completed_at = datetime.now(timezone.utc).isoformat()
                checkpoint.error_summary = str(e)
                stage_runner.save_checkpoint(checkpoint)

    async def _stage_download_source(
        self,
        job_id: str,
        blob_container: Optional[str],
        local_file_path: Optional[str],
    ) -> Path:
        """Stage 01: Download CSV from blob storage or use local file.

        Args:
            job_id: Job identifier
            blob_container: Blob container name
            local_file_path: Local file path

        Returns:
            Path to CSV file

        Raises:
            Exception: If download or file read fails
        """
        stage_runner = self._stage_runners[job_id]

        self._update_job_status(
            job_id,
            status="processing",
            message="Loading CSV source",
            progress=10.0,
        )

        stage_dir = stage_runner.get_stage_dir(StageRunner.STAGE_SOURCE)

        if local_file_path:
            # Use local file
            import shutil
            csv_path = stage_dir / "source.csv"
            shutil.copy(local_file_path, csv_path)

            # Calculate hash
            with open(local_file_path, "rb") as f:
                file_hash = hashlib.sha256(f.read()).hexdigest()

        else:
            # Download from blob
            if not blob_container:
                raise RuntimeError("blob_container is required for download")
            csv_bytes = await self._blob_writer.download_blob(
                blob_container, f"{job_id}.csv"
            )

            if not csv_bytes:
                raise RuntimeError(f"Failed to download CSV from {blob_container}/{job_id}.csv")

            csv_path = stage_dir / "source.csv"
            with open(csv_path, "wb") as f:
                f.write(csv_bytes)

            # Calculate hash
            file_hash = hashlib.sha256(csv_bytes).hexdigest()

        # Update stage metadata
        stage_metadata = stage_runner._stages.get(StageRunner.STAGE_SOURCE)
        if stage_metadata:
            stage_metadata.artifact_path = str(csv_path)
            stage_metadata.artifact_hash = file_hash
            stage_metadata.metadata_json = {
                "upload_id": job_id,
                "blob_container": blob_container,
                "local_file_path": local_file_path,
            }

        logger.info("Loaded CSV to %s (hash: %s)", csv_path, file_hash[:16])
        return csv_path

    async def _stage_build_graph(
        self,
        job_id: str,
        upload_id: str,
    ) -> dict:
        """Stage 02: Build graph nodes and edges from CSV.

        Args:
            job_id: Job identifier
            upload_id: Upload identifier for provenance

        Returns:
            Graph result with nodes and edges

        Raises:
            Exception: If graph building fails
        """
        stage_runner = self._stage_runners[job_id]

        self._update_job_status(
            job_id,
            status="processing",
            message="Building graph from CSV data",
            progress=40.0,
        )

        # Load CSV
        csv_path = stage_runner.get_stage_dir(StageRunner.STAGE_SOURCE) / "source.csv"
        df = await asyncio.to_thread(pd.read_csv, csv_path)

        if df.empty:
            raise RuntimeError("CSV file is empty")

        # Create lowercase column map for lookup
        lower_col_map = {col.lower(): col for col in df.columns}

        # Build nodes and edges
        nodes = []
        edges = []
        seen_categories = set()
        seen_engine_types = set()

        for _, row in df.iterrows():
            # Get model field
            model_field = row.get("model")
            if pd.isna(model_field) or not model_field:
                continue

            model_str = str(model_field).strip()
            make, model = _split_make(model_str)

            # Create motorcycle node
            bike_id = _node_id(f"{make}|{model}")
            description = _build_description(row, lower_col_map)

            nodes.append({
                "id": bike_id,
                "name": model_str,
                "type": "Motorcycle",
                "description": description,
                "sourceDocumentId": upload_id,
            })

            # Create category node and edge
            category = row.get("category")
            if pd.notna(category) and category:
                category_str = str(category).strip()
                if category_str not in seen_categories:
                    category_id = _node_id(f"category|{category_str}")
                    nodes.append({
                        "id": category_id,
                        "name": category_str,
                        "type": "Category",
                        "sourceDocumentId": upload_id,
                    })
                    seen_categories.add(category_str)

                edges.append({
                    "fromNodeId": bike_id,
                    "toNodeId": _node_id(f"category|{category_str}"),
                    "relationshipType": "BELONGS_TO",
                    "weight": 1.0,
                })

            # Create engine type node and edge
            engine_type = row.get("engine type")
            if pd.notna(engine_type) and engine_type:
                engine_str = str(engine_type).strip()
                if engine_str not in seen_engine_types:
                    engine_id = _node_id(f"engine|{engine_str}")
                    nodes.append({
                        "id": engine_id,
                        "name": engine_str,
                        "type": "EngineType",
                        "sourceDocumentId": upload_id,
                    })
                    seen_engine_types.add(engine_str)

                edges.append({
                    "fromNodeId": bike_id,
                    "toNodeId": _node_id(f"engine|{engine_str}"),
                    "relationshipType": "HAS_ENGINE_TYPE",
                    "weight": 1.0,
                })

        graph_result = {
            "nodes": nodes,
            "edges": edges,
        }

        # Save to stage directory
        stage_dir = stage_runner.get_stage_dir(StageRunner.STAGE_GRAPH_CANDIDATES)
        graph_file = stage_dir / "graph_entities.json"

        with open(graph_file, "w", encoding="utf-8") as f:
            json.dump(graph_result, f, indent=2)

        logger.info(
            "Built graph: %d nodes, %d edges",
            len(nodes),
            len(edges),
        )

        return graph_result

    async def _stage_upload_artifacts(
        self,
        job_id: str,
        upload_id: str,
    ) -> dict:
        """Stage 03: Upload graph artifacts via API.

        Args:
            job_id: Job identifier
            upload_id: Upload identifier

        Returns:
            Upload results

        Raises:
            Exception: If upload fails
        """
        stage_runner = self._stage_runners[job_id]

        self._update_job_status(
            job_id,
            status="processing",
            message="Uploading graph artifacts via API",
            progress=80.0,
        )

        # Load graph data
        graph_file = stage_runner.get_stage_dir(
            StageRunner.STAGE_GRAPH_CANDIDATES
        ) / "graph_entities.json"

        with open(graph_file, "r", encoding="utf-8") as f:
            graph_data = json.load(f)

        # Upload via API client
        # Note: This will be updated in PY-03 to use proper API endpoints
        upload_results = {
            "nodes_uploaded": len(graph_data.get("nodes", [])),
            "edges_uploaded": len(graph_data.get("edges", [])),
        }

        logger.info(
            "Uploaded graph artifacts: %s",
            upload_results,
        )

        return upload_results

    async def _stage_complete(
        self,
        job_id: str,
    ) -> None:
        """Stage 04: Mark processing as complete.

        Args:
            job_id: Job identifier
        """
        stage_runner = self._stage_runners[job_id]

        self._update_job_status(
            job_id,
            status="processing",
            message="Processing completed",
            progress=100.0,
        )

        logger.info("Job %s graph seeding complete", job_id)
