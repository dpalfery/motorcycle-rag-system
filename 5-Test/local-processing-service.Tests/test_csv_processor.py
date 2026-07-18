"""Unit tests for CSVProcessor — all external calls mocked."""

import asyncio
import logging
import uuid
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from processors.csv_processor import (
    CSVProcessor,
    _estimate_tokens,
    _format_row,
    _jobs,
    _split_text_into_chunks,
    _tasks,
)


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


@pytest.fixture(autouse=True)
def _clear_jobs():
    """Ensure the module-level _jobs dict is empty before/after each test."""
    _jobs.clear()
    _tasks.clear()
    yield
    _jobs.clear()
    _tasks.clear()


async def _wait_for_terminal_status(processor, job_id: str, timeout: float = 5.0):
    deadline = asyncio.get_running_loop().time() + timeout
    while asyncio.get_running_loop().time() < deadline:
        status = await processor.get_job_status(job_id)
        if status and status.get("status") in {"completed", "failed", "cancelled"}:
            return status
        await asyncio.sleep(0.01)
    raise TimeoutError(f"Job {job_id} did not reach a terminal state")


@pytest.fixture()
def blob_writer():
    bw = MagicMock()
    bw.download_blob = AsyncMock(return_value=b"make,model,year\nHonda,CB500,2020")
    bw.upload_jsonl = AsyncMock(return_value=None)
    return bw


@pytest.fixture()
def api_client():
    ac = MagicMock()
    ac.is_configured = MagicMock(return_value=False)
    ac.upload_artifact = AsyncMock(return_value=None)
    ac.report_stage = AsyncMock(return_value=None)
    return ac


@pytest.fixture()
def embedder():
    em = MagicMock()
    em.generate_embedding = AsyncMock(return_value=[0.1] * 1536)
    return em


@pytest.fixture()
def processor(blob_writer, embedder, api_client):
    return CSVProcessor(
        blob_writer=blob_writer, embedder=embedder, api_client=api_client
    )


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


class TestCSVProcessorInstantiation:
    def test_instantiates_with_mock_dependencies(
        self, blob_writer, embedder, api_client
    ):
        proc = CSVProcessor(
            blob_writer=blob_writer, embedder=embedder, api_client=api_client
        )
        assert proc.blob_writer is blob_writer
        assert proc.embedder is embedder


class TestCSVHelpers:
    def test_format_row_omits_missing_values_and_estimates_tokens(self):
        import pandas as pd

        assert (
            _format_row(pd.Series({"make": "Honda", "year": float("nan")}))
            == "make: Honda"
        )
        assert _estimate_tokens("12345678") == 2

    def test_split_text_returns_single_or_multiple_chunks(self):
        assert _split_text_into_chunks("short", 10) == ["short"]
        assert _split_text_into_chunks("12345678\nabcdefgh\nijklmnop", 3) == [
            "12345678",
            "abcdefgh",
            "ijklmnop",
        ]


class TestProcessCSVAsync:
    async def test_returns_job_id_string(self, processor):
        job_id = await processor.process_csv_async(
            upload_id="upload-1",
            blob_container="raw-uploads",
        )
        assert isinstance(job_id, str)
        # Must be a valid UUID
        uuid.UUID(job_id)

    async def test_job_status_exists_after_process(self, processor):
        job_id = await processor.process_csv_async(
            upload_id="upload-1",
            blob_container="raw-uploads",
        )
        status = await processor.get_job_status(job_id)
        assert status is not None
        assert "status" in status


class TestGetJobStatus:
    async def test_returns_dict_with_status_field(self, processor):
        job_id = await processor.process_csv_async(
            upload_id="upload-1",
            blob_container="raw-uploads",
        )
        result = await processor.get_job_status(job_id)
        assert isinstance(result, dict)
        assert "status" in result

    async def test_returns_none_for_unknown_job(self, processor):
        result = await processor.get_job_status("nonexistent-id")
        assert result is None

    async def test_list_and_clear_jobs_preserves_active_work(self, processor):
        _jobs.update(
            {
                "active": {"status": "RUNNING"},
                "done": {"status": "completed"},
                "missing": {},
            }
        )
        _tasks["done"] = MagicMock()

        removed = await processor.clear_terminal_jobs()

        assert removed == 2
        assert await processor.list_jobs() == [{"status": "RUNNING"}]
        assert "done" not in _tasks

    async def test_stop_unknown_and_terminal_jobs(self, processor):
        assert await processor.stop_job("missing") is None
        _jobs["done"] = {"status": "completed"}
        assert await processor.stop_job("done") == {"status": "completed"}

    def test_mark_cancelled_ignores_unknown_job(self, processor):
        processor._mark_cancelled("missing")
        assert _jobs == {}

    async def test_raise_if_cancelled_raises_cancelled_error(self, processor):
        _jobs["job"] = {"status": "cancelled"}
        with pytest.raises(asyncio.CancelledError):
            processor._raise_if_cancelled("job")

    async def test_terminal_reports_include_progress_and_swallow_legacy_signature(
        self, processor, api_client
    ):
        _jobs["job"] = {
            "status": "failed",
            "chunks_processed": 2,
            "total_chunks": 3,
        }
        api_client.is_configured.return_value = True

        await processor._report_failed("job", "bad")
        await processor._report_cancelled("job")

        assert api_client.report_stage.await_count == 2
        assert (
            api_client.report_stage.await_args_list[0].kwargs["failure_reason"] == "bad"
        )
        api_client.report_stage = AsyncMock(side_effect=TypeError("old signature"))
        await processor._report_failed("job", "bad")
        await processor._report_cancelled("job")

    def test_set_unknown_stage_preserves_stage_index(self, processor, api_client):
        _jobs["job"] = {"stage_index": 4}

        processor._set_stage("job", "custom", "custom stage", 0.5)

        assert _jobs["job"]["stage_index"] == 4
        assert _jobs["job"]["progress"] == 0.5


class TestCSVBackgroundProcessing:
    async def test_completed_after_background_runs(
        self, processor, blob_writer, embedder, api_client
    ):
        """Wait for background task to finish and verify completed status."""
        job_id = await processor.process_csv_async(
            upload_id="upload-1",
            blob_container="raw-uploads",
        )
        # Let the background task finish
        await asyncio.sleep(0.3)
        # Give tasks a chance to complete
        tasks = [
            t
            for t in asyncio.all_tasks()
            if not t.done() and t is not asyncio.current_task()
        ]
        if tasks:
            await asyncio.wait(tasks, timeout=5)

        status = await processor.get_job_status(job_id)
        assert status["status"] == "completed"
        assert status["chunks_processed"] >= 1
        api_client.upload_artifact.assert_awaited_once()
        embedder.generate_embedding.assert_awaited()

    async def test_reports_all_csv_pipeline_stages_to_configured_api_client(
        self, processor, api_client
    ):
        api_client.is_configured.return_value = True

        job_id = await processor.process_csv_async(
            upload_id="upload-csv-stages",
            blob_container="raw-uploads",
        )

        await _wait_for_terminal_status(processor, job_id)
        await asyncio.sleep(0)

        reported_stages = [
            call.args[1] for call in api_client.report_stage.await_args_list
        ]
        assert reported_stages[0] == "copying"
        for stage in [
            "copying",
            "parsing",
            "chunking",
            "embedding",
            "uploading-chunks",
            "completed",
        ]:
            assert stage in reported_stages
        assert reported_stages[-1] == "completed"

    async def test_empty_csv_results_in_failed(self, blob_writer, embedder, api_client):
        """An empty CSV (headers only, no data rows) should result in 'failed'."""
        blob_writer.download_blob = AsyncMock(return_value=b"make,model,year\n")
        proc = CSVProcessor(
            blob_writer=blob_writer, embedder=embedder, api_client=api_client
        )
        job_id = await proc.process_csv_async(
            upload_id="upload-empty",
            blob_container="raw-uploads",
        )
        await asyncio.sleep(0.3)
        tasks = [
            t
            for t in asyncio.all_tasks()
            if not t.done() and t is not asyncio.current_task()
        ]
        if tasks:
            await asyncio.wait(tasks, timeout=5)

        status = await proc.get_job_status(job_id)
        assert status["status"] == "failed"
        assert status["message"] == "Empty CSV file"

    async def test_stop_job_marks_only_selected_job_cancelled(self, processor):
        first_job_id = await processor.process_csv_async(
            upload_id="upload-stop-1",
            blob_container="raw-uploads",
        )
        second_job_id = await processor.process_csv_async(
            upload_id="upload-stop-2",
            blob_container="raw-uploads",
        )

        stopped = await processor.stop_job(first_job_id)
        await asyncio.sleep(0)

        assert stopped["status"] == "cancelled"
        assert (await processor.get_job_status(first_job_id))["status"] == "cancelled"
        assert (await processor.get_job_status(second_job_id))["status"] != "cancelled"
        await processor.stop_job(second_job_id)

    async def test_cancelled_job_does_not_upload_chunks(
        self, processor, embedder, api_client
    ):
        async def slow_embedding(_text: str):
            await asyncio.sleep(10)
            return [0.1] * 1536

        embedder.generate_embedding = AsyncMock(side_effect=slow_embedding)

        job_id = await processor.process_csv_async(
            upload_id="upload-cancel-no-upload",
            blob_container="raw-uploads",
        )
        await asyncio.sleep(0.05)

        await processor.stop_job(job_id)
        await asyncio.sleep(0)

        status = await processor.get_job_status(job_id)
        assert status["status"] == "cancelled"
        api_client.upload_artifact.assert_not_awaited()

    async def test_local_csv_without_group_columns_uses_metadata_defaults(
        self, processor, tmp_path, api_client
    ):
        from types import SimpleNamespace

        path = tmp_path / "specs.csv"
        path.write_text("power,torque\n47,43\n", encoding="utf-8")

        job_id = await processor.process_csv_async(
            upload_id="local",
            local_file_path=str(path),
            metadata=SimpleNamespace(make="Honda", model="CB500", year=2020),
        )
        status = await _wait_for_terminal_status(processor, job_id)

        assert status["status"] == "completed"
        uploaded = api_client.upload_artifact.await_args.args[0].decode("utf-8")
        chunk = __import__("json").loads(uploaded)
        assert (chunk["make"], chunk["model"], chunk["year"]) == (
            "Honda",
            "CB500",
            2020,
        )

    async def test_embedding_failure_marks_job_failed_and_reports_reason(
        self, processor, embedder, api_client
    ):
        api_client.is_configured.return_value = True
        embedder.generate_embedding.side_effect = RuntimeError("model offline")

        job_id = await processor.process_csv_async(
            upload_id="failure", blob_container="raw"
        )
        status = await _wait_for_terminal_status(processor, job_id)

        assert status["status"] == "failed"
        assert status["stage"] == "failed"
        assert "RuntimeError: model offline" in status["message"]
        assert any(
            call.args[1] == "failed" for call in api_client.report_stage.await_args_list
        )


async def test_process_csv_when_upload_id_contains_controls_logs_reversible_value(
    processor, caplog
):
    unsafe_upload_id = "external\\path\r\n\t\0\x01\x1f\x7f\x85\x9fvalue"
    escaped_upload_id = (
        "external\\\\path\\r\\n\\t\\0\\u0001\\u001F\\u007F\\u0085\\u009Fvalue"
    )
    caplog.set_level(logging.INFO, logger="processors.csv_processor")

    job_id = await processor.process_csv_async(
        upload_id=unsafe_upload_id,
        blob_container="raw-uploads",
    )
    await _wait_for_terminal_status(processor, job_id)

    target_records = [
        item for item in caplog.records if item.name == "processors.csv_processor"
    ]
    target_messages = [item.getMessage() for item in target_records]

    assert target_messages
    assert any(escaped_upload_id in message for message in target_messages)
    assert all(
        not any(character in message for character in "\r\n\t\0\x01\x1f\x7f\x85\x9f")
        for message in target_messages
    )
