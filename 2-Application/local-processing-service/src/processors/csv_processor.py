import asyncio
import io
import json
import logging
import os
import uuid
from datetime import datetime, timezone
from typing import Any

import pandas as pd

from api.api_client import ApiClient
from security.log_sanitizer import sanitize_log_value

logger = logging.getLogger(__name__)
_ACTIVE_JOB_STATUSES = {"queued", "processing", "running", "inprogress"}
PIPELINE_STAGES = [
    "copying",
    "parsing",
    "chunking",
    "embedding",
    "uploading-chunks",
    "completed",
]

JobRecord = dict[str, Any]
ArtifactRecord = dict[str, Any]

_jobs: dict[str, JobRecord] = {}
_tasks: dict[str, asyncio.Task[None]] = {}

MAX_CHUNK_SIZE_TOKENS = int(os.getenv("MAX_CHUNK_SIZE_TOKENS", "512"))


def _estimate_tokens(text: str) -> int:
    return len(text) // 4


def _format_row(row: pd.Series) -> str:
    return " | ".join(f"{col}: {val}" for col, val in row.items() if pd.notna(val))


def _split_text_into_chunks(text: str, max_tokens: int) -> list[str]:
    if _estimate_tokens(text) <= max_tokens:
        return [text]
    lines = text.split("\n")
    chunks = []
    current_lines: list[str] = []
    current_tokens = 0
    for line in lines:
        line_tokens = _estimate_tokens(line)
        if current_tokens + line_tokens > max_tokens and current_lines:
            chunks.append("\n".join(current_lines))
            current_lines = []
            current_tokens = 0
        current_lines.append(line)
        current_tokens += line_tokens
    if current_lines:
        chunks.append("\n".join(current_lines))
    return chunks


class CSVProcessor:
    def __init__(self, blob_writer, embedder, api_client: ApiClient):
        self.blob_writer = blob_writer
        self.embedder = embedder
        self._api_client = api_client

    async def process_csv_async(
        self,
        upload_id: str,
        blob_container: str | None = None,
        metadata=None,
        local_file_path: str | None = None,
        job_id: str | None = None,
    ) -> str:
        job_id = job_id or str(uuid.uuid4())
        now = datetime.now(timezone.utc).isoformat()
        _jobs[job_id] = {
            "job_id": job_id,
            "upload_id": upload_id,
            "document_type": "spec-dataset",
            "status": "processing",
            "stage": "copying",
            "stage_index": 0,
            "total_stages": len(PIPELINE_STAGES) - 1,
            "message": "CSV processing started",
            "progress": 0.0,
            "chunks_processed": 0,
            "total_chunks": 0,
            "created_at": now,
            "updated_at": now,
        }
        task = asyncio.create_task(
            self._process_background(
                job_id, upload_id, blob_container, metadata, local_file_path
            )
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
        logger.info("CSV job cancelled job_id=%s", sanitize_log_value(job_id))
        return _jobs.get(job_id)

    def _mark_cancelled(self, job_id: str) -> None:
        job = _jobs.get(job_id)
        if not job:
            return
        job.update(
            {
                "status": "cancelled",
                "stage": "cancelled",
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
            job = _jobs.get(job_id, {})
            await self._api_client.report_stage(
                job_id,
                "cancelled",
                chunks_processed=job.get("chunks_processed", 0),
                total_chunks=job.get("total_chunks", 0),
                failure_reason="Cancelled by user",
            )
        except TypeError:
            pass

    async def _report_failed(self, job_id: str, failure_reason: str) -> None:
        if not self._api_client.is_configured():
            return
        try:
            job = _jobs.get(job_id, {})
            await self._api_client.report_stage(
                job_id,
                "failed",
                chunks_processed=job.get("chunks_processed", 0),
                total_chunks=job.get("total_chunks", 0),
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
                        chunks_processed=job.get("chunks_processed", 0),
                        total_chunks=job.get("total_chunks", 0),
                    )
                )
            except TypeError:
                pass

    async def _process_background(
        self,
        job_id: str,
        upload_id: str,
        blob_container: str | None,
        metadata,
        local_file_path: str | None = None,
    ) -> None:
        try:
            self._set_stage(job_id, "copying", "Preparing CSV source", 0.0)
            if local_file_path:
                df = await asyncio.to_thread(pd.read_csv, local_file_path)
            else:
                csv_bytes = await self.blob_writer.download_blob(
                    blob_container, f"{upload_id}.csv"
                )
                df = pd.read_csv(io.BytesIO(csv_bytes))
            self._raise_if_cancelled(job_id)

            self._set_stage(job_id, "parsing", "Parsing CSV rows", 0.1)
            if df.empty:
                _jobs[job_id].update(
                    {
                        "status": "failed",
                        "message": "Empty CSV file",
                        "progress": 1.0,
                        "updated_at": datetime.now(timezone.utc).isoformat(),
                    }
                )
                return

            df.columns = [col.strip().lower() for col in df.columns]

            grouping_cols = [c for c in ["make", "model", "year"] if c in df.columns]

            if grouping_cols:
                grouped = df.groupby(grouping_cols, dropna=False)
            else:
                grouped = [("all", df)]

            chunks: list[ArtifactRecord] = []
            chunk_index = 0

            meta_make = getattr(metadata, "make", None) if metadata else None
            meta_model = getattr(metadata, "model", None) if metadata else None
            meta_year = getattr(metadata, "year", None) if metadata else None

            groups = list(grouped) if grouping_cols else grouped
            total_groups = len(groups)

            self._set_stage(job_id, "chunking", "Chunking CSV rows", 0.2)
            for i, (group_key, group_df) in enumerate(groups):
                self._raise_if_cancelled(job_id)
                if grouping_cols:
                    key_dict = (
                        dict(zip(grouping_cols, group_key))
                        if isinstance(group_key, tuple)
                        else {grouping_cols[0]: group_key}
                    )
                else:
                    key_dict = {}

                make = str(key_dict.get("make", meta_make or "Unknown"))
                model = str(key_dict.get("model", meta_model or "Unknown"))
                year_val = key_dict.get("year", meta_year)
                year = (
                    int(year_val) if year_val is not None and pd.notna(year_val) else 0
                )

                row_texts = [_format_row(row) for _, row in group_df.iterrows()]
                group_text = "\n".join(row_texts)

                sub_chunks = _split_text_into_chunks(group_text, MAX_CHUNK_SIZE_TOKENS)

                if len(sub_chunks) > 1:
                    title_base = (
                        f"{make} {model} {year} - Specifications"
                        if make != "Unknown"
                        else "Motorcycle Specifications"
                    )
                else:
                    title_base = (
                        f"{make} {model} {year} - Specifications"
                        if make != "Unknown"
                        else "Motorcycle Specifications"
                    )

                for text in sub_chunks:
                    self._raise_if_cancelled(job_id)
                    self._set_stage(
                        job_id,
                        "embedding",
                        f"Embedding chunk {chunk_index + 1}",
                        round(0.2 + (i + 1) / max(total_groups, 1) * 0.5, 2),
                        chunks_processed=chunk_index,
                        total_chunks=max(
                            len(chunks) + len(sub_chunks), chunk_index + 1
                        ),
                    )
                    embedding = await self.embedder.generate_embedding(text)
                    self._raise_if_cancelled(job_id)
                    now = datetime.now(timezone.utc).isoformat()

                    chunk = {
                        "id": f"{upload_id}-csv-{chunk_index}",
                        "title": title_base,
                        "content": text,
                        "documentType": "spec-dataset",
                        "category": "Unknown",
                        "make": make,
                        "model": model,
                        "year": year,
                        "sourceFile": upload_id,
                        "section": "Specifications",
                        "pageNumber": 0,
                        "pageRange": "N/A",
                        "primarySection": "Specifications",
                        "sectionLevel": 1,
                        "sectionHeadings": ["Specifications"],
                        "tableCaption": None,
                        "chunkIndex": chunk_index,
                        "tags": ["csv", "specifications"],
                        "contentVector": embedding,
                        "createdAt": now,
                        "updatedAt": now,
                    }
                    chunks.append(chunk)
                    chunk_index += 1
                    if chunk_index == 1 or chunk_index % 10 == 0:
                        self._set_stage(
                            job_id,
                            "embedding",
                            f"Embedding chunk {chunk_index}",
                            round(0.2 + (i + 1) / max(total_groups, 1) * 0.5, 2),
                            chunks_processed=chunk_index,
                            total_chunks=max(len(chunks), chunk_index),
                        )

                _jobs[job_id]["progress"] = round((i + 1) / total_groups * 0.9, 2)
                _jobs[job_id]["updated_at"] = datetime.now(timezone.utc).isoformat()

            self._raise_if_cancelled(job_id)
            self._set_stage(
                job_id,
                "uploading-chunks",
                f"Uploading {len(chunks)} search chunks",
                0.9,
                chunks_processed=len(chunks),
                total_chunks=len(chunks),
            )
            chunks_bytes = ("\n".join(json.dumps(c) for c in chunks)).encode("utf-8")
            self._raise_if_cancelled(job_id)
            await self._api_client.upload_artifact(
                chunks_bytes, upload_id, "search-chunks", "application/x-ndjson"
            )
            self._raise_if_cancelled(job_id)

            self._set_stage(
                job_id,
                "completed",
                f"Processed {len(chunks)} chunks from CSV",
                1.0,
                chunks_processed=len(chunks),
                total_chunks=len(chunks),
            )
            _jobs[job_id].update(
                {
                    "status": "completed",
                    "message": f"Processed {len(chunks)} chunks from CSV",
                    "progress": 1.0,
                    "chunks_processed": len(chunks),
                    "updated_at": datetime.now(timezone.utc).isoformat(),
                }
            )
            logger.info(
                "CSV processing completed for upload %s: %d chunks",
                # codeql[py/log-injection]
                sanitize_log_value(upload_id),
                len(chunks),
            )

        except asyncio.CancelledError:
            self._mark_cancelled(job_id)
            await self._report_cancelled(job_id)
            logger.info(
                "CSV processing cancelled for upload %s",
                # codeql[py/log-injection]
                sanitize_log_value(upload_id),
            )
        except Exception as exc:
            logger.error(
                "CSV processing failed for upload %s error=%s",
                # codeql[py/log-injection]
                sanitize_log_value(upload_id),
                sanitize_log_value(str(exc)),
            )
            failure_reason = (
                f"CSV processing failed — {type(exc).__name__}: {str(exc)[:300]}"
            )
            _jobs[job_id].update(
                {
                    "status": "failed",
                    "stage": "failed",
                    "message": failure_reason,
                    "progress": 0.0,
                    "updated_at": datetime.now(timezone.utc).isoformat(),
                }
            )
            await self._report_failed(job_id, failure_reason)
