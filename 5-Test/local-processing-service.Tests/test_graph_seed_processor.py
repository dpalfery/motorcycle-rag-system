"""Unit tests for deterministic CSV graph seeding."""

import json
from unittest.mock import AsyncMock, MagicMock, patch

import pandas as pd
import pytest

from infrastructure.stage_runner import (
    RunMetadata,
    RunStatus,
    StageMetadata,
    StageRunner,
    StageStatus,
)
from processors.graph_seed_processor import (
    GRAPH_SEEDING_STAGES,
    GraphSeedProcessor,
    _build_description,
    _node_id,
    _split_make,
)


@pytest.fixture
def processor(tmp_path):
    blob_writer = MagicMock()
    blob_writer.download_blob = AsyncMock()
    api_client = MagicMock()
    api_client.is_configured.return_value = False
    return GraphSeedProcessor(blob_writer, api_client, tmp_path)


def _runner(processor, tmp_path, job_id="job-1"):
    runner = StageRunner(job_id, tmp_path / job_id, GRAPH_SEEDING_STAGES)
    processor._stage_runners[job_id] = runner
    processor._jobs[job_id] = {"job_id": job_id, "status": "processing"}
    return runner


def test_helpers_are_deterministic_and_handle_sparse_values():
    first = _node_id("Honda|CB500")

    assert first == _node_id("Honda|CB500")
    assert first != _node_id("Honda|CB650")
    assert _split_make(" Aprilia RS 660 ") == ("Aprilia", "RS 660")
    assert _split_make("Honda") == ("Honda", "Honda")
    row = pd.Series(
        {"Category": "Sport", "Power hp": 0, "Torque Nm": None, "Other": "ignored"}
    )
    assert _build_description(
        row, {"category": "Category", "power hp": "Power hp", "torque nm": "Torque Nm"}
    ) == "Category: Sport"


async def test_process_async_requires_a_source(processor):
    with pytest.raises(ValueError, match="Either blob_container or local_file_path"):
        await processor.process_async("upload")


async def test_process_async_initializes_checkpoint_and_background_task(
    processor, tmp_path
):
    created = []

    def capture(coroutine):
        created.append(coroutine)
        return MagicMock()

    with patch("processors.graph_seed_processor.asyncio.create_task", side_effect=capture):
        job_id = await processor.process_async(
            "upload-1", local_file_path="/tmp/source.csv"
        )

    created[0].close()
    checkpoint = processor._stage_runners[job_id].load_checkpoint()
    assert checkpoint.document_id == "upload-1"
    assert checkpoint.status is RunStatus.RUNNING
    assert checkpoint.started_from_stage == StageRunner.STAGE_SOURCE
    assert (tmp_path / job_id).is_dir()


async def test_process_graph_seeding_without_runner_marks_failure(processor):
    processor._jobs["missing"] = {"status": "processing"}

    await processor._process_graph_seeding("missing", "upload", None, None)

    assert processor._jobs["missing"]["status"] == "failed"


async def test_process_graph_seeding_completes_pipeline_and_checkpoint(
    processor, tmp_path
):
    runner = _runner(processor, tmp_path)
    metadata = RunMetadata(
        run_id="job-1",
        document_id="upload",
        run_type="graph-seeding",
        status=RunStatus.RUNNING,
        started_at="start",
    )
    runner.save_checkpoint(metadata)
    runner.run_pipeline = AsyncMock(return_value={})
    runner.get_next_stage = MagicMock(return_value=StageRunner.STAGE_SOURCE)

    await processor._process_graph_seeding("job-1", "upload", None, "source.csv")

    saved = runner.load_checkpoint()
    assert saved.status is RunStatus.COMPLETED
    assert saved.completed_stage == StageRunner.STAGE_COMPLETE
    assert saved.completed_at is not None
    assert processor._jobs["job-1"]["status"] == "completed"
    assert set(runner.run_pipeline.await_args.kwargs["stages"]) == set(
        GRAPH_SEEDING_STAGES
    )


async def test_process_graph_seeding_handles_already_complete_and_failure(
    processor, tmp_path
):
    runner = _runner(processor, tmp_path, "complete")
    runner.get_next_stage = MagicMock(return_value=None)

    await processor._process_graph_seeding("complete", "upload", None, None)

    assert processor._jobs["complete"]["message"] == "All stages already completed"

    failed_runner = _runner(processor, tmp_path, "failed")
    metadata = RunMetadata(
        run_id="failed",
        document_id="upload",
        run_type="graph-seeding",
        status=RunStatus.RUNNING,
        started_at="start",
    )
    failed_runner.save_checkpoint(metadata)
    failed_runner.run_pipeline = AsyncMock(side_effect=RuntimeError("bad csv"))

    await processor._process_graph_seeding("failed", "upload", None, "source.csv")

    saved = failed_runner.load_checkpoint()
    assert saved.status is RunStatus.FAILED
    assert saved.error_summary == "bad csv"
    assert processor._jobs["failed"]["status"] == "failed"


async def test_download_source_copies_local_file_and_records_hash(
    processor, tmp_path
):
    source = tmp_path / "input.csv"
    source.write_text("model,category\nHonda CB500,Naked\n", encoding="utf-8")
    runner = _runner(processor, tmp_path)
    runner._stages[StageRunner.STAGE_SOURCE] = StageMetadata(
        StageRunner.STAGE_SOURCE, StageStatus.IN_PROGRESS
    )

    result = await processor._stage_download_source("job-1", None, str(source))

    assert result.read_text(encoding="utf-8") == source.read_text(encoding="utf-8")
    stage = runner._stages[StageRunner.STAGE_SOURCE]
    assert len(stage.artifact_hash) == 64
    assert stage.metadata_json["local_file_path"] == str(source)
    assert processor._jobs["job-1"]["progress"] == 10.0


async def test_download_source_writes_blob_bytes(processor, tmp_path):
    runner = _runner(processor, tmp_path)
    processor._blob_writer.download_blob.return_value = b"model\nHonda CB500\n"

    result = await processor._stage_download_source("job-1", "raw", None)

    assert result.read_bytes() == b"model\nHonda CB500\n"
    processor._blob_writer.download_blob.assert_awaited_once_with(
        "raw", "job-1.csv"
    )


async def test_download_source_rejects_missing_container_or_empty_blob(
    processor, tmp_path
):
    _runner(processor, tmp_path)

    with pytest.raises(RuntimeError, match="blob_container is required"):
        await processor._stage_download_source("job-1", None, None)

    processor._blob_writer.download_blob.return_value = b""
    with pytest.raises(RuntimeError, match="Failed to download CSV"):
        await processor._stage_download_source("job-1", "raw", None)


async def test_build_graph_deduplicates_lookup_nodes_and_skips_blank_models(
    processor, tmp_path
):
    runner = _runner(processor, tmp_path)
    source = runner.get_stage_dir(StageRunner.STAGE_SOURCE) / "source.csv"
    source.write_text(
        "model,category,engine type,power hp\n"
        "Honda CB500,Naked,Twin,47\n"
        "Honda CB650,Naked,Four,95\n"
        ",Sport,Twin,100\n",
        encoding="utf-8",
    )

    result = await processor._stage_build_graph("job-1", "upload-1")

    node_types = [node["type"] for node in result["nodes"]]
    assert node_types.count("Motorcycle") == 2
    assert node_types.count("Category") == 1
    assert node_types.count("EngineType") == 2
    assert len(result["edges"]) == 4
    assert result["nodes"][0]["description"] == "category: Naked; engine type: Twin; power hp: 47"
    persisted = json.loads(
        (
            runner.get_stage_dir(StageRunner.STAGE_GRAPH_CANDIDATES)
            / "graph_entities.json"
        ).read_text(encoding="utf-8")
    )
    assert persisted == result


async def test_build_graph_rejects_empty_csv(processor, tmp_path):
    runner = _runner(processor, tmp_path)
    source = runner.get_stage_dir(StageRunner.STAGE_SOURCE) / "source.csv"
    source.write_text("model,category\n", encoding="utf-8")

    with pytest.raises(RuntimeError, match="CSV file is empty"):
        await processor._stage_build_graph("job-1", "upload")


async def test_upload_and_complete_stages_report_observable_results(
    processor, tmp_path
):
    runner = _runner(processor, tmp_path)
    graph_file = (
        runner.get_stage_dir(StageRunner.STAGE_GRAPH_CANDIDATES)
        / "graph_entities.json"
    )
    graph_file.write_text(
        json.dumps({"nodes": [{}, {}], "edges": [{}]}), encoding="utf-8"
    )

    result = await processor._stage_upload_artifacts("job-1", "upload")
    await processor._stage_complete("job-1")

    assert result == {"nodes_uploaded": 2, "edges_uploaded": 1}
    assert processor._jobs["job-1"]["progress"] == 100.0
