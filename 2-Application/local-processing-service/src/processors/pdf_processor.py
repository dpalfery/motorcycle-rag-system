"""PDF processor using Docling HybridChunker for semantic chunking."""

import asyncio
import json
import logging
import math
import os
import time
import uuid
from pathlib import Path
from types import SimpleNamespace
from datetime import datetime, timezone

# HybridChunker is re-exported through ``docling.chunking``, but that shim uses
# bare ``from X import Y`` re-exports which type checkers (Pyright/Pylance) treat
# as private side-effect imports, producing a "cannot resolve" / reportPrivateImportUsage
# warning. Import from the canonical docling_core path instead — same class, resolvable.
from docling.document_converter import DocumentConverter
from docling_core.transforms.chunker.doc_chunk import DocMeta
from docling_core.transforms.chunker.hybrid_chunker import HybridChunker

from typing import Any, cast
from api.api_client import ApiClient
from embeddings.tokenizer_provider import get_pdf_chunker_tokenizer
from extraction.graph_extractor import GraphExtractor
from extraction.metadata_extractor import MetadataExtractor
from security.log_sanitizer import sanitize_log_value
from storage.blob_writer import BlobWriter

logger = logging.getLogger(__name__)
# "awaiting-metadata" is a non-terminal pause state (job paused for manual
# metadata entry) and must be preserved by clear_terminal_jobs().
_ACTIVE_JOB_STATUSES = {
    "queued",
    "processing",
    "running",
    "inprogress",
    "awaiting-metadata",
}

PDF_CHUNKER_MAX_TOKENS = int(os.getenv("PDF_CHUNKER_MAX_TOKENS", "512"))

BATCH_TOKEN_BUDGET = 4000  # must match graph_extractor.py's constant

PIPELINE_STAGES = [
    "copying",
    "parsing",
    "extracting-metadata",
    "chunking",
    "embedding",
    "uploading-chunks",
    "extracting-graph",
    "uploading-graph",
    "completed",
]

_jobs: dict[str, dict[str, Any]] = {}
_tasks: dict[str, asyncio.Task[None]] = {}


def _make_job(job_id: str, upload_id: str, document_type: str) -> dict[str, Any]:
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
        detail = str(exc).strip()
        message = (
            f"PDF processing failed: {detail[:300]}"
            if detail
            else f"PDF processing failed: {type(exc).__name__}"
        )

    return {"status": "failed", "message": message, "error": message}


def _merge_metadata(base: Any, extracted: dict[str, Any]) -> SimpleNamespace:
    """Merge LLM-extracted metadata onto the request metadata object.

    Non-empty extracted fields override the base values so search chunks carry
    the most specific metadata available. Returns a SimpleNamespace so the
    chunk-building loop can read make/model/year/category/tags uniformly
    regardless of whether ``base`` was a pydantic ``Metadata`` model or a test
    mock.
    """
    return SimpleNamespace(
        make=str(extracted.get("make") or getattr(base, "make", "") or ""),
        model=str(extracted.get("model") or getattr(base, "model", "") or ""),
        year=extracted.get("year") or getattr(base, "year", 0) or 0,
        category=extracted.get("category") or getattr(base, "category", None),
        tags=list(extracted.get("tags") or getattr(base, "tags", []) or []),
        document_type=getattr(base, "document_type", None),
        language=getattr(base, "language", None),
        custom=dict(getattr(base, "custom", {})),
    )


def _resolve_source_file_name(
    source_file_name: str | None,
    source_path: str | None,
    upload_id: str,
) -> str:
    """Resolve the display name for a chunk's ``sourceFile`` field.

    Search results show this value so users can see which manual a chunk came
    from (e.g. "2023 Honda CBR600RR Service Manual.pdf"). Prefers the explicit
    basename from the watch-folder manifest (``source_file_name``), then the
    basename of the original file path (``source_path``), and finally falls
    back to the opaque ``upload_id`` so older/API-direct ingest paths keep
    working. Only the basename is ever used — never a directory path.

    Args:
        source_file_name: Basename from the watch-folder manifest, if any.
        source_path: Original uploader file path, if any.
        upload_id: Opaque upload identifier used as the last-resort fallback.

    Returns:
        The resolved source file name (always a bare filename or upload_id).
    """
    if source_file_name:
        base = os.path.basename(source_file_name).strip()
        if base:
            return base
    if source_path:
        base = os.path.basename(source_path).strip()
        if base:
            return base
    return upload_id


class PDFProcessor:
    def __init__(
        self,
        blob_writer: BlobWriter,
        embedder: Any,
        graph_extractor: GraphExtractor,
        metadata_extractor: MetadataExtractor,
        api_client: ApiClient,
    ):
        self._blob_writer = blob_writer
        self._embedder = embedder
        self._graph_extractor = graph_extractor
        self._metadata_extractor = metadata_extractor
        self._api_client = api_client
        self._stage_timers: dict[str, float] = {}
        self._current_stage: str | None = None

    async def process_pdf_async(
        self,
        upload_id: str,
        document_type: str,
        blob_container: str,
        metadata,
        source_access_token: str | None = None,
        local_file_path: str | None = None,
        job_id: str | None = None,
        source_path: str | None = None,
        source_file_name: str | None = None,
    ) -> str:
        """Returns job_id immediately, fires background task via asyncio.create_task.

        If called with a ``job_id`` whose job is currently paused awaiting manual
        metadata, the job is resumed: parsing re-runs and chunking proceeds with
        the supplied (manual) metadata, skipping the LLM extraction stage.

        Args:
            source_path: Optional original file path of the source document. It
                is forwarded to the metadata extractor as leading LLM context
                (directory names often encode year/make/model). Not used for
                file IO; that is ``local_file_path``.
            source_file_name: Optional basename of the original source file
                (e.g. "2023 Honda CBR600RR Service Manual.pdf"). Written to each
                chunk's ``sourceFile`` field so search results can show which
                manual a result came from. When omitted the basename of
                ``source_path`` is used, falling back to ``upload_id``.
        """
        job_id = job_id or str(uuid.uuid4())
        existing = _jobs.get(job_id)
        is_resume = existing is not None and bool(existing.get("paused_for_metadata"))

        if is_resume:
            # Reuse the existing job (preserving its stage history) and clear the
            # pause markers so a subsequent call is treated as a fresh start.
            job = _jobs[job_id]
            job["status"] = "processing"
            job["stage"] = "copying"
            job["paused_for_metadata"] = False
            job.pop("extracted_metadata", None)
            logger.info(
                "component=pdf_processor job_id=%s message=resuming after manual metadata",
                sanitize_log_value(job_id),
            )
        else:
            _jobs[job_id] = _make_job(job_id, upload_id, document_type)

        logger.info(
            "component=pdf_processor job_id=%s upload_id=%s document_type=%s blob_container=%s "
            "has_source_access_token=%s has_local_file=%s resume=%s message=job queued",
            sanitize_log_value(job_id),
            sanitize_log_value(upload_id),
            sanitize_log_value(document_type),
            sanitize_log_value(blob_container),
            bool(source_access_token),
            bool(local_file_path),
            is_resume,
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
                is_resume=is_resume,
                source_path=source_path,
                source_file_name=source_file_name,
            )
        )
        _tasks[job_id] = task

        def _on_done(task, jid=job_id):
            # Only pop if this is still the registered task; a resumed job may
            # have registered a newer task with the same job_id.
            if _tasks.get(jid) is task:
                _tasks.pop(jid, None)

        task.add_done_callback(_on_done)
        return job_id

    async def get_job_status(self, job_id: str) -> dict[str, Any] | None:
        return _jobs.get(job_id)

    async def list_jobs(self) -> list[dict[str, Any]]:
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

    async def stop_job(self, job_id: str) -> dict[str, Any] | None:
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
        logger.info(
            "component=pdf_processor job_id=%s message=job cancelled",
            sanitize_log_value(job_id),
        )
        return _jobs.get(job_id)

    def _mark_cancelled(self, job_id: str) -> None:
        job = _jobs.get(job_id)
        if not job:
            return
        now = datetime.now(timezone.utc).isoformat()
        job.update(
            {
                "status": "cancelled",
                "stage": "cancelled",
                "message": "Cancelled by user",
                "progress": job.get("progress", 0.0),
                "updated_at": now,
            }
        )
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
                "component=pdf_processor job_id=%s "
                "message=failed to report terminal processor failure error=%s",
                sanitize_log_value(job_id),
                sanitize_log_value(str(exc)),
            )

    async def _report_paused(self, job_id: str, failure_reason: str) -> None:
        """Report the needs-manual-metadata pause to the API (never raises)."""
        if not self._api_client.is_configured():
            return
        try:
            job = _jobs.get(job_id, {})
            await self._api_client.report_stage(
                job_id,
                "needs-manual-metadata",
                chunks_processed=job.get("chunks_processed", 0),
                total_chunks=job.get("total_chunks", 0),
                failure_reason=failure_reason,
            )
        except TypeError:
            pass
        except Exception as exc:
            logger.warning(
                "component=pdf_processor job_id=%s "
                "message=failed to report needs-manual-metadata error=%s",
                sanitize_log_value(job_id),
                sanitize_log_value(str(exc)),
            )

    def _mark_paused_for_metadata(
        self, job_id: str, extracted: dict[str, Any], message: str
    ) -> None:
        """Mark a job paused awaiting manual metadata (no API report)."""
        job = _jobs[job_id]
        now = datetime.now(timezone.utc).isoformat()
        job.update(
            {
                "status": "awaiting-metadata",
                "stage": "needs-manual-metadata",
                "message": message,
                "progress": 0.10,
                "paused_for_metadata": True,
                "extracted_metadata": extracted,
                "updated_at": now,
            }
        )
        self._append_stage_history(job, "needs-manual-metadata", message, now)

    def _extract_page_texts(self, document: Any) -> list[str]:
        """Extract per-page text from a parsed Docling document for metadata sampling.

        Returns up to ``MetadataExtractor.PAGE_SAMPLE_SIZES[-1]`` (3) page
        strings. Docling exposes ``document.pages`` as a ``dict[int, PageItem]``
        keyed by 1-based page number and ``document.export_to_text(page_no=N)``
        for per-page text. Returns an empty list when the document exposes no
        pages or text export is unavailable, so the extractor falls back to
        manual metadata entry.
        """
        cap = self._metadata_extractor.PAGE_SAMPLE_SIZES[-1]
        try:
            page_numbers = sorted(document.pages.keys())
        except Exception:
            return []

        pages: list[str] = []
        for page_no in page_numbers[:cap]:
            try:
                text = document.export_to_text(page_no=page_no)
            except Exception:
                continue
            if text:
                pages.append(str(text))
        return pages

    def _mark_failed(self, job_id: str, message: str, error: str | None = None) -> None:
        job = _jobs[job_id]
        now = datetime.now(timezone.utc).isoformat()
        failure = error or message
        job.update(
            {
                "status": "failed",
                "stage": "failed",
                "message": message,
                "error": failure,
                "progress": job.get("progress", 0.0),
                "updated_at": now,
            }
        )
        self._append_stage_history(job, "failed", message, now)

    def _set_stage(
        self, job_id: str, stage: str, message: str, progress: float, **extra
    ) -> None:
        # Log previous stage duration on stage transition
        if self._current_stage is not None and self._current_stage != stage:
            elapsed_ms = int(
                (time.perf_counter() - self._stage_timers.get(self._current_stage, 0))
                * 1000
            )
            logger.info(
                "component=pdf_processor job_id=%s stage=%s elapsed_ms=%d",
                sanitize_log_value(job_id),
                sanitize_log_value(self._current_stage),
                elapsed_ms,
            )

        # Record new stage start time
        if self._current_stage != stage:
            self._current_stage = stage
            self._stage_timers[stage] = time.perf_counter()

        job = _jobs[job_id]
        try:
            stage_index = PIPELINE_STAGES.index(stage)
        except ValueError:
            stage_index = job.get("stage_index", 0)
        now = datetime.now(timezone.utc).isoformat()
        job.update(
            {
                "stage": stage,
                "stage_index": stage_index,
                "message": message,
                "progress": progress,
                "updated_at": now,
                **extra,
            }
        )
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
    def _append_stage_history(
        job: dict[str, Any], stage: str, message: str, set_at: str
    ) -> None:
        history = job.setdefault("stage_history", [])
        if history and history[-1].get("stage") == stage:
            history[-1].update(
                {
                    "message": message,
                    "progress": job.get("progress", 0.0),
                    "chunks_processed": job.get("chunks_processed", 0),
                    "total_chunks": job.get("total_chunks", 0),
                    "set_at": set_at,
                }
            )
            return

        history.append(
            {
                "stage": stage,
                "message": message,
                "progress": job.get("progress", 0.0),
                "chunks_processed": job.get("chunks_processed", 0),
                "total_chunks": job.get("total_chunks", 0),
                "set_at": set_at,
            }
        )

    async def _process_pdf(
        self,
        job_id: str,
        upload_id: str,
        document_type: str,
        blob_container: str,
        metadata,
        source_access_token: str | None = None,
        local_file_path: str | None = None,
        is_resume: bool = False,
        source_path: str | None = None,
        source_file_name: str | None = None,
    ) -> None:
        """Background coroutine that downloads, chunks, embeds, extracts, and uploads.

        When ``is_resume`` is True the job was previously paused awaiting manual
        metadata; parsing re-runs but the LLM extraction stage is skipped in
        favour of the metadata supplied with the resume request.
        """
        try:
            logger.info(
                "component=pdf_processor job_id=%s upload_id=%s document_type=%s message=job started",
                sanitize_log_value(job_id),
                sanitize_log_value(upload_id),
                sanitize_log_value(document_type),
            )

            # ── Stage 0: Copying ──────────────────────────────
            self._set_stage(job_id, "copying", "Preparing source PDF", 0.0)

            pdf_file_path = await self._resolve_source_pdf_path(
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
            result = await asyncio.to_thread(converter.convert, str(pdf_file_path))
            self._raise_if_cancelled(job_id)
            logger.info(
                "component=pdf_processor job_id=%s upload_id=%s message=Docling conversion completed",
                sanitize_log_value(job_id),
                sanitize_log_value(upload_id),
            )

            # ── Stage 2: Extracting metadata ─────────────────
            if is_resume:
                self._set_stage(
                    job_id,
                    "extracting-metadata",
                    "Resuming with manual metadata",
                    0.08,
                )
                logger.info(
                    "component=pdf_processor job_id=%s message=resuming after manual metadata, skipping LLM extraction",
                    sanitize_log_value(job_id),
                )
            else:
                self._set_stage(
                    job_id,
                    "extracting-metadata",
                    "Extracting metadata from PDF pages",
                    0.08,
                )
                page_texts = self._extract_page_texts(result.document)
                metadata_result = await self._metadata_extractor.extract(
                    page_texts, job_id=job_id, source_path=source_path
                )
                self._raise_if_cancelled(job_id)

                if metadata_result["fill_rate"] >= 1.0:
                    metadata = _merge_metadata(metadata, metadata_result)
                    logger.info(
                        "component=pdf_processor job_id=%s fill_rate=%.2f pages_sampled=%d message=metadata extracted",
                        sanitize_log_value(job_id),
                        metadata_result["fill_rate"],
                        metadata_result["pages_sampled"],
                    )
                else:
                    # Pause for manual metadata entry and stop processing.
                    failure_reason = (
                        f"Metadata extraction incomplete after "
                        f"{metadata_result['pages_sampled']} pages "
                        f"(fill rate {metadata_result['fill_rate']:.0%}). "
                        f"Manual entry required."
                    )
                    self._mark_paused_for_metadata(
                        job_id, metadata_result, failure_reason
                    )
                    await self._report_paused(job_id, failure_reason)
                    logger.info(
                        "component=pdf_processor job_id=%s fill_rate=%.2f pages_sampled=%d message=paused for manual metadata",
                        sanitize_log_value(job_id),
                        metadata_result["fill_rate"],
                        metadata_result["pages_sampled"],
                    )
                    return  # Coroutine ends; job awaits resume.

            # ── Stage 3: Chunking ─────────────────────────────
            self._set_stage(job_id, "chunking", "Loading chunker tokenizer", 0.1)
            logger.info(
                "component=pdf_processor job_id=%s upload_id=%s max_tokens=%d message=loading chunker tokenizer",
                sanitize_log_value(job_id),
                sanitize_log_value(upload_id),
                PDF_CHUNKER_MAX_TOKENS,
            )
            tokenizer = get_pdf_chunker_tokenizer(PDF_CHUNKER_MAX_TOKENS)
            logger.info(
                "component=pdf_processor job_id=%s upload_id=%s tokenizer_class=%s message=tokenizer loaded",
                sanitize_log_value(job_id),
                sanitize_log_value(upload_id),
                sanitize_log_value(type(tokenizer).__name__),
            )

            self._set_stage(job_id, "chunking", "Chunking document", 0.12)
            chunker = HybridChunker(tokenizer=tokenizer)
            chunks = list(await asyncio.to_thread(chunker.chunk, result.document))
            self._raise_if_cancelled(job_id)
            total_chunks = len(chunks)
            logger.info(
                "component=pdf_processor job_id=%s upload_id=%s chunk_count=%d message=chunking completed",
                sanitize_log_value(job_id),
                sanitize_log_value(upload_id),
                total_chunks,
            )

            if not chunks:
                logger.warning(
                    "component=pdf_processor job_id=%s upload_id=%s message=no chunks extracted",
                    sanitize_log_value(job_id),
                    sanitize_log_value(upload_id),
                )
                _jobs[job_id]["progress"] = 1.0
                self._mark_failed(job_id, "No chunks extracted from PDF")
                await self._report_failed(job_id, "No chunks extracted from PDF")
                return

            # ── Stage 4: Embedding ────────────────────────────
            # Resolve the human-readable source file name once so every chunk
            # carries the original filename (e.g. "2023 Honda CBR600RR Service
            # Manual.pdf") instead of the opaque upload_id.
            resolved_source_file = _resolve_source_file_name(
                source_file_name, source_path, upload_id
            )
            records: list[dict[str, Any]] = []
            for i, chunk in enumerate(chunks):
                self._raise_if_cancelled(job_id)
                # HybridChunker yields DocChunk objects whose ``meta`` is a DocMeta
                # (a BaseMeta subclass that exposes headings/doc_items). The declared
                # return type is BaseChunk/BaseMeta, so narrow once for the checker.
                meta = cast(DocMeta, chunk.meta)
                headings = list(meta.headings) if meta.headings else []
                has_prov = bool(meta.doc_items) and bool(meta.doc_items[0].prov)
                page_no = meta.doc_items[0].prov[0].page_no if has_prov else 0
                embedding = await self._embedder.generate_embedding(chunk.text)
                self._raise_if_cancelled(job_id)

                if i == 0 or (i + 1) == total_chunks or (i + 1) % 10 == 0:
                    logger.info(
                        "component=pdf_processor job_id=%s upload_id=%s chunk_index=%d total_chunks=%d message=embedding progress",
                        sanitize_log_value(job_id),
                        sanitize_log_value(upload_id),
                        i + 1,
                        total_chunks,
                    )

                make = str(getattr(metadata, "make", "") or "")
                model = str(getattr(metadata, "model", "") or "")
                year_val = getattr(metadata, "year", 0) or 0
                try:
                    year = int(year_val)
                except (TypeError, ValueError):
                    year = 0
                category = getattr(metadata, "category", None) or "Unknown"
                base_tags = [t for t in [make, model] if t]
                tags = list(getattr(metadata, "tags", None) or base_tags)

                now_ts = datetime.now(timezone.utc).isoformat()
                records.append(
                    {
                        "id": f"{upload_id}-pdf-{i}",
                        "title": headings[0] if headings else f"Chunk {i}",
                        "content": chunk.text,
                        "documentType": document_type,
                        "category": category,
                        "make": make,
                        "model": model,
                        "year": year,
                        "sourceFile": resolved_source_file,
                        "section": headings[0] if headings else "",
                        "pageNumber": page_no,
                        "pageRange": str(page_no) if page_no else "0",
                        "primarySection": headings[0] if headings else "",
                        "sectionLevel": len(headings),
                        "sectionHeadings": headings,
                        "tableCaption": None,
                        "chunkIndex": i,
                        "tags": tags,
                        "contentVector": embedding,
                        "createdAt": now_ts,
                        "updatedAt": now_ts,
                    }
                )

                self._set_stage(
                    job_id,
                    "embedding",
                    f"Embedding chunk {i + 1} of {total_chunks}",
                    round(0.2 + len(records) / total_chunks * 0.3, 2),
                    chunks_processed=len(records),
                    total_chunks=total_chunks,
                )

            # ── Stage 5: Uploading chunks ─────────────────────
            self._set_stage(
                job_id,
                "uploading-chunks",
                f"Uploading {len(records)} search chunks",
                0.55,
                chunks_processed=len(records),
                total_chunks=total_chunks,
            )
            logger.info(
                "component=pdf_processor job_id=%s upload_id=%s record_count=%d message=uploading search chunks",
                sanitize_log_value(job_id),
                sanitize_log_value(upload_id),
                len(records),
            )

            self._raise_if_cancelled(job_id)
            chunks_bytes = ("\n".join(json.dumps(r) for r in records)).encode("utf-8")
            await self._api_client.upload_artifact(
                chunks_bytes, upload_id, "search-chunks", "application/x-ndjson"
            )
            self._raise_if_cancelled(job_id)
            logger.info(
                "component=pdf_processor job_id=%s upload_id=%s byte_count=%d message=search chunks uploaded",
                sanitize_log_value(job_id),
                sanitize_log_value(upload_id),
                len(chunks_bytes),
            )

            # ── Stage 6: Extracting graph entities ────────────
            self._set_stage(
                job_id,
                "extracting-graph",
                f"Extracting graph entities from {total_chunks} chunks",
                0.8,
            )
            logger.info(
                "component=pdf_processor job_id=%s upload_id=%s chunk_count=%d message=extracting graph entities",
                sanitize_log_value(job_id),
                sanitize_log_value(upload_id),
                total_chunks,
            )

            combined_text = "\n\n".join(chunk.text for chunk in chunks)
            combined_chars = len(combined_text)
            combined_tokens_approx = combined_chars // 4
            expected_batches = max(
                1, math.ceil(combined_chars / (BATCH_TOKEN_BUDGET * 4))
            )
            logger.info(
                "component=pdf_processor job_id=%s stage=extracting_graph "
                "combined_chars=%d combined_tokens_approx=%d expected_batches=%d",
                sanitize_log_value(job_id),
                combined_chars,
                combined_tokens_approx,
                expected_batches,
            )
            logger.debug(
                "component=pdf_processor job_id=%s combined_text_preview=%s",
                sanitize_log_value(job_id),
                sanitize_log_value(
                    combined_text[:2000]
                    + ("...[truncated]" if len(combined_text) > 2000 else "")
                ),
            )

            entities = await self._graph_extractor.extract(
                combined_text, source_document_id=upload_id
            )
            self._raise_if_cancelled(job_id)

            if entities and isinstance(entities, list) and len(entities) > 0:
                node_count = len(entities[0].get("nodes", []))
                edge_count = len(entities[0].get("edges", []))
                logger.info(
                    "component=pdf_processor job_id=%s stage=extracting_graph "
                    "result=ok nodes=%d edges=%d",
                    sanitize_log_value(job_id),
                    node_count,
                    edge_count,
                )

            # ── Stage 7: Uploading graph ──────────────────────
            self._set_stage(job_id, "uploading-graph", "Uploading graph entities", 0.9)
            entities_bytes = json.dumps(entities).encode("utf-8")
            self._raise_if_cancelled(job_id)
            await self._api_client.upload_artifact(
                entities_bytes, upload_id, "graph-entities", "application/json"
            )
            self._raise_if_cancelled(job_id)
            logger.info(
                "component=pdf_processor job_id=%s upload_id=%s byte_count=%d message=graph entities uploaded",
                sanitize_log_value(job_id),
                sanitize_log_value(upload_id),
                len(entities_bytes),
            )

            # ── Stage 8: Completed ────────────────────────────
            self._set_stage(
                job_id,
                "completed",
                f"Processed {len(records)} chunks from PDF",
                1.0,
                chunks_processed=len(records),
                total_chunks=total_chunks,
            )
            _jobs[job_id].update(
                {
                    "status": "completed",
                    "stage": "completed",
                    "stage_index": len(PIPELINE_STAGES) - 1,
                    "progress": 1.0,
                    "message": f"Processed {len(records)} chunks from PDF",
                    "chunks_processed": len(records),
                    "total_chunks": total_chunks,
                    "updated_at": datetime.now(timezone.utc).isoformat(),
                }
            )
            logger.info(
                "component=pdf_processor job_id=%s upload_id=%s chunk_count=%d message=job completed",
                sanitize_log_value(job_id),
                sanitize_log_value(upload_id),
                len(records),
            )

        except asyncio.CancelledError:
            self._mark_cancelled(job_id)
            await self._report_cancelled(job_id)
            logger.info(
                "component=pdf_processor job_id=%s upload_id=%s message=processing cancelled",
                sanitize_log_value(job_id),
                sanitize_log_value(upload_id),
            )
        except Exception as exc:
            failure = _format_safe_failure(exc)
            logger.error(
                "component=pdf_processor job_id=%s upload_id=%s "
                "message=processing failed error=%s",
                sanitize_log_value(job_id),
                sanitize_log_value(upload_id),
                sanitize_log_value(failure["error"]),
            )
            self._mark_failed(job_id, failure["message"], failure["error"])
            await self._report_failed(job_id, failure["error"])

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
            logger.info(
                "component=pdf_processor upload_id=%s message=using local PDF source",
                sanitize_log_value(upload_id),
            )
            return source_path

        if source_access_token:
            logger.info(
                "component=pdf_processor upload_id=%s message=downloading source via API access token",
                sanitize_log_value(upload_id),
            )
            pdf_bytes = await self._api_client.download_source(
                upload_id, document_type, source_access_token
            )
            return await self._write_temp_pdf(upload_id, pdf_bytes)

        if self._api_client.is_configured():
            logger.info(
                "component=pdf_processor upload_id=%s message=downloading source via API machine credentials",
                sanitize_log_value(upload_id),
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
            "component=pdf_processor upload_id=%s byte_count=%d message=temporary file created",
            sanitize_log_value(upload_id),
            len(pdf_bytes),
        )
        return path
