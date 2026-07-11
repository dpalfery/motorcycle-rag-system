"""Unit tests for shared processor job lifecycle behavior."""

from unittest.mock import MagicMock, patch

from infrastructure.base_processor import BaseProcessor
from infrastructure.job_failure import format_failure


class _Processor(BaseProcessor):
    async def process_async(self, upload_id: str, **kwargs) -> str:
        return upload_id


def _processor(configured=True):
    api_client = MagicMock()
    api_client.is_configured.return_value = configured
    return _Processor(api_client), api_client


async def test_process_and_empty_registry_accessors():
    processor, _ = _processor()

    assert await processor.process_async("upload-1") == "upload-1"
    assert await processor.get_job_status("missing") is None
    assert await processor.list_jobs() == []


async def test_create_update_complete_and_clear_job():
    processor, _ = _processor()

    with patch("infrastructure.base_processor.uuid.uuid4", return_value="job-1"):
        job_id, created = processor._create_job(
            "upload-1", "graph-seeding", {"source": "local"}
        )

    assert job_id == "job-1"
    assert created["status"] == "processing"
    assert created["source"] == "local"
    processor._update_job_status(
        job_id, "running", message="working", progress=42.0, count=3
    )
    assert (await processor.get_job_status(job_id))["count"] == 3

    processor._mark_job_completed(job_id, "done", artifact_count=4)
    completed = await processor.get_job_status(job_id)
    assert completed["status"] == "completed"
    assert completed["progress"] == 100.0
    assert completed["artifact_count"] == 4
    assert await processor.clear_terminal_jobs() == 1
    assert await processor.list_jobs() == []


async def test_clear_terminal_jobs_preserves_all_active_status_spellings():
    processor, _ = _processor()
    processor._jobs = {
        "queued": {"status": " queued "},
        "processing": {"status": "processing"},
        "running": {"status": "RUNNING"},
        "in-progress": {"status": "inprogress"},
        "failed": {"status": "failed"},
        "missing": {},
    }

    removed = await processor.clear_terminal_jobs()

    assert removed == 2
    assert set(processor._jobs) == {
        "queued",
        "processing",
        "running",
        "in-progress",
    }


async def test_failed_job_and_unknown_update():
    processor, _ = _processor()
    job_id, _ = processor._create_job("upload", "test")

    processor._mark_job_failed(job_id, "bad input")
    processor._update_job_status("unknown", "completed")

    failed = await processor.get_job_status(job_id)
    assert failed["status"] == "failed"
    assert failed["error"] == "bad input"
    assert failed["message"] == "Processing failed: bad input"


def test_api_properties_delegate_to_client():
    processor, api_client = _processor(configured=False)

    assert processor.api_client is api_client
    assert processor.is_api_configured is False


def test_format_failure_captures_exception_type_message_and_traceback():
    try:
        raise ValueError("invalid row")
    except ValueError as exc:
        result = format_failure(exc)

    assert result["status"] == "failed"
    assert result["message"] == "ValueError: invalid row"
    assert "raise ValueError" in result["error"]
