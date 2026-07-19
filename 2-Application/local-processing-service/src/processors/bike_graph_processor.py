"""
Bike graph processor: converts a local motorcycle spec CSV into graph nodes and edges.

No LLM, no embeddings, no Azure AI Search — all processing is deterministic and local.

Graph structure produced:
  - GraphNode (Type="Motorcycle")  — one per unique (model_name, year)
  - GraphNode (Type="Category")    — one per unique category value
  - GraphNode (Type="EngineType")  — one per unique engine-type value
  - GraphEdge BELONGS_TO           — bike -> category
  - GraphEdge HAS_ENGINE_TYPE      — bike -> engine type

Output is written to blob storage at:
  raw-uploads/graph-entities/{upload_id}/entities.json

in the format consumed by GraphEntityIngestionService:
  [{"nodes": [...], "edges": [...]}]
"""

import asyncio
import io
import json
import logging
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Optional

import pandas as pd

from api.api_client import ApiClient
from security.log_sanitizer import sanitize_log_value

logger = logging.getLogger(__name__)

# Deterministic UUID namespace — stable across runs so re-processing is idempotent.
_NAMESPACE = uuid.UUID("6ba7b810-9dad-11d1-80b4-00c04fd430c8")
_ACTIVE_JOB_STATUSES = {"queued", "processing", "running", "inprogress"}
PIPELINE_STAGES = [
    "copying",
    "parsing",
    "building-graph",
    "uploading-graph",
    "completed",
]

JobRecord = dict[str, Any]
GraphRecord = dict[str, Any]

_jobs: dict[str, JobRecord] = {}
_tasks: dict[str, asyncio.Task[None]] = {}

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


def _node_id(seed: str) -> str:
    """Return a deterministic UUID string derived from *seed*."""
    return str(uuid.uuid5(_NAMESPACE, seed))


def _split_make(model_field: str) -> tuple[str, str]:
    """Return (make, rest_of_model) by splitting on the first space.

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


class BikeGraphProcessor:
    """Converts a motorcycle spec CSV to graph nodes/edges without LLM or embeddings."""

    def __init__(self, blob_writer, api_client: ApiClient) -> None:
        self.blob_writer = blob_writer
        self._api_client = api_client

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    async def process_async(
        self,
        upload_id: str,
        blob_container: Optional[str] = None,
        local_file_path: Optional[str] = None,
        job_id: Optional[str] = None,
    ) -> str:
        """Start background processing from blob storage or a local CSV path."""
        if not blob_container and not local_file_path:
            raise ValueError("Either blob_container or local_file_path is required")

        job_id = job_id or str(uuid.uuid4())
        now = datetime.now(timezone.utc).isoformat()
        _jobs[job_id] = {
            "job_id": job_id,
            "upload_id": upload_id,
            "document_type": "bike-graph",
            "status": "processing",
            "stage": "copying",
            "stage_index": 0,
            "total_stages": len(PIPELINE_STAGES) - 1,
            "message": "Bike graph processing started",
            "progress": 0.0,
            "nodes_created": 0,
            "edges_created": 0,
            "created_at": now,
            "updated_at": now,
        }
        logger.info(
            "Bike graph job queued for upload %s (source=%s)",
            # codeql[py/log-injection]
            sanitize_log_value(upload_id),
            # codeql[py/log-injection]
            sanitize_log_value(local_file_path or f"blob:{blob_container}"),
        )
        task = asyncio.create_task(
            self._process_background(job_id, upload_id, blob_container, local_file_path)
        )
        _tasks[job_id] = task

        def remove_completed_task(_: asyncio.Task[None]) -> None:
            _tasks.pop(job_id, None)

        task.add_done_callback(remove_completed_task)
        return job_id

    async def get_job_status(self, job_id: str) -> JobRecord | None:
        return _jobs.get(job_id)

    async def list_jobs(self) -> list[JobRecord]:
        return list(_jobs.values())

    async def clear_terminal_jobs(self) -> int:
        terminal_job_ids = [
            job_id
            for job_id, job in _jobs.items()
            if str(job.get("status", "")).strip().lower() not in _ACTIVE_JOB_STATUSES
        ]

        for job_id in terminal_job_ids:
            _jobs.pop(job_id, None)
            _tasks.pop(job_id, None)

        return len(terminal_job_ids)

    async def stop_job(self, job_id: str) -> JobRecord | None:
        job = _jobs.get(job_id)
        if not job:
            return None
        if str(job.get("status", "")).strip().lower() not in _ACTIVE_JOB_STATUSES:
            return job

        self._mark_cancelled(job_id)
        task = _tasks.get(job_id)
        if task and not task.done():
            task.cancel()

        await self._report_cancelled(job_id)
        logger.info("Bike graph job cancelled job_id=%s", sanitize_log_value(job_id))
        return _jobs.get(job_id)

    def _mark_cancelled(self, job_id: str) -> None:
        job = _jobs.get(job_id)
        if not job:
            return
        job.update(
            {
                "status": "cancelled",
                "message": "Cancelled by user",
                "progress": job.get("progress", 0.0),
                "updated_at": datetime.now(timezone.utc).isoformat(),
            }
        )

    def _raise_if_cancelled(self, job_id: str) -> None:
        job = _jobs.get(job_id)
        if str(job.get("status", "") if job else "").lower() == "cancelled":
            raise asyncio.CancelledError()

    async def _report_cancelled(self, job_id: str) -> None:
        if not self._api_client.is_configured():
            return
        try:
            await self._api_client.report_stage(
                job_id,
                "cancelled",
                failure_reason="Cancelled by user",
            )
        except TypeError:
            pass

    async def _report_failed(self, job_id: str, failure_reason: str) -> None:
        if not self._api_client.is_configured():
            return
        try:
            await self._api_client.report_stage(
                job_id,
                "failed",
                chunks_processed=_jobs[job_id].get("nodes_created", 0)
                + _jobs[job_id].get("edges_created", 0),
                total_chunks=_jobs[job_id].get("nodes_created", 0)
                + _jobs[job_id].get("edges_created", 0),
                failure_reason=failure_reason,
            )
        except TypeError:
            pass
        except Exception as exc:
            logger.warning(
                "Failed to report terminal processor failure for job %s error=%s",
                sanitize_log_value(job_id),
                sanitize_log_value(str(exc)),
            )

    def _set_stage(
        self,
        job_id: str,
        stage: str,
        message: str,
        progress: float,
        **extra,
    ) -> None:
        job = _jobs[job_id]
        try:
            stage_index = PIPELINE_STAGES.index(stage)
        except ValueError:
            stage_index = job.get("stage_index", 0)

        job.update(
            {
                "stage": stage,
                "stage_index": stage_index,
                "message": message,
                "progress": progress,
                "updated_at": datetime.now(timezone.utc).isoformat(),
                **extra,
            }
        )

        if self._api_client.is_configured():
            try:
                _ = asyncio.create_task(
                    self._api_client.report_stage(
                        job_id,
                        stage,
                        chunks_processed=job.get("nodes_created", 0)
                        + job.get("edges_created", 0),
                        total_chunks=job.get("nodes_created", 0)
                        + job.get("edges_created", 0),
                    )
                )
            except TypeError:
                pass

    # ------------------------------------------------------------------
    # Background processing
    # ------------------------------------------------------------------

    async def _process_background(
        self,
        job_id: str,
        upload_id: str,
        blob_container: Optional[str],
        local_file_path: Optional[str],
    ) -> None:
        try:
            logger.info(
                "Bike graph processing started for upload %s (source=%s)",
                # codeql[py/log-injection]
                sanitize_log_value(upload_id),
                # codeql[py/log-injection]
                sanitize_log_value(local_file_path or f"blob:{blob_container}"),
            )
            self._set_stage(job_id, "copying", "Preparing bike graph source", 0.0)
            if local_file_path:
                logger.info(
                    "Bike graph upload %s reading local CSV %s",
                    # codeql[py/log-injection]
                    sanitize_log_value(upload_id),
                    sanitize_log_value(local_file_path),
                )
                self._raise_if_cancelled(job_id)
                self._set_stage(job_id, "parsing", "Parsing bike graph CSV", 0.25)
                nodes, edges = await asyncio.to_thread(
                    self._build_graph, upload_id, local_file_path
                )
            else:
                logger.info(
                    "Bike graph upload %s downloading source CSV from blob container %s",
                    # codeql[py/log-injection]
                    sanitize_log_value(upload_id),
                    # codeql[py/log-injection]
                    sanitize_log_value(blob_container),
                )
                csv_bytes = await self.blob_writer.download_blob(
                    blob_container, f"{upload_id}.csv"
                )
                self._raise_if_cancelled(job_id)
                self._set_stage(job_id, "parsing", "Parsing bike graph CSV", 0.25)
                nodes, edges = await asyncio.to_thread(
                    self._build_graph_from_bytes, upload_id, csv_bytes
                )
            self._set_stage(
                job_id,
                "building-graph",
                "Building graph nodes and edges",
                0.55,
                nodes_created=len(nodes),
                edges_created=len(edges),
            )
            self._raise_if_cancelled(job_id)

            logger.info(
                "Bike graph upload %s built local artifact with %d nodes and %d edges; embeddings are not used for this job type",
                # codeql[py/log-injection]
                sanitize_log_value(upload_id),
                len(nodes),
                len(edges),
            )

            payload = [{"nodes": nodes, "edges": edges}]
            entities_bytes = json.dumps(payload).encode("utf-8")
            self._set_stage(
                job_id,
                "uploading-graph",
                "Uploading graph entities",
                0.85,
                nodes_created=len(nodes),
                edges_created=len(edges),
            )
            logger.info(
                "Bike graph upload %s sending graph-entities artifact to backend API (%d bytes)",
                # codeql[py/log-injection]
                sanitize_log_value(upload_id),
                len(entities_bytes),
            )
            self._raise_if_cancelled(job_id)
            await self._api_client.upload_artifact(
                entities_bytes, upload_id, "graph-entities", "application/json"
            )
            self._raise_if_cancelled(job_id)

            self._set_stage(
                job_id,
                "completed",
                f"Built {len(nodes)} nodes and {len(edges)} edges",
                1.0,
                nodes_created=len(nodes),
                edges_created=len(edges),
            )
            _jobs[job_id]["status"] = "completed"
            logger.info(
                "Bike graph processing completed for upload %s: %d nodes, %d edges",
                # codeql[py/log-injection]
                sanitize_log_value(upload_id),
                len(nodes),
                len(edges),
            )

        except asyncio.CancelledError:
            self._mark_cancelled(job_id)
            await self._report_cancelled(job_id)
            logger.info(
                "Bike graph processing cancelled for upload %s",
                # codeql[py/log-injection]
                sanitize_log_value(upload_id),
            )
        except Exception as exc:
            logger.error(
                "Bike graph processing failed for upload %s error=%s",
                # codeql[py/log-injection]
                sanitize_log_value(upload_id),
                sanitize_log_value(str(exc)),
            )
            failure_reason = (
                f"Bike graph processing failed — {type(exc).__name__}: {str(exc)[:300]}"
            )
            _jobs[job_id].update(
                {
                    "status": "failed",
                    "stage": "failed",
                    "message": failure_reason,
                    "progress": _jobs[job_id].get("progress", 0.0),
                    "updated_at": datetime.now(timezone.utc).isoformat(),
                }
            )
            await self._report_failed(job_id, failure_reason)

    # ------------------------------------------------------------------
    # Graph construction (synchronous — runs in a thread)
    # ------------------------------------------------------------------

    def _build_graph(
        self, upload_id: str, local_file_path: str
    ) -> tuple[list[GraphRecord], list[GraphRecord]]:
        logger.info(
            "Bike graph upload %s loading dataframe from local file %s",
            sanitize_log_value(upload_id),
            sanitize_log_value(local_file_path),
        )
        df = pd.read_csv(local_file_path, low_memory=False)
        return self._build_graph_from_dataframe(upload_id, df)

    def _build_graph_from_bytes(
        self, upload_id: str, csv_bytes: bytes
    ) -> tuple[list[GraphRecord], list[GraphRecord]]:
        logger.info(
            "Bike graph upload %s loading dataframe from blob payload (%d bytes)",
            sanitize_log_value(upload_id),
            len(csv_bytes),
        )
        df = pd.read_csv(io.BytesIO(csv_bytes), low_memory=False)
        return self._build_graph_from_dataframe(upload_id, df)

    def _build_graph_from_dataframe(
        self, upload_id: str, df: pd.DataFrame
    ) -> tuple[list[GraphRecord], list[GraphRecord]]:
        logger.info(
            "Bike graph upload %s normalising dataframe with %d rows and %d columns",
            sanitize_log_value(upload_id),
            len(df.index),
            len(df.columns),
        )
        if df.empty:
            raise ValueError("CSV file is empty")

        # Normalise column names for lookup while keeping originals for data access.
        df.columns = [c.strip() for c in df.columns]
        lower_col_map: dict[str, str] = {c.lower(): c for c in df.columns}

        model_col = lower_col_map.get("model")
        year_col = lower_col_map.get("year")
        category_col = lower_col_map.get("category")
        engine_col = lower_col_map.get("engine type")

        if not model_col:
            raise ValueError("CSV must contain a 'Model' column")
        if not year_col:
            raise ValueError("CSV must contain a 'Year' column")

        nodes: list[GraphRecord] = []
        edges: list[GraphRecord] = []

        # Track IDs already added to avoid duplicates across rows.
        seen_bike_ids: set[str] = set()
        seen_category_ids: set[str] = set()
        seen_engine_ids: set[str] = set()
        seen_edge_keys: set[tuple[str, str, str]] = set()

        for _, row in df.iterrows():
            model_raw = str(row[model_col]).strip() if pd.notna(row[model_col]) else ""
            year_raw = row[year_col]

            if not model_raw or not pd.notna(year_raw):
                continue

            year = int(year_raw)

            # ---- Bike node ------------------------------------------------
            bike_id = _node_id(f"bike:{model_raw.lower()}:{year}")
            if bike_id not in seen_bike_ids:
                seen_bike_ids.add(bike_id)
                nodes.append(
                    {
                        "id": bike_id,
                        "name": f"{model_raw} {year}",
                        "type": "Motorcycle",
                        "description": _build_description(row, lower_col_map),
                        "sourceDocumentId": upload_id,
                    }
                )

            # ---- Category node + BELONGS_TO edge --------------------------
            if category_col and pd.notna(row.get(category_col)):
                category = str(row[category_col]).strip()
                if category:
                    cat_id = _node_id(f"category:{category.lower()}")
                    if cat_id not in seen_category_ids:
                        seen_category_ids.add(cat_id)
                        nodes.append(
                            {
                                "id": cat_id,
                                "name": category,
                                "type": "Category",
                                "description": f"Motorcycle category: {category}",
                                "sourceDocumentId": upload_id,
                            }
                        )
                    edge_key = (bike_id, cat_id, "BELONGS_TO")
                    if edge_key not in seen_edge_keys:
                        seen_edge_keys.add(edge_key)
                        edges.append(
                            {
                                "fromNodeId": bike_id,
                                "toNodeId": cat_id,
                                "relationshipType": "BELONGS_TO",
                                "weight": 1.0,
                                "context": f"{model_raw} {year} is a {category} motorcycle",
                            }
                        )

            # ---- Engine type node + HAS_ENGINE_TYPE edge ------------------
            if engine_col and pd.notna(row.get(engine_col)):
                engine = str(row[engine_col]).strip()
                if engine:
                    eng_id = _node_id(f"enginetype:{engine.lower()}")
                    if eng_id not in seen_engine_ids:
                        seen_engine_ids.add(eng_id)
                        nodes.append(
                            {
                                "id": eng_id,
                                "name": engine,
                                "type": "EngineType",
                                "description": f"Engine configuration: {engine}",
                                "sourceDocumentId": upload_id,
                            }
                        )
                    edge_key = (bike_id, eng_id, "HAS_ENGINE_TYPE")
                    if edge_key not in seen_edge_keys:
                        seen_edge_keys.add(edge_key)
                        edges.append(
                            {
                                "fromNodeId": bike_id,
                                "toNodeId": eng_id,
                                "relationshipType": "HAS_ENGINE_TYPE",
                                "weight": 1.0,
                                "context": f"{model_raw} {year} uses {engine}",
                            }
                        )

            logger.info(
                "Bike graph upload %s finished graph construction with %d nodes and %d edges",
                sanitize_log_value(upload_id),
                len(nodes),
                len(edges),
            )
        return nodes, edges
