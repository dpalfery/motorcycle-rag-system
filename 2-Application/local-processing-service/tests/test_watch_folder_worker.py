"""Unit tests for watch-folder worker lifecycle and malformed manifests."""

import asyncio
import json
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from watch_folder import (
    WatchFolderWorker,
    _files_folder,
    _manifest_folder,
    _optional_int,
    _read_manifest,
    _required_string,
    _required_string_alias,
    _resolve_source_path,
    _write_failure_marker,
    get_watch_folder_from_env,
)


def _payload(**overrides):
    payload = {
        "jobId": "job",
        "uploadId": "upload",
        "processorRunId": "run",
        "documentType": "spec-dataset",
        "sourceFileName": "specs.csv",
        "pairedLocalFileName": "paired.csv",
        "size": 4,
        "metadata": [],
    }
    payload.update(overrides)
    return payload


def _manifest(tmp_path, **overrides):
    path = _manifest_folder(tmp_path) / "job.json"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(_payload(**overrides)), encoding="utf-8")
    return path


def test_manifest_helpers_validate_required_and_optional_values(tmp_path):
    assert _required_string({"key": " value "}, "key") == "value"
    assert _required_string_alias({"second": " value "}, "first", "second") == "value"
    assert _optional_int(None) is None
    assert _optional_int(0) == 0
    with pytest.raises(RuntimeError, match="'key' is required"):
        _required_string({"key": " "}, "key")
    with pytest.raises(RuntimeError, match="One of these manifest fields"):
        _required_string_alias({}, "first", "second")
    with pytest.raises(RuntimeError, match="non-negative integer"):
        _optional_int(-1)


def test_path_helpers_reject_nested_names_and_read_environment(tmp_path, monkeypatch):
    assert _manifest_folder(tmp_path) == tmp_path / "manifests"
    assert _files_folder(tmp_path) == tmp_path / "files"
    with pytest.raises(RuntimeError, match="must be a file name"):
        _resolve_source_path(tmp_path, "../escape.pdf")

    monkeypatch.setenv("WATCH_FOLDER", str(tmp_path / "watch"))
    assert get_watch_folder_from_env() == (tmp_path / "watch").resolve()


def test_read_manifest_accepts_aliases_and_defaults_metadata(tmp_path):
    path = _manifest(tmp_path, createdAtUtc="now")

    parsed = _read_manifest(path, tmp_path)

    assert parsed.local_file_name == "paired.csv"
    assert parsed.size_bytes == 4
    assert parsed.created_at_utc == "now"
    assert parsed.metadata.make is None


def test_failure_marker_contains_bounded_diagnostics(tmp_path):
    path = tmp_path / "job.failed"

    _write_failure_marker(path, ValueError("x" * 600))
    payload = json.loads(path.read_text(encoding="utf-8"))

    assert payload["errorType"] == "ValueError"
    assert len(payload["error"]) == 500
    assert payload["failedAtUtc"]


async def test_start_manifest_dispatches_csv_and_validates_source(tmp_path):
    source = _files_folder(tmp_path) / "paired.csv"
    source.parent.mkdir(parents=True)
    source.write_bytes(b"data")
    path = _manifest(tmp_path)
    csv = MagicMock()
    csv.process_csv_async = AsyncMock(return_value="run")
    worker = WatchFolderWorker(tmp_path, MagicMock(), csv)

    await worker._start_manifest(path)

    csv.process_csv_async.assert_awaited_once()
    assert csv.process_csv_async.await_args.kwargs["job_id"] == "run"

    source.unlink()
    with pytest.raises(FileNotFoundError, match="Source file is missing"):
        await worker._start_manifest(path)


async def test_start_manifest_rejects_size_mismatch_and_unsupported_type(tmp_path):
    source = _files_folder(tmp_path) / "paired.csv"
    source.parent.mkdir(parents=True)
    source.write_bytes(b"wrong")
    worker = WatchFolderWorker(tmp_path, MagicMock(), MagicMock())

    with pytest.raises(RuntimeError, match="size does not match"):
        await worker._start_manifest(_manifest(tmp_path))

    source.write_bytes(b"data")
    with pytest.raises(RuntimeError, match="Unsupported document type"):
        await worker._start_manifest(
            _manifest(tmp_path, documentType="spreadsheet")
        )


async def test_process_available_manifests_accepts_success_and_marks_failure(tmp_path):
    good = _manifest(tmp_path)
    bad = _manifest_folder(tmp_path) / "bad.json"
    bad.write_text("{}", encoding="utf-8")
    worker = WatchFolderWorker(tmp_path, MagicMock(), MagicMock())

    async def start_manifest(path):
        if path.name == "bad.processing":
            raise RuntimeError("bad")

    worker._start_manifest = AsyncMock(side_effect=start_manifest)

    await worker._process_available_manifests()

    assert good.with_suffix(".accepted").exists()
    failure_files = list(_manifest_folder(tmp_path).glob("*.failed"))
    assert len(failure_files) == 1
    assert json.loads(failure_files[0].read_text())["error"] == "bad"


async def test_run_recovers_scan_error_and_stops_without_delay(tmp_path):
    worker = WatchFolderWorker(tmp_path, MagicMock(), MagicMock(), poll_interval_seconds=1)
    worker._process_available_manifests = AsyncMock(side_effect=RuntimeError("scan"))

    async def stop_on_wait():
        worker._stop_event.set()

    worker._stop_event.wait = AsyncMock(side_effect=stop_on_wait)
    await worker._run()

    worker._process_available_manifests.assert_awaited_once()


async def test_start_is_idempotent_and_stop_awaits_task(tmp_path):
    worker = WatchFolderWorker(tmp_path, MagicMock(), MagicMock())
    task = MagicMock()
    task.done.return_value = False
    worker._task = task

    with patch("watch_folder.asyncio.create_task") as create:
        worker.start()
    create.assert_not_called()
    assert worker.watch_folder == tmp_path

    completed = asyncio.create_task(asyncio.sleep(0))
    worker._task = completed
    await worker.stop()
    assert worker._stop_event.is_set()
