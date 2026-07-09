"""Unit tests for watch-folder manifest parsing (``watch_folder._read_manifest``).

Focuses on the ``source_path`` field — the original uploader file path that is
forwarded to the metadata extractor as LLM context.
"""

import json
from pathlib import Path
from typing import Any
from unittest.mock import AsyncMock, MagicMock

from watch_folder import WatchFolderManifest, WatchFolderWorker, _read_manifest


def _base_payload(**overrides) -> dict[str, Any]:
    payload = {
        "jobId": "job-1",
        "uploadId": "upload-1",
        "processorRunId": "run-1",
        "documentType": "manual-pdf",
        "sourceFileName": "service-manual.pdf",
        "localFileName": "paired-abc.pdf",
        "metadata": {},
    }
    payload.update(overrides)
    return payload


def _write_manifest(tmp_path: Path, payload: dict[str, Any]) -> Path:
    manifest_dir = tmp_path / "manifests"
    manifest_dir.mkdir(parents=True, exist_ok=True)
    manifest_path = manifest_dir / "manifest.json"
    manifest_path.write_text(json.dumps(payload), encoding="utf-8")
    return manifest_path


class TestReadManifestSourcePath:
    def test_reads_camel_case_source_path(self, tmp_path):
        manifest_path = _write_manifest(
            tmp_path,
            _base_payload(sourcePath="/data/manuals/2023/Honda/CBR600RR/service-manual.pdf"),
        )

        manifest = _read_manifest(manifest_path, tmp_path)

        assert isinstance(manifest, WatchFolderManifest)
        assert manifest.source_path == "/data/manuals/2023/Honda/CBR600RR/service-manual.pdf"

    def test_reads_snake_case_source_path_alias(self, tmp_path):
        """The snake_case key is accepted as a fallback for robustness."""
        manifest_path = _write_manifest(
            tmp_path,
            _base_payload(source_path="/home/user/bike.pdf"),
        )

        manifest = _read_manifest(manifest_path, tmp_path)

        assert manifest.source_path == "/home/user/bike.pdf"

    def test_source_path_defaults_to_none_when_omitted(self, tmp_path):
        """Older manifests without source_path parse to None (back-compat)."""
        manifest_path = _write_manifest(tmp_path, _base_payload())

        manifest = _read_manifest(manifest_path, tmp_path)

        assert manifest.source_path is None

    def test_blank_source_path_treated_as_none(self, tmp_path):
        manifest_path = _write_manifest(tmp_path, _base_payload(sourcePath="   "))

        manifest = _read_manifest(manifest_path, tmp_path)

        assert manifest.source_path is None


class TestStartManifestForwardsSourceFileName:
    """Verifies the manifest's source_file_name reaches the PDF processor.

    The original filename is written to each chunk's ``sourceFile`` field so
    search results can display which manual a result came from.
    """

    async def test_source_file_name_forwarded_to_pdf_processor(self, tmp_path):
        source_bytes = b"%PDF-1.4 fake"
        payload = _base_payload(
            sourceFileName="2023 Honda CBR600RR Service Manual.pdf",
            sizeBytes=len(source_bytes),
        )
        manifest_path = _write_manifest(tmp_path, payload)
        # Create the paired source file under files/.
        files_dir = tmp_path / "files"
        files_dir.mkdir(parents=True, exist_ok=True)
        (files_dir / "paired-abc.pdf").write_bytes(source_bytes)

        mock_pdf = MagicMock()
        mock_pdf.process_pdf_async = AsyncMock(return_value="run-1")
        mock_csv = MagicMock()
        worker = WatchFolderWorker(tmp_path, mock_pdf, mock_csv)

        await worker._start_manifest(manifest_path)

        mock_pdf.process_pdf_async.assert_awaited_once()
        kwargs = mock_pdf.process_pdf_async.await_args.kwargs
        assert (
            kwargs["source_file_name"]
            == "2023 Honda CBR600RR Service Manual.pdf"
        )
        assert kwargs["source_path"] is None  # omitted in the base payload
