"""Unit tests for PDFProcessor — Docling, Ollama, and Azure Blob all mocked."""

import asyncio
import json
import uuid
from types import SimpleNamespace
from typing import Any
from unittest.mock import AsyncMock, MagicMock, PropertyMock, patch

import pytest

from processors.pdf_processor import (
    PDFProcessor,
    _jobs,
    _resolve_source_file_name,
    _tasks,
)


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


async def _wait_for_paused_for_metadata(processor, job_id: str, timeout: float = 5.0):
    deadline = asyncio.get_running_loop().time() + timeout
    while asyncio.get_running_loop().time() < deadline:
        status = await processor.get_job_status(job_id)
        if status and status.get("paused_for_metadata"):
            return status
        await asyncio.sleep(0.01)
    raise TimeoutError(f"Job {job_id} did not pause for metadata")


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
def metadata_extractor():
    """Mocked MetadataExtractor that reports a complete (100% fill) result.

    The default mock lets existing pipeline tests sail through the
    extracting-metadata stage. Tests that exercise the pause/resume paths
    override ``extract.return_value`` or ``extract.side_effect``.
    """
    me = MagicMock()
    me.PAGE_SAMPLE_SIZES = [3, 6, 9, 10]
    me.extract = AsyncMock(return_value={
        "make": "Honda",
        "model": "CB500",
        "year": 2020,
        "category": "naked",
        "tags": ["naked", "500cc"],
        "fill_rate": 1.0,
        "pages_sampled": 3,
    })
    return me


@pytest.fixture()
def metadata():
    return SimpleNamespace(make="Honda", model="CB500", year=2020)


@pytest.fixture()
def processor(blob_writer, embedder, graph_extractor, metadata_extractor, api_client):
    return PDFProcessor(
        blob_writer=blob_writer,
        embedder=embedder,
        graph_extractor=graph_extractor,
        metadata_extractor=metadata_extractor,
        api_client=api_client,
    )


# ---------------------------------------------------------------------------
# Tests
# ---------------------------------------------------------------------------


class TestPDFProcessorInstantiation:
    def test_instantiates_with_mocked_deps(
        self, blob_writer, embedder, graph_extractor, metadata_extractor, api_client
    ):
        proc = PDFProcessor(
            blob_writer=blob_writer,
            embedder=embedder,
            graph_extractor=graph_extractor,
            metadata_extractor=metadata_extractor,
            api_client=api_client,
        )
        assert proc._blob_writer is blob_writer
        assert proc._embedder is embedder
        assert proc._graph_extractor is graph_extractor
        assert proc._metadata_extractor is metadata_extractor


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
            "extracting-metadata",
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
            "extracting-metadata",
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
        # Drain pending fire-and-forget report_stage tasks so all reports are
        # recorded before asserting. _set_stage reports via asyncio.create_task
        # (best-effort), so the terminal "failed" report (directly awaited) is
        # not guaranteed to be the last entry in await_args_list.
        pending = [
            t for t in asyncio.all_tasks()
            if t is not asyncio.current_task() and not t.done()
        ]
        if pending:
            await asyncio.wait(pending, timeout=5)

        # The terminal "failed" report must have been sent with the right
        # failure reason. Assert by filtering rather than by position because
        # fire-and-forget reports can interleave.
        failed_calls = [
            c for c in api_client.report_stage.await_args_list
            if c.args[1] == "failed"
        ]
        assert failed_calls
        assert failed_calls[-1].kwargs["failure_reason"] == "PDF processing failed: Expected 1536 dims, got 2560"

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


# ---------------------------------------------------------------------------
# Metadata extraction integration (Phase 2)
# ---------------------------------------------------------------------------


class TestMetadataExtraction:
    @patch("processors.pdf_processor.get_pdf_chunker_tokenizer")
    @patch("processors.pdf_processor.HybridChunker")
    @patch("processors.pdf_processor.DocumentConverter")
    async def test_successful_metadata_extraction_completes_pipeline(
        self, MockConverter, MockChunker, MockGetTokenizer,
        processor, metadata_extractor, metadata,
    ):
        """100% fill rate merges metadata and proceeds to chunking/completion."""
        MockGetTokenizer.return_value = MagicMock()
        fake_chunks = [_make_chunk("Chunk 1"), _make_chunk("Chunk 2", page_no=2)]
        mock_result = MagicMock()
        mock_result.document = MagicMock()
        MockConverter.return_value.convert.return_value = mock_result
        MockChunker.return_value.chunk.return_value = fake_chunks

        job_id = await processor.process_pdf_async(
            upload_id="upload-meta-ok",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
        )

        status = await _wait_for_terminal_status(processor, job_id)

        # Extractor was called with the page texts produced from the document.
        metadata_extractor.extract.assert_awaited_once()
        assert status["status"] == "completed"
        assert status["stage"] == "completed"
        assert status["chunks_processed"] == 2

        # The merged metadata stage appears in the reported stage history.
        history_stages = [e["stage"] for e in status["stage_history"]]
        assert "extracting-metadata" in history_stages

    @patch("processors.pdf_processor.get_pdf_chunker_tokenizer")
    @patch("processors.pdf_processor.HybridChunker")
    @patch("processors.pdf_processor.DocumentConverter")
    async def test_source_path_forwarded_to_metadata_extractor(
        self, MockConverter, MockChunker, MockGetTokenizer,
        processor, metadata_extractor, metadata,
    ):
        """source_path is threaded through to MetadataExtractor.extract()."""
        MockGetTokenizer.return_value = MagicMock()
        fake_chunks = [_make_chunk("Chunk 1"), _make_chunk("Chunk 2", page_no=2)]
        mock_result = MagicMock()
        mock_result.document = MagicMock()
        MockConverter.return_value.convert.return_value = mock_result
        MockChunker.return_value.chunk.return_value = fake_chunks

        source_path = "/data/manuals/2023/Honda/CBR600RR/service-manual.pdf"
        job_id = await processor.process_pdf_async(
            upload_id="upload-source-path",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
            source_path=source_path,
        )

        await _wait_for_terminal_status(processor, job_id)

        metadata_extractor.extract.assert_awaited_once()
        assert metadata_extractor.extract.await_args.kwargs["source_path"] == source_path

    @patch("processors.pdf_processor.get_pdf_chunker_tokenizer")
    @patch("processors.pdf_processor.HybridChunker")
    @patch("processors.pdf_processor.DocumentConverter")
    async def test_source_path_defaults_to_none_when_omitted(
        self, MockConverter, MockChunker, MockGetTokenizer,
        processor, metadata_extractor, metadata,
    ):
        """Omitting source_path forwards None to the extractor (no KeyError)."""
        MockGetTokenizer.return_value = MagicMock()
        fake_chunks = [_make_chunk("Chunk 1")]
        mock_result = MagicMock()
        mock_result.document = MagicMock()
        MockConverter.return_value.convert.return_value = mock_result
        MockChunker.return_value.chunk.return_value = fake_chunks

        job_id = await processor.process_pdf_async(
            upload_id="upload-no-source-path",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
        )

        await _wait_for_terminal_status(processor, job_id)

        metadata_extractor.extract.assert_awaited_once()
        assert metadata_extractor.extract.await_args.kwargs["source_path"] is None

    @patch("processors.pdf_processor.get_pdf_chunker_tokenizer")
    @patch("processors.pdf_processor.HybridChunker")
    @patch("processors.pdf_processor.DocumentConverter")
    async def test_pauses_at_needs_manual_metadata_when_fill_rate_low(
        self, MockConverter, MockChunker, MockGetTokenizer,
        processor, metadata_extractor, api_client, metadata,
    ):
        """Fill rate < 1.0 pauses the job without error and reports to the API."""
        api_client.is_configured.return_value = True
        metadata_extractor.extract.return_value = {
            "make": "Honda", "model": None, "year": 0, "category": None,
            "tags": [], "fill_rate": 0.25, "pages_sampled": 10,
        }
        MockGetTokenizer.return_value = MagicMock()
        mock_result = MagicMock()
        mock_result.document = MagicMock()
        MockConverter.return_value.convert.return_value = mock_result
        # Chunker must never run when paused before chunking.
        MockChunker.return_value.chunk.return_value = []

        job_id = await processor.process_pdf_async(
            upload_id="upload-meta-pause",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
        )

        status = await _wait_for_paused_for_metadata(processor, job_id)

        assert status["stage"] == "needs-manual-metadata"
        assert status["paused_for_metadata"] is True
        assert status["status"] == "awaiting-metadata"
        assert status["extracted_metadata"]["fill_rate"] == 0.25

        # The API was told about the pause with a human-readable failure reason.
        pause_calls = [
            c for c in api_client.report_stage.await_args_list
            if c.args[1] == "needs-manual-metadata"
        ]
        assert pause_calls
        assert "Manual entry required" in pause_calls[-1].kwargs["failure_reason"]

        # The pipeline stopped before chunking.
        MockChunker.return_value.chunk.assert_not_called()

    @patch("processors.pdf_processor.get_pdf_chunker_tokenizer")
    @patch("processors.pdf_processor.HybridChunker")
    @patch("processors.pdf_processor.DocumentConverter")
    async def test_resumes_with_manual_metadata_after_pause(
        self, MockConverter, MockChunker, MockGetTokenizer,
        processor, metadata_extractor, metadata,
    ):
        """A second call with the same job_id resumes from chunking."""
        MockGetTokenizer.return_value = MagicMock()
        fake_chunks = [_make_chunk("Chunk 1"), _make_chunk("Chunk 2", page_no=2)]
        mock_result = MagicMock()
        mock_result.document = MagicMock()
        MockConverter.return_value.convert.return_value = mock_result
        MockChunker.return_value.chunk.return_value = fake_chunks

        # First pass: incomplete extraction -> pause.
        metadata_extractor.extract.return_value = {
            "make": None, "model": None, "year": 0, "category": None,
            "tags": [], "fill_rate": 0.0, "pages_sampled": 10,
        }
        manual_metadata = SimpleNamespace(make="Kawasaki", model="Ninja 400", year=2022)

        job_id = await processor.process_pdf_async(
            upload_id="upload-meta-resume",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
            job_id="resume-job-1",
        )

        await _wait_for_paused_for_metadata(processor, job_id)
        extract_calls_after_first = metadata_extractor.extract.await_count
        assert extract_calls_after_first == 1

        # Second pass: resume with manual metadata (same job_id).
        job_id_2 = await processor.process_pdf_async(
            upload_id="upload-meta-resume",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=manual_metadata,
            source_access_token="test-token",
            job_id="resume-job-1",
        )
        assert job_id_2 == job_id

        status = await _wait_for_terminal_status(processor, job_id)
        assert status["status"] == "completed"
        assert status["chunks_processed"] == 2

        # Resume skipped LLM extraction entirely.
        assert metadata_extractor.extract.await_count == extract_calls_after_first

        # The paused state was cleared.
        final = await processor.get_job_status(job_id)
        assert not final.get("paused_for_metadata")

        # L1: extracted_metadata is cleared on resume, not left stale.
        assert "extracted_metadata" not in final


# ---------------------------------------------------------------------------
# _extract_page_texts edge cases
# ---------------------------------------------------------------------------


class TestExtractPageTexts:
    """Direct tests for PDFProcessor._extract_page_texts.

    These do not exercise the background pipeline; they construct fake
    Docling-like documents and assert the defensive handling for empty
    documents and per-page export failures.
    """

    def _make_doc(self, pages: dict[int, Any], export_side_effect=None):
        """Build a fake Docling document.

        Args:
            pages: A dict mapping 1-based page numbers to placeholder values.
            export_side_effect: If not None, ``export_to_text`` will raise this
                instead of returning text.
        """
        doc = MagicMock()
        doc.pages = pages

        def _export(page_no):
            if export_side_effect is not None:
                raise export_side_effect
            return f"page {page_no} text"

        doc.export_to_text = MagicMock(side_effect=_export)
        return doc

    def test_returns_empty_list_when_document_has_no_pages(self, processor):
        """A document with an empty ``pages`` dict yields no page texts."""
        doc = self._make_doc(pages={})
        result = processor._extract_page_texts(doc)
        assert result == []

    def test_returns_empty_list_when_pages_access_raises(self, processor):
        """If ``document.pages`` access raises, the method returns [].

        This guards against malformed documents that don't expose a usable
        ``pages`` mapping.
        """
        doc = MagicMock()
        # Simulate a document whose ``.pages`` property raises.
        type(doc).pages = PropertyMock(side_effect=RuntimeError("no pages"))
        doc.export_to_text = MagicMock()
        result = processor._extract_page_texts(doc)
        assert result == []
        doc.export_to_text.assert_not_called()

    def test_skips_pages_whose_export_raises(self, processor):
        """A per-page ``export_to_text`` failure is skipped, not fatal."""
        # Pages dict keyed by 1-based page number (Docling convention).
        doc = MagicMock()
        doc.pages = {1: object(), 2: object(), 3: object()}

        call_count = {"n": 0}

        def _export(page_no):
            call_count["n"] += 1
            if page_no == 2:
                raise ValueError("export failed for page 2")
            return f"page {page_no} text"

        doc.export_to_text = MagicMock(side_effect=_export)

        result = processor._extract_page_texts(doc)
        # Page 2 is skipped but pages 1 and 3 are returned, in page order.
        assert result == ["page 1 text", "page 3 text"]
        assert call_count["n"] == 3

    def test_capped_to_page_sample_upper_bound(self, processor):
        """More pages than the max sample size are truncated to the cap."""
        # metadata_extractor.PAGE_SAMPLE_SIZES[-1] == 10
        cap = processor._metadata_extractor.PAGE_SAMPLE_SIZES[-1]
        pages = {i: object() for i in range(1, cap + 5)}
        doc = MagicMock()
        doc.pages = pages
        doc.export_to_text = MagicMock(
            side_effect=lambda page_no: f"page {page_no} text"
        )

        result = processor._extract_page_texts(doc)
        assert len(result) == cap
        assert result[0] == "page 1 text"
        assert result[-1] == f"page {cap} text"
        # Only the first ``cap`` pages are exported.
        assert doc.export_to_text.call_count == cap

    def test_skips_empty_page_text(self, processor):
        """Pages that export to empty/whitespace text are omitted."""
        doc = MagicMock()
        doc.pages = {1: object(), 2: object()}

        def _export(page_no):
            return "" if page_no == 1 else "page 2 text"

        doc.export_to_text = MagicMock(side_effect=_export)

        result = processor._extract_page_texts(doc)
        assert result == ["page 2 text"]


def _uploaded_search_chunks(api_client) -> list[dict[str, Any]]:
    """Extract the chunk records uploaded as search-chunks NDJSON.

    Returns the list of parsed JSON objects from the ``search-chunks``
    ``upload_artifact`` call, raising if that call never happened.
    """
    calls = [
        c for c in api_client.upload_artifact.await_args_list
        if len(c.args) >= 3 and c.args[2] == "search-chunks"
    ]
    assert calls, "Expected a search-chunks upload_artifact call"
    payload = calls[-1].args[0]
    if isinstance(payload, (bytes, bytearray)):
        payload = payload.decode("utf-8")
    return [json.loads(line) for line in payload.splitlines() if line.strip()]


class TestResolveSourceFileName:
    """Unit tests for the _resolve_source_file_name helper.

    Covers the basename-only guarantee and the three-tier fallback:
    source_file_name -> basename(source_path) -> upload_id.
    """

    def test_uses_explicit_source_file_name(self):
        assert _resolve_source_file_name(
            "2023 Honda CBR600RR Service Manual.pdf", None, "upload-1"
        ) == "2023 Honda CBR600RR Service Manual.pdf"

    def test_strips_directory_from_source_file_name(self):
        """A full path in source_file_name is reduced to its basename."""
        assert _resolve_source_file_name(
            "/data/manuals/service-manual.pdf", None, "upload-1"
        ) == "service-manual.pdf"

    def test_blank_source_file_name_falls_back_to_source_path_basename(self):
        assert _resolve_source_file_name(
            "   ", "/data/manuals/2023/Honda/CBR600RR/manual.pdf", "upload-1"
        ) == "manual.pdf"

    def test_falls_back_to_source_path_basename(self):
        assert _resolve_source_file_name(
            None, "/data/manuals/2023/Honda/CBR600RR/manual.pdf", "upload-1"
        ) == "manual.pdf"

    def test_uses_only_basename_of_source_path(self):
        """The directory portion of source_path is never included."""
        resolved = _resolve_source_file_name(None, "/a/b/c/doc.pdf", "upload-1")
        assert resolved == "doc.pdf"
        assert "/" not in resolved

    def test_falls_back_to_upload_id_when_neither_given(self):
        assert _resolve_source_file_name(None, None, "upload-1") == "upload-1"

    def test_falls_back_to_upload_id_when_both_blank(self):
        assert _resolve_source_file_name("   ", "   ", "upload-1") == "upload-1"

    def test_source_file_name_takes_precedence_over_source_path(self):
        assert _resolve_source_file_name(
            "explicit.pdf", "/other/path.pdf", "upload-1"
        ) == "explicit.pdf"


class TestChunkSourceFileField:
    """Integration tests verifying the chunk ``sourceFile`` wire field."""

    @patch("processors.pdf_processor.get_pdf_chunker_tokenizer")
    @patch("processors.pdf_processor.HybridChunker")
    @patch("processors.pdf_processor.DocumentConverter")
    async def test_source_file_name_written_to_chunks(
        self, MockConverter, MockChunker, MockGetTokenizer,
        processor, metadata_extractor, api_client, metadata,
    ):
        """The manifest basename appears in every chunk's sourceFile."""
        MockGetTokenizer.return_value = MagicMock()
        fake_chunks = [_make_chunk("Chunk 1"), _make_chunk("Chunk 2", page_no=2)]
        mock_result = MagicMock()
        mock_result.document = MagicMock()
        MockConverter.return_value.convert.return_value = mock_result
        MockChunker.return_value.chunk.return_value = fake_chunks

        job_id = await processor.process_pdf_async(
            upload_id="upload-src-name",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
            source_file_name="2023 Honda CBR600RR Service Manual.pdf",
        )

        await _wait_for_terminal_status(processor, job_id)
        records = _uploaded_search_chunks(api_client)
        assert len(records) == 2
        for record in records:
            assert record["sourceFile"] == "2023 Honda CBR600RR Service Manual.pdf"

    @patch("processors.pdf_processor.get_pdf_chunker_tokenizer")
    @patch("processors.pdf_processor.HybridChunker")
    @patch("processors.pdf_processor.DocumentConverter")
    async def test_source_path_basename_used_when_no_explicit_name(
        self, MockConverter, MockChunker, MockGetTokenizer,
        processor, metadata_extractor, api_client, metadata,
    ):
        """Falls back to basename(source_path) when source_file_name is absent."""
        MockGetTokenizer.return_value = MagicMock()
        fake_chunks = [_make_chunk("Chunk 1")]
        mock_result = MagicMock()
        mock_result.document = MagicMock()
        MockConverter.return_value.convert.return_value = mock_result
        MockChunker.return_value.chunk.return_value = fake_chunks

        job_id = await processor.process_pdf_async(
            upload_id="upload-src-path",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
            source_path="/data/manuals/2023/Honda/CBR600RR/manual.pdf",
        )

        await _wait_for_terminal_status(processor, job_id)
        records = _uploaded_search_chunks(api_client)
        assert records[0]["sourceFile"] == "manual.pdf"

    @patch("processors.pdf_processor.get_pdf_chunker_tokenizer")
    @patch("processors.pdf_processor.HybridChunker")
    @patch("processors.pdf_processor.DocumentConverter")
    async def test_upload_id_used_when_neither_given(
        self, MockConverter, MockChunker, MockGetTokenizer,
        processor, metadata_extractor, api_client, metadata,
    ):
        """API-direct ingest (no filename) falls back to upload_id."""
        MockGetTokenizer.return_value = MagicMock()
        fake_chunks = [_make_chunk("Chunk 1")]
        mock_result = MagicMock()
        mock_result.document = MagicMock()
        MockConverter.return_value.convert.return_value = mock_result
        MockChunker.return_value.chunk.return_value = fake_chunks

        job_id = await processor.process_pdf_async(
            upload_id="upload-fallback",
            document_type="manual",
            blob_container="raw-uploads",
            metadata=metadata,
            source_access_token="test-token",
        )

        await _wait_for_terminal_status(processor, job_id)
        records = _uploaded_search_chunks(api_client)
        assert records[0]["sourceFile"] == "upload-fallback"
