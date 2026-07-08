"""PDF processor using Docling HybridChunker for semantic chunking."""

import asyncio
import json
import logging
import os
import uuid
from pathlib import Path
from datetime import datetime, timezone

from docling.chunking import HybridChunker
from docling.document_converter import DocumentConverter

from typing import Any
from api.api_client import ApiClient
from embeddings.tokenizer_provider import get_pdf_chunker_tokenizer
from extraction.graph_extractor import GraphExtractor
from storage.blob_writer import BlobWriter

logger = logging.getLogger(__name__)
_ACTIVE_JOB_STATUSES = {"queued", "processing", "running", "inprogress"}

PDF_CHUNKER_MAX_TOKENS = int(os.getenv("PDF_CHUNKER_MAX_TOKENS", "512"))

PIPELINE_STAGES = [
    "copying",
    "parsing",
    "chunking",
    "embedding",
    "uploading-chunks",
    "extracting-graph",
    "uploading-graph",
    "completed",
]

_jobs: dict[str, dict] = {}
_tasks: dict[str, asyncio.Task] = {}


def _make_job(job_id: str, upload_id: str, document_type: str) -> dict:
    now = datetime.now(timezone.utc).isoformat()
    return {
        "job_id": job_id,
        "upload_id": upload_id,
        "document_type": document_type,
        "status": "processing",
        "stage": "copying",
        "stage_index": 0,
        "total_stages": len(PIPELINE_STAGES) - 1,
        "progress": 0.0,
        "message": "Copying source PDF",
        "chunks_processed": 0,
        "total_chunks": 0,
        "created_at": now,
        "updated_at": now,
        "stage_history": [
            {
                "stage": "copying",
                "message": "Copying source PDF",
                "progress": 0.0,
                "chunks_processed": 0,
                "total_chunks": 0,
                "set_at": now,
            }
        ],
    }


def _is_timeout_failure(exc: BaseException) -> bool:
    current: BaseException | None = exc
    while current is not None:
        if isinstance(current, TimeoutError):
            return True
        current = current.__cause__ or current.__context__

    return False


def _format_safe_failure(exc: Exception) -> dict[str, str]:
    if _is_timeout_failure(exc):
        message = (
            "Embedding request timed out. Check the local embedding model and retry."
        )
    else:
        message = f"PDF processing failed: {type(exc).__name__}"

    return {"status": "failed", "message": message, "error": message}


class PDFProcessor:
    def __init__(
        self,
        blob_writer: BlobWriter,
        embedder: Any,
        graph_extractor: GraphExtractor,
        api_client: ApiClient,
    ):
        self._blob_writer = blob_writer
        self._embedder = embedder
        self._graph_extractor = graph_extractor
        self._api_client = api_client

    async def process_pdf_async(
        self,
        upload_id: str,
        document_type: str,
        blob_container: str,
        metadata,
        source_access_token: str | None = None,
        local_file_path: str | None = None,
        job_id: str | None = None,
    ) -> str:
        """Returns job_id immediately, fires background task via asyncio.create_task."""
        job_id = job_id or str(uuid.uuid4())
        _jobs[job_id] = _make_job(job_id, upload_id, document_type)
        logger.info(
            "PDF job queued job_id=%s upload_id=%s document_type=%s blob_container=%s has_source_access_token=%s has_local_file=%s",
            job_id,
            upload_id,
            document_type,
            blob_container,
            bool(source_access_token),
            bool(local_file_path),
        )
        task = asyncio.create_task(
            self._process_pdf(
                job_id,
                upload_id,
                document_type,
                blob_container,
                metadata,
                source_access_token,
                local_file_path,
            )
        )
        _tasks[job_id] = task
        task.add_done_callback(lambda _task, jid=job_id: _tasks.pop(jid, None))
        return job_id

    async def get_job_status(self, job_id: str) -> dict | None:
        return _jobs.get(job_id)

    async def list_jobs(self) -> list[dict]:
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

    async def stop_job(self, job_id: str) -> dict | None:
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
        logger.info("PDF job cancelled job_id=%s", job_id)
        return _jobs.get(job_id)

    def _mark_cancelled(self, job_id: str) -> None:
        job = _jobs.get(job_id)
        if not job:
            return
        now = datetime.now(timezone.utc).isoformat()
        job.update({
            "status": "cancelled",
            "stage": "cancelled",
            "message": "Cancelled by user",
            "progress": job.get("progress", 0.0),
            "updated_at": now,
        })
        self._append_stage_history(job, "cancelled", "Cancelled by user", now)

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

    def _set_stage(self, job_id: str, stage: str, message: str, progress: float, **extra) -> None:
        job = _jobs[job_id]
        try:
            stage_index = PIPELINE_STAGES.index(stage)
        except ValueError:
            stage_index = job.get("stage_index", 0)
        now = datetime.now(timezone.utc).isoformat()
        job.update({
            "stage": stage,
            "stage_index": stage_index,
            "message": message,
            "progress": progress,
            "updated_at": now,
            **extra,
        })
        self._append_stage_history(job, stage, message, now)
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
                pass  # mock client in tests

    @staticmethod
    def _append_stage_history(job: dict, stage: str, message: str, set_at: str) -> None:
        history = job.setdefault("stage_history", [])
        if history and history[-1].get("stage") == stage:
            history[-1].update({
                "message": message,
                "progress": job.get("progress", 0.0),
                "chunks_processed": job.get("chunks_processed", 0),
                "total_chunks": job.get("total_chunks", 0),
                "set_at": set_at,
            })
            return

        history.append({
            "stage": stage,
            "message": message,
            "progress": job.get("progress", 0.0),
            "chunks_processed": job.get("chunks_processed", 0),
            "total_chunks": job.get("total_chunks", 0),
            "set_at": set_at,
        })

    async def _process_pdf(
        self,
        job_id: str,
        upload_id: str,
        document_type: str,
        blob_container: str,
        metadata,
        source_access_token: str | None = None,
        local_file_path: str | None = None,
    ) -> None:
        """Background coroutine that downloads, chunks, embeds, extracts, and uploads."""
        try:
            logger.info(
                "PDF job started job_id=%s upload_id=%s document_type=%s",
                job_id, upload_id, document_type,
            )

            # ── Stage 0: Copying ──────────────────────────────
            self._set_stage(job_id, "copying", "Preparing source PDF", 0.0)

            source_path = await self._resolve_source_pdf_path(
                upload_id,
                document_type,
                blob_container,
                source_access_token,
                local_file_path,
            )
            self._raise_if_cancelled(job_id)

            # ── Stage 1: Parsing ──────────────────────────────
            self._set_stage(job_id, "parsing", "Converting PDF with Docling", 0.05)

            converter = DocumentConverter()
            result = await asyncio.to_thread(converter.convert, str(source_path))
            self._raise_if_cancelled(job_id)
            logger.info(
                "PDF job Docling conversion completed job_id=%s upload_id=%s",
                job_id, upload_id,
            )

            # ── Stage 2: Chunking ─────────────────────────────
            self._set_stage(job_id, "chunking", "Loading chunker tokenizer", 0.1)
            logger.info(
                "PDF job loading chunker tokenizer job_id=%s upload_id=%s max_tokens=%d",
                job_id, upload_id, PDF_CHUNKER_MAX_TOKENS,
            )
            tokenizer = get_pdf_chunker_tokenizer(PDF_CHUNKER_MAX_TOKENS)
            logger.info(
                "PDF job tokenizer loaded job_id=%s upload_id=%s tokenizer_class=%s",
                job_id, upload_id, type(tokenizer).__name__,
            )

            self._set_stage(job_id, "chunking", "Chunking document", 0.12)
            chunker = HybridChunker(tokenizer=tokenizer)
            chunks = list(await asyncio.to_thread(chunker.chunk, result.document))
            self._raise_if_cancelled(job_id)
            total_chunks = len(chunks)
            logger.info(
                "PDF job chunking completed job_id=%s upload_id=%s chunk_count=%d",
                job_id, upload_id, total_chunks,
            )

            if not chunks:
                logger.warning("PDF job failed with no chunks job_id=%s upload_id=%s", job_id, upload_id)
                _jobs[job_id].update({
                    "status": "failed",
                    "stage": "chunking",
                    "error": "No chunks extracted from PDF",
                    "progress": 1.0,
                    "updated_at": datetime.now(timezone.utc).isoformat(),
                })
                return

            # ── Stage 3: Embedding ────────────────────────────
            records: list[dict] = []
            for i, chunk in enumerate(chunks):
                self._raise_if_cancelled(job_id)
                headings = list(chunk.meta.headings) if chunk.meta.headings else []
                has_prov = chunk.meta.doc_items and chunk.meta.doc_items[0].prov
                page_no = chunk.meta.doc_items[0].prov[0].page_no if has_prov else 0
                embedding = await self._embedder.generate_embedding(chunk.text)
                self._raise_if_cancelled(job_id)

                if i == 0 or (i + 1) == total_chunks or (i + 1) % 10 == 0:
                    logger.info(
                        "PDF job embedding progress job_id=%s upload_id=%s chunk_index=%d total_chunks=%d",
                        job_id, upload_id, i + 1, total_chunks,
                    )

                make = str(getattr(metadata, "make", "") or "")
                model = str(getattr(metadata, "model", "") or "")
                year_val = getattr(metadata, "year", 0) or 0
                try:
                    year = int(year_val)
                except (TypeError, ValueError):
                    year = 0

                now_ts = datetime.now(timezone.utc).isoformat()
                records.append({
                    "id": f"{upload_id}-pdf-{i}",
                    "title": headings[0] if headings else f"Chunk {i}",
                    "content": chunk.text,
                    "documentType": document_type,
                    "category": "Unknown",
                    "make": make,
                    "model": model,
                    "year": year,
                    "sourceFile": upload_id,
                    "section": headings[0] if headings else "",
                    "pageNumber": page_no,
                    "pageRange": str(page_no) if page_no else "0",
                    "primarySection": headings[0] if headings else "",
                    "sectionLevel": len(headings),
                    "sectionHeadings": headings,
                    "tableCaption": None,
                    "chunkIndex": i,
                    "tags": [t for t in [make, model] if t],
                    "contentVector": embedding,
                    "createdAt": now_ts,
                    "updatedAt": now_ts,
                })

                self._set_stage(
                    job_id, "embedding",
                    f"Embedding chunk {i + 1} of {total_chunks}",
                    round(0.2 + len(records) / total_chunks * 0.3, 2),
                    chunks_processed=len(records),
                    total_chunks=total_chunks,
                )

            # ── Stage 4: Uploading chunks ─────────────────────
            self._set_stage(job_id, "uploading-chunks",
                f"Uploading {len(records)} search chunks", 0.55,
                chunks_processed=len(records), total_chunks=total_chunks,
            )
            logger.info(
                "PDF job uploading search chunks job_id=%s upload_id=%s record_count=%d",
                job_id, upload_id, len(records),
            )

            self._raise_if_cancelled(job_id)
            chunks_bytes = ("\n".join(json.dumps(r) for r in records)).encode("utf-8")
            await self._api_client.upload_artifact(
                chunks_bytes, upload_id, "search-chunks", "application/x-ndjson"
            )
            self._raise_if_cancelled(job_id)
            logger.info(
                "PDF job search chunks uploaded job_id=%s upload_id=%s byte_count=%d",
                job_id, upload_id, len(chunks_bytes),
            )

            # ── Stage 5: Extracting graph entities ────────────
            self._set_stage(job_id, "extracting-graph",
                f"Extracting graph entities from {total_chunks} chunks", 0.8,
            )
            logger.info(
                "PDF job extracting graph entities job_id=%s upload_id=%s chunk_count=%d",
                job_id, upload_id, total_chunks,
            )

            combined_text = "\n\n".join(chunk.text for chunk in chunks)
            entities = await self._graph_extractor.extract(combined_text, source_document_id=upload_id)
            self._raise_if_cancelled(job_id)

            # ── Stage 6: Uploading graph ──────────────────────
            self._set_stage(job_id, "uploading-graph", "Uploading graph entities", 0.9)
            entities_bytes = json.dumps(entities).encode("utf-8")
            self._raise_if_cancelled(job_id)
            await self._api_client.upload_artifact(
                entities_bytes, upload_id, "graph-entities", "application/json"
            )
            self._raise_if_cancelled(job_id)
            logger.info(
                "PDF job graph entities uploaded job_id=%s upload_id=%s byte_count=%d",
                job_id, upload_id, len(entities_bytes),
            )

            # ── Stage 7: Completed ────────────────────────────
            self._set_stage(
                job_id,
                "completed",
                f"Processed {len(records)} chunks from PDF",
                1.0,
                chunks_processed=len(records),
                total_chunks=total_chunks,
            )
            _jobs[job_id].update({
                "status": "completed",
                "stage": "completed",
                "stage_index": len(PIPELINE_STAGES) - 1,
                "progress": 1.0,
                "message": f"Processed {len(records)} chunks from PDF",
                "chunks_processed": len(records),
                "total_chunks": total_chunks,
                "updated_at": datetime.now(timezone.utc).isoformat(),
            })
            logger.info(
                "PDF job completed job_id=%s upload_id=%s chunk_count=%d",
                job_id, upload_id, len(records),
            )

        except asyncio.CancelledError:
            self._mark_cancelled(job_id)
            await self._report_cancelled(job_id)
            logger.info("PDF processing cancelled for upload %s", upload_id)
        except Exception as exc:
            logger.exception("PDF processing failed for upload %s", upload_id)
            _jobs[job_id].update({
                **_format_safe_failure(exc),
                "progress": _jobs[job_id].get("progress", 0.0),
                "updated_at": datetime.now(timezone.utc).isoformat(),
            })
            if self._api_client.is_configured():
                try:
                    _ = asyncio.create_task(
                        self._api_client.report_stage(
                            job_id,
                            _jobs[job_id].get("stage", "processing"),
                            chunks_processed=_jobs[job_id].get("chunks_processed", 0),
                            total_chunks=_jobs[job_id].get("total_chunks", 0),
                            failure_reason=_jobs[job_id].get("error"),
                        )
                    )
                except TypeError:
                    pass

    async def _resolve_source_pdf_path(
        self,
        upload_id: str,
        document_type: str,
        blob_container: str,
        source_access_token: str | None,
        local_file_path: str | None,
    ) -> Path:
        if local_file_path:
            source_path = Path(local_file_path).expanduser().resolve(strict=True)
            if not source_path.is_file():
                raise RuntimeError("Local PDF source path is not a file.")
            logger.info("Using local PDF source for upload %s.", upload_id)
            return source_path

        if source_access_token:
            logger.info(
                "Downloading source for upload %s via API access token.", upload_id
            )
            pdf_bytes = await self._api_client.download_source(
                upload_id, document_type, source_access_token
            )
            return await self._write_temp_pdf(upload_id, pdf_bytes)

        if self._api_client.is_configured():
            logger.info(
                "Downloading source for upload %s via API machine credentials.", upload_id
            )
            pdf_bytes = await self._api_client.download_source(upload_id, document_type)
            return await self._write_temp_pdf(upload_id, pdf_bytes)

        raise RuntimeError(
            "source_access_token is required for PDF ingestion. Restart the MotorcycleRAG "
            "API and local processor, then retry the upload."
        )

    async def _write_temp_pdf(self, upload_id: str, pdf_bytes: bytes) -> Path:
        def write_file() -> Path:
            temp_dir = Path(os.getenv("LOCAL_PROCESSOR_TEMP_DIR", "/tmp"))
            temp_dir.mkdir(parents=True, exist_ok=True)
            path = temp_dir / f"{upload_id}.source.pdf"
            path.write_bytes(pdf_bytes)
            return path

        path = await asyncio.to_thread(write_file)
        logger.info(
            "PDF job temporary file created upload_id=%s byte_count=%d",
            upload_id,
            len(pdf_bytes),
        )
        return path
