"""Watch-folder ingestion worker for local-first processing."""

from __future__ import annotations

import asyncio
import json
import logging
import os
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from models.schemas import Metadata
from processors.csv_processor import CSVProcessor
from processors.pdf_processor import PDFProcessor

logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class WatchFolderManifest:
    job_id: str
    upload_id: str
    processor_run_id: str
    document_type: str
    source_file_name: str
    # Original file path on the uploader's system (e.g.
    # "/data/manuals/2023/Honda/CBR600RR/service-manual.pdf"). Optional — older
    # manifests may omit it. Forwarded to the metadata extractor as LLM context.
    source_path: str | None
    local_file_name: str
    size_bytes: int | None
    created_at_utc: str | None
    metadata: Metadata


class WatchFolderWorker:
    """Polls a local folder for manifest+file pairs and starts processor jobs."""

    def __init__(
        self,
        watch_folder: Path,
        pdf_processor: PDFProcessor,
        csv_processor: CSVProcessor,
        *,
        poll_interval_seconds: float = 1.0,
    ) -> None:
        self._watch_folder = watch_folder
        self._pdf_processor = pdf_processor
        self._csv_processor = csv_processor
        self._poll_interval_seconds = poll_interval_seconds
        self._stop_event = asyncio.Event()
        self._task: asyncio.Task[None] | None = None

    @property
    def watch_folder(self) -> Path:
        return self._watch_folder

    def start(self) -> None:
        if self._task and not self._task.done():
            return

        self._watch_folder.mkdir(parents=True, exist_ok=True)
        self._task = asyncio.create_task(self._run(), name="watch-folder-worker")
        logger.info("Watch-folder worker started path=%s", self._watch_folder)

    async def stop(self) -> None:
        self._stop_event.set()
        if self._task:
            await self._task

    async def _run(self) -> None:
        while not self._stop_event.is_set():
            try:
                await self._process_available_manifests()
            except Exception:
                logger.exception("Unexpected watch-folder scan failure")

            try:
                await asyncio.wait_for(
                    self._stop_event.wait(),
                    timeout=self._poll_interval_seconds,
                )
            except asyncio.TimeoutError:
                pass

    async def _process_available_manifests(self) -> None:
        manifest_folder = _manifest_folder(self._watch_folder)
        manifest_folder.mkdir(parents=True, exist_ok=True)
        _files_folder(self._watch_folder).mkdir(parents=True, exist_ok=True)

        for manifest_path in sorted(manifest_folder.glob("*.json")):
            claimed_path = manifest_path.with_suffix(".processing")
            try:
                manifest_path.rename(claimed_path)
            except FileNotFoundError:
                continue
            except OSError as exc:
                logger.warning(
                    "Could not claim watch-folder manifest %s: %s",
                    manifest_path.name,
                    exc,
                )
                continue

            try:
                await self._start_manifest(claimed_path)
                accepted_path = claimed_path.with_suffix(".accepted")
                claimed_path.rename(accepted_path)
            except Exception as exc:
                logger.exception(
                    "Watch-folder manifest failed before processing could start: %s",
                    claimed_path.name,
                )
                failed_path = claimed_path.with_suffix(".failed")
                _write_failure_marker(failed_path, exc)

    async def _start_manifest(self, manifest_path: Path) -> None:
        manifest = _read_manifest(manifest_path, self._watch_folder)
        local_source_path = _resolve_source_path(self._watch_folder, manifest.local_file_name)
        if not local_source_path.is_file():
            raise FileNotFoundError(f"Source file is missing for manifest {manifest_path.name}")

        if manifest.size_bytes is not None and local_source_path.stat().st_size != manifest.size_bytes:
            raise RuntimeError("Source file size does not match manifest.")

        logger.info(
            "Starting watch-folder job processor_run_id=%s upload_id=%s document_type=%s",
            manifest.processor_run_id,
            manifest.upload_id,
            manifest.document_type,
        )

        if manifest.document_type == "manual-pdf":
            await self._pdf_processor.process_pdf_async(
                upload_id=manifest.upload_id,
                document_type=manifest.document_type,
                blob_container="",
                metadata=manifest.metadata,
                local_file_path=str(local_source_path),
                job_id=manifest.processor_run_id,
                source_path=manifest.source_path,
                source_file_name=manifest.source_file_name,
            )
            return

        if manifest.document_type in {"spec-dataset", "bike-graph"}:
            await self._csv_processor.process_csv_async(
                upload_id=manifest.upload_id,
                metadata=manifest.metadata,
                local_file_path=str(local_source_path),
                job_id=manifest.processor_run_id,
            )
            return

        raise RuntimeError(f"Unsupported document type: {manifest.document_type}")


def _read_manifest(path: Path, watch_folder: Path) -> WatchFolderManifest:
    payload = json.loads(path.read_text(encoding="utf-8"))
    metadata_payload = payload.get("metadata")
    if not isinstance(metadata_payload, dict):
        metadata_payload = {}

    manifest = WatchFolderManifest(
        job_id=_required_string(payload, "jobId"),
        upload_id=_required_string(payload, "uploadId"),
        processor_run_id=_required_string(payload, "processorRunId"),
        document_type=_required_string(payload, "documentType"),
        source_file_name=_required_string(payload, "sourceFileName"),
        source_path=_optional_string(
            payload.get("sourcePath", payload.get("source_path"))
        ),
        local_file_name=_required_string_alias(
            payload,
            "localFileName",
            "pairedLocalFileName",
        ),
        size_bytes=_optional_int(payload.get("sizeBytes", payload.get("size"))),
        created_at_utc=_optional_string(payload.get("createdAtUtc")),
        metadata=Metadata(**metadata_payload),
    )

    _resolve_source_path(watch_folder, manifest.local_file_name)
    return manifest


def _resolve_source_path(watch_folder: Path, local_file_name: str) -> Path:
    if Path(local_file_name).name != local_file_name:
        raise RuntimeError("Manifest localFileName must be a file name, not a path.")

    root = _files_folder(watch_folder).resolve()
    source_path = (root / local_file_name).resolve()
    if root != source_path and root not in source_path.parents:
        raise RuntimeError("Manifest source path escapes the watch folder.")

    return source_path


def _required_string(payload: dict[str, Any], key: str) -> str:
    value = payload.get(key)
    if not isinstance(value, str) or not value.strip():
        raise RuntimeError(f"Manifest field '{key}' is required.")
    return value.strip()


def _required_string_alias(payload: dict[str, Any], *keys: str) -> str:
    for key in keys:
        value = payload.get(key)
        if isinstance(value, str) and value.strip():
            return value.strip()

    raise RuntimeError(f"One of these manifest fields is required: {', '.join(keys)}.")


def _optional_string(value: Any) -> str | None:
    return value.strip() if isinstance(value, str) and value.strip() else None


def _optional_int(value: Any) -> int | None:
    if value is None:
        return None
    if isinstance(value, int) and value >= 0:
        return value
    raise RuntimeError("Manifest sizeBytes must be a non-negative integer.")


def _write_failure_marker(path: Path, exc: Exception) -> None:
    payload = {
        "failedAtUtc": datetime.now(timezone.utc).isoformat(),
        "errorType": type(exc).__name__,
        "error": str(exc)[:500],
    }
    path.write_text(json.dumps(payload, indent=2), encoding="utf-8")


def _manifest_folder(watch_folder: Path) -> Path:
    return watch_folder / "manifests"


def _files_folder(watch_folder: Path) -> Path:
    return watch_folder / "files"


def get_watch_folder_from_env() -> Path:
    return Path(os.getenv("WATCH_FOLDER", "./input")).expanduser().resolve()
