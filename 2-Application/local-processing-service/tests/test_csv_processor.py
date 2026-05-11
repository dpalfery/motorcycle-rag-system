"""Unit tests for CSVProcessor — all external calls mocked."""

import asyncio
import uuid
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from processors.csv_processor import CSVProcessor, _jobs


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


@pytest.fixture(autouse=True)
def _clear_jobs():
    """Ensure the module-level _jobs dict is empty before/after each test."""
    _jobs.clear()
    yield
    _jobs.clear()


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
    return ac


@pytest.fixture()
def embedder():
    em = MagicMock()
    em.generate_embedding = AsyncMock(return_value=[0.1] * 1536)
    return em


@pytest.fixture()
def processor(blob_writer, embedder, api_client):
    return CSVProcessor(blob_writer=blob_writer, embedder=embedder, api_client=api_client)


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


class TestCSVProcessorInstantiation:
    def test_instantiates_with_mock_dependencies(self, blob_writer, embedder, api_client):
        proc = CSVProcessor(blob_writer=blob_writer, embedder=embedder, api_client=api_client)
        assert proc.blob_writer is blob_writer
        assert proc.embedder is embedder


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

    async def test_empty_csv_results_in_failed(self, blob_writer, embedder):
        """An empty CSV (headers only, no data rows) should result in 'failed'."""
        blob_writer.download_blob = AsyncMock(return_value=b"make,model,year\n")
        proc = CSVProcessor(blob_writer=blob_writer, embedder=embedder, api_client=api_client)
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
