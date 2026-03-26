"""Unit tests for BikeGraphProcessor — all external calls mocked."""

import asyncio
import io
import tempfile
import uuid
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock

import pandas as pd
import pytest

from processors.bike_graph_processor import BikeGraphProcessor, _jobs, _node_id


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------

MINIMAL_CSV = (
    "Model,Year,Category,Engine type,Displacement ccm,Power HP\n"
    "Aprilia RS 660,2021,Sport,\"Twin, four-stroke\",659,100\n"
    "Aprilia RSV4,2021,Sport,\"V4, four-stroke\",1099,217\n"
    "Honda CB500F,2022,Naked bike,\"Twin, four-stroke\",471,47\n"
)

DUPLICATE_ROW_CSV = (
    "Model,Year,Category,Engine type\n"
    "Aprilia RS 660,2021,Sport,\"Twin, four-stroke\"\n"
    "Aprilia RS 660,2021,Sport,\"Twin, four-stroke\"\n"  # exact duplicate
)

NO_YEAR_CSV = "Model,Category\nAprilia RS 660,Sport\n"
NO_MODEL_CSV = "Year,Category\n2021,Sport\n"
EMPTY_CSV = "Model,Year,Category\n"


@pytest.fixture(autouse=True)
def _clear_jobs():
    _jobs.clear()
    yield
    _jobs.clear()


@pytest.fixture()
def blob_writer():
    bw = MagicMock()
    bw.upload_json = AsyncMock(return_value=None)
    return bw


@pytest.fixture()
def processor(blob_writer):
    return BikeGraphProcessor(blob_writer=blob_writer)


def _write_csv(content: str) -> Path:
    """Write *content* to a temp file and return its path."""
    tmp = tempfile.NamedTemporaryFile(
        mode="w", suffix=".csv", delete=False, encoding="utf-8"
    )
    tmp.write(content)
    tmp.flush()
    tmp.close()
    return Path(tmp.name)


# ---------------------------------------------------------------------------
# Instantiation
# ---------------------------------------------------------------------------


class TestInstantiation:
    def test_requires_blob_writer(self, blob_writer):
        proc = BikeGraphProcessor(blob_writer=blob_writer)
        assert proc.blob_writer is blob_writer


# ---------------------------------------------------------------------------
# process_async
# ---------------------------------------------------------------------------


class TestProcessAsync:
    async def test_returns_valid_uuid_job_id(self, processor):
        csv_path = _write_csv(MINIMAL_CSV)
        job_id = await processor.process_async(
            upload_id=str(uuid.uuid4()), local_file_path=str(csv_path)
        )
        uuid.UUID(job_id)  # raises if not valid UUID

    async def test_job_registered_immediately(self, processor):
        csv_path = _write_csv(MINIMAL_CSV)
        job_id = await processor.process_async(
            upload_id=str(uuid.uuid4()), local_file_path=str(csv_path)
        )
        assert job_id in _jobs
        assert _jobs[job_id]["status"] == "processing"


# ---------------------------------------------------------------------------
# get_job_status
# ---------------------------------------------------------------------------


class TestGetJobStatus:
    async def test_returns_none_for_unknown_job(self, processor):
        assert await processor.get_job_status("no-such-id") is None

    async def test_returns_dict_for_known_job(self, processor):
        csv_path = _write_csv(MINIMAL_CSV)
        job_id = await processor.process_async(
            upload_id=str(uuid.uuid4()), local_file_path=str(csv_path)
        )
        result = await processor.get_job_status(job_id)
        assert isinstance(result, dict)
        assert "status" in result


# ---------------------------------------------------------------------------
# Background processing — completed / failed paths
# ---------------------------------------------------------------------------


async def _wait_for_job(processor, job_id: str, timeout: float = 5.0):
    tasks = [
        t
        for t in asyncio.all_tasks()
        if not t.done() and t is not asyncio.current_task()
    ]
    if tasks:
        await asyncio.wait(tasks, timeout=timeout)
    return await processor.get_job_status(job_id)


class TestBackgroundProcessing:
    async def test_completes_successfully(self, processor, blob_writer):
        csv_path = _write_csv(MINIMAL_CSV)
        upload_id = str(uuid.uuid4())
        job_id = await processor.process_async(
            upload_id=upload_id, local_file_path=str(csv_path)
        )
        status = await _wait_for_job(processor, job_id)

        assert status["status"] == "completed"
        assert status["nodes_created"] > 0
        assert status["edges_created"] > 0
        blob_writer.upload_json.assert_awaited_once()

    async def test_blob_path_uses_upload_id(self, processor, blob_writer):
        csv_path = _write_csv(MINIMAL_CSV)
        upload_id = str(uuid.uuid4())
        await processor.process_async(
            upload_id=upload_id, local_file_path=str(csv_path)
        )
        await _wait_for_job(processor, "dummy")  # allow background to finish

        call_args = blob_writer.upload_json.call_args
        assert call_args is not None
        _, blob_path, _ = call_args.args
        assert upload_id in blob_path

    async def test_empty_csv_results_in_failed(self, processor):
        csv_path = _write_csv(EMPTY_CSV)
        job_id = await processor.process_async(
            upload_id=str(uuid.uuid4()), local_file_path=str(csv_path)
        )
        status = await _wait_for_job(processor, job_id)
        assert status["status"] == "failed"

    async def test_missing_year_column_results_in_failed(self, processor):
        csv_path = _write_csv(NO_YEAR_CSV)
        job_id = await processor.process_async(
            upload_id=str(uuid.uuid4()), local_file_path=str(csv_path)
        )
        status = await _wait_for_job(processor, job_id)
        assert status["status"] == "failed"

    async def test_missing_model_column_results_in_failed(self, processor):
        csv_path = _write_csv(NO_MODEL_CSV)
        job_id = await processor.process_async(
            upload_id=str(uuid.uuid4()), local_file_path=str(csv_path)
        )
        status = await _wait_for_job(processor, job_id)
        assert status["status"] == "failed"


# ---------------------------------------------------------------------------
# Graph structure — _build_graph (tested directly for precision)
# ---------------------------------------------------------------------------


class TestBuildGraph:
    def test_creates_motorcycle_nodes(self, processor):
        csv_path = _write_csv(MINIMAL_CSV)
        nodes, _ = processor._build_graph("upload-1", str(csv_path))
        bike_nodes = [n for n in nodes if n["type"] == "Motorcycle"]
        assert len(bike_nodes) == 3  # RS 660, RSV4, CB500F

    def test_creates_category_nodes(self, processor):
        csv_path = _write_csv(MINIMAL_CSV)
        nodes, _ = processor._build_graph("upload-1", str(csv_path))
        cat_nodes = [n for n in nodes if n["type"] == "Category"]
        names = {n["name"] for n in cat_nodes}
        assert "Sport" in names
        assert "Naked bike" in names

    def test_creates_engine_type_nodes(self, processor):
        csv_path = _write_csv(MINIMAL_CSV)
        nodes, _ = processor._build_graph("upload-1", str(csv_path))
        eng_nodes = [n for n in nodes if n["type"] == "EngineType"]
        # "Twin, four-stroke" appears for RS 660 and CB500F — only one node
        # "V4, four-stroke" for RSV4
        assert len(eng_nodes) == 2

    def test_creates_belongs_to_edges(self, processor):
        csv_path = _write_csv(MINIMAL_CSV)
        _, edges = processor._build_graph("upload-1", str(csv_path))
        belongs_to = [e for e in edges if e["relationshipType"] == "BELONGS_TO"]
        assert len(belongs_to) == 3  # one per bike

    def test_creates_has_engine_type_edges(self, processor):
        csv_path = _write_csv(MINIMAL_CSV)
        _, edges = processor._build_graph("upload-1", str(csv_path))
        engine_edges = [e for e in edges if e["relationshipType"] == "HAS_ENGINE_TYPE"]
        assert len(engine_edges) == 3  # one per bike

    def test_duplicate_rows_produce_single_bike_node(self, processor):
        csv_path = _write_csv(DUPLICATE_ROW_CSV)
        nodes, edges = processor._build_graph("upload-1", str(csv_path))
        bike_nodes = [n for n in nodes if n["type"] == "Motorcycle"]
        assert len(bike_nodes) == 1

    def test_duplicate_rows_produce_single_edge(self, processor):
        csv_path = _write_csv(DUPLICATE_ROW_CSV)
        _, edges = processor._build_graph("upload-1", str(csv_path))
        belongs_to = [e for e in edges if e["relationshipType"] == "BELONGS_TO"]
        assert len(belongs_to) == 1

    def test_node_ids_are_deterministic(self, processor):
        csv_path = _write_csv(MINIMAL_CSV)
        nodes1, _ = processor._build_graph("upload-1", str(csv_path))
        nodes2, _ = processor._build_graph("upload-1", str(csv_path))
        ids1 = {n["id"] for n in nodes1}
        ids2 = {n["id"] for n in nodes2}
        assert ids1 == ids2

    def test_source_document_id_set_on_all_nodes(self, processor):
        csv_path = _write_csv(MINIMAL_CSV)
        upload_id = str(uuid.uuid4())
        nodes, _ = processor._build_graph(upload_id, str(csv_path))
        for node in nodes:
            assert node["sourceDocumentId"] == upload_id

    def test_motorcycle_name_includes_year(self, processor):
        csv_path = _write_csv(MINIMAL_CSV)
        nodes, _ = processor._build_graph("upload-1", str(csv_path))
        bike_names = {n["name"] for n in nodes if n["type"] == "Motorcycle"}
        assert "Aprilia RS 660 2021" in bike_names

    def test_raises_on_missing_model_column(self, processor):
        csv_path = _write_csv(NO_MODEL_CSV)
        with pytest.raises(ValueError, match="Model"):
            processor._build_graph("upload-1", str(csv_path))

    def test_raises_on_missing_year_column(self, processor):
        csv_path = _write_csv(NO_YEAR_CSV)
        with pytest.raises(ValueError, match="Year"):
            processor._build_graph("upload-1", str(csv_path))

    def test_raises_on_empty_csv(self, processor):
        csv_path = _write_csv(EMPTY_CSV)
        with pytest.raises(ValueError, match="empty"):
            processor._build_graph("upload-1", str(csv_path))
