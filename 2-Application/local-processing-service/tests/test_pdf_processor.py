"""Unit tests for PDFProcessor — Docling, Ollama, and Azure Blob all mocked."""

import asyncio
import uuid
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from processors.pdf_processor import PDFProcessor, _jobs, _tasks


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


@pytest.fixture(autouse=True)
def _clear_jobs():
    _jobs.clear()
    _tasks.clear()
    yield
    _jobs.clear()
    _tasks.clear()


def _make_chunk(text: str = "Sample chunk text", page_no: int = 1):
    """Build a fake Docling chunk with the minimal .meta shape."""
    prov_item = SimpleNamespace(page_no=page_no)
    doc_item = SimpleNamespace(prov=[prov_item])
    meta = SimpleNamespace(headings=["Section A"], doc_items=[doc_item])
    return SimpleNamespace(text=text, meta=meta)


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
    bw.download_blob = AsyncMock(return_value=b"%PDF-1.4 fake content")
    bw.upload_jsonl = AsyncMock(return_value=None)
    bw.upload_json = AsyncMock(return_value=None)
    return bw


@pytest.fixture()
def api_client():
    ac = MagicMock()
    ac.is_configured = MagicMock(return_value=False)
    ac.download_source = AsyncMock(return_value=b"%PDF-1.4 fake content")
    ac.upload_artifact = AsyncMock(return_value=None)
    ac.report_stage = AsyncMock(return_value=None)
    return ac


@pytest.fixture()
def embedder():
    em = MagicMock()
    em.generate_embedding = AsyncMock(return_value=[0.1] * 1536)
    return em


@pytest.fixture()
def graph_extractor():
    ge = MagicMock()
    ge.extract = AsyncMock(return_value=[{"nodes": [], "edges": []}])
    return ge


@pytest.fixture()
def metadata():
    return SimpleNamespace(make="Honda", model="CB500", year=2020)


@pytest.fixture()
def processor(blob_writer, embedder, graph_extractor, api_client):
    return PDFProcessor(
        blob_writer=blob_writer,
        embedder=embedder,
        graph_extractor=graph_extractor,
        api_client=api_client,
    )


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


class TestPDFProcessorInstantiation:
    def test_instantiates_with_mocked_deps(
        self, blob_writer, embedder, graph_extractor, api_client
    ):
        proc = PDFProcessor(
            blob_writer=blob_writer,
            embedder=embedder,
            graph_extractor=graph_extractor,
            api_client=api_client,
        )
        assert proc._blob_writer is blob_writer
        assert proc._embedder is embedder
        assert proc._graph_extractor is graph_extractor


class TestProcessPDFAsync:
    async def test_returns_job_id_string(self, processor, metadata):
        job_id = await processor.process_pdf_async(
            upload_id="upload-pdf-1",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
        )
        assert isinstance(job_id, str)
        uuid.UUID(job_id)

    async def test_job_status_exists_immediately(self, processor, metadata):
        job_id = await processor.process_pdf_async(
            upload_id="upload-pdf-1",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
        )
        status = await processor.get_job_status(job_id)
        assert status is not None
        assert "status" in status


class TestGetJobStatus:
    async def test_returns_dict_with_status_field(self, processor, metadata):
        job_id = await processor.process_pdf_async(
            upload_id="upload-pdf-1",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
        )
        result = await processor.get_job_status(job_id)
        assert isinstance(result, dict)
        assert "status" in result

    async def test_returns_none_for_unknown_job(self, processor):
        result = await processor.get_job_status("nonexistent-id")
        assert result is None


class TestPDFBackgroundProcessing:
    @patch("processors.pdf_processor.get_pdf_chunker_tokenizer")
    @patch("processors.pdf_processor.HybridChunker")
    @patch("processors.pdf_processor.DocumentConverter")
    async def test_completed_after_background_runs(
        self, MockConverter, MockChunker, MockGetTokenizer, processor, metadata
    ):
        """Mock Docling so no real PDF conversion happens."""
        MockGetTokenizer.return_value = MagicMock()
        fake_chunks = [_make_chunk("Chunk 1"), _make_chunk("Chunk 2", page_no=2)]

        # DocumentConverter().convert() returns object with .document
        mock_result = MagicMock()
        mock_result.document = MagicMock()
        MockConverter.return_value.convert.return_value = mock_result

        # HybridChunker().chunk() returns the fake chunks
        MockChunker.return_value.chunk.return_value = fake_chunks

        job_id = await processor.process_pdf_async(
            upload_id="upload-pdf-1",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
        )

        # Wait for background task
        await asyncio.sleep(0.5)
        tasks = [
            t
            for t in asyncio.all_tasks()
            if not t.done() and t is not asyncio.current_task()
        ]
        if tasks:
            await asyncio.wait(tasks, timeout=5)

        status = await processor.get_job_status(job_id)
        assert status["status"] == "completed"
        assert status["chunks_processed"] == 2

    @patch("processors.pdf_processor.get_pdf_chunker_tokenizer")
    @patch("processors.pdf_processor.HybridChunker")
    @patch("processors.pdf_processor.DocumentConverter")
    async def test_reports_all_pdf_pipeline_stages_to_configured_api_client(
        self, MockConverter, MockChunker, MockGetTokenizer, processor, api_client, metadata
    ):
        api_client.is_configured.return_value = True
        MockGetTokenizer.return_value = MagicMock()
        fake_chunks = [_make_chunk("Chunk 1"), _make_chunk("Chunk 2", page_no=2)]

        mock_result = MagicMock()
        mock_result.document = MagicMock()
        MockConverter.return_value.convert.return_value = mock_result
        MockChunker.return_value.chunk.return_value = fake_chunks

        job_id = await processor.process_pdf_async(
            upload_id="upload-pdf-stages",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
        )

        await _wait_for_terminal_status(processor, job_id)
        await asyncio.sleep(0)

        reported_stages = [call.args[1] for call in api_client.report_stage.await_args_list]
        assert reported_stages[0] == "copying"
        for stage in [
            "copying",
            "parsing",
            "chunking",
            "embedding",
            "uploading-chunks",
            "extracting-graph",
            "uploading-graph",
            "completed",
        ]:
            assert stage in reported_stages
        assert reported_stages[-1] == "completed"

        status = await processor.get_job_status(job_id)
        history = status["stage_history"]
        history_stages = [entry["stage"] for entry in history]
        assert history_stages == [
            "copying",
            "parsing",
            "chunking",
            "embedding",
            "uploading-chunks",
            "extracting-graph",
            "uploading-graph",
            "completed",
        ]
        assert all(entry["set_at"] for entry in history)

    @patch("processors.pdf_processor.get_pdf_chunker_tokenizer")
    @patch("processors.pdf_processor.HybridChunker")
    @patch("processors.pdf_processor.DocumentConverter")
    async def test_failed_when_second_embedding_times_out(
        self, MockConverter, MockChunker, MockGetTokenizer, processor, embedder, metadata
    ):
        MockGetTokenizer.return_value = MagicMock()
        fake_chunks = [_make_chunk("Chunk 1"), _make_chunk("Chunk 2", page_no=2)]

        mock_result = MagicMock()
        mock_result.document = MagicMock()
        MockConverter.return_value.convert.return_value = mock_result
        MockChunker.return_value.chunk.return_value = fake_chunks
        timeout_error = RuntimeError(
            "Foundry Local embedding failed after 3 retries"
        )
        timeout_error.__cause__ = asyncio.TimeoutError()
        embedder.generate_embedding = AsyncMock(
            side_effect=[[0.1] * 1536, timeout_error]
        )

        job_id = await processor.process_pdf_async(
            upload_id="upload-pdf-timeout",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
        )

        await asyncio.sleep(0.5)
        tasks = [
            t
            for t in asyncio.all_tasks()
            if not t.done() and t is not asyncio.current_task()
        ]
        if tasks:
            await asyncio.wait(tasks, timeout=5)

        status = await processor.get_job_status(job_id)
        assert status["status"] == "failed"
        assert status["chunks_processed"] == 1
        assert status["progress"] > 0.3
        assert status["message"] == (
            "Embedding request timed out. Check the local embedding model and retry."
        )
        assert "Traceback" not in status["error"]

    @patch("processors.pdf_processor.get_pdf_chunker_tokenizer")
    @patch("processors.pdf_processor.HybridChunker")
    @patch("processors.pdf_processor.DocumentConverter")
    async def test_reports_terminal_failed_stage_to_configured_api_client(
        self, MockConverter, MockChunker, MockGetTokenizer, processor, api_client, embedder, metadata
    ):
        api_client.is_configured.return_value = True
        MockGetTokenizer.return_value = MagicMock()
        mock_result = MagicMock()
        mock_result.document = MagicMock()
        MockConverter.return_value.convert.return_value = mock_result
        MockChunker.return_value.chunk.return_value = [_make_chunk("Chunk 1")]
        embedder.generate_embedding = AsyncMock(side_effect=ValueError("Expected 1536 dims, got 2560"))

        job_id = await processor.process_pdf_async(
            upload_id="upload-pdf-failed-stage",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
        )

        await _wait_for_terminal_status(processor, job_id)
        await asyncio.sleep(0)

        last_call = api_client.report_stage.await_args_list[-1]
        assert last_call.args[1] == "failed"
        assert last_call.kwargs["failure_reason"] == "PDF processing failed: Expected 1536 dims, got 2560"

        status = await processor.get_job_status(job_id)
        assert status["status"] == "failed"
        assert status["stage"] == "failed"

    @patch("processors.pdf_processor.get_pdf_chunker_tokenizer")
    @patch("processors.pdf_processor.HybridChunker")
    @patch("processors.pdf_processor.DocumentConverter")
    async def test_failed_when_no_chunks_extracted(
        self, MockConverter, MockChunker, MockGetTokenizer, processor, metadata
    ):
        MockGetTokenizer.return_value = MagicMock()
        mock_result = MagicMock()
        mock_result.document = MagicMock()
        MockConverter.return_value.convert.return_value = mock_result

        # Docling produces zero chunks
        MockChunker.return_value.chunk.return_value = []

        job_id = await processor.process_pdf_async(
            upload_id="upload-pdf-2",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
        )

        await asyncio.sleep(0.5)
        tasks = [
            t
            for t in asyncio.all_tasks()
            if not t.done() and t is not asyncio.current_task()
        ]
        if tasks:
            await asyncio.wait(tasks, timeout=5)

        status = await processor.get_job_status(job_id)
        assert status["status"] == "failed"
