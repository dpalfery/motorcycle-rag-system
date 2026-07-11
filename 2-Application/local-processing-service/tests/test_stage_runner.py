"""Unit tests for restartable stage execution and checkpoint metadata."""

import json
from unittest.mock import AsyncMock, patch

import pytest

from infrastructure.stage_runner import (
    RunMetadata,
    RunStatus,
    StageMetadata,
    StageRunner,
    StageStatus,
)


def _run_metadata(tmp_path, stages=None):
    return RunMetadata(
        run_id="run-1",
        document_id="doc-1",
        run_type="test",
        status=RunStatus.RUNNING,
        started_at="2026-01-01T00:00:00+00:00",
        local_working_folder=str(tmp_path),
        stages=stages or {},
    )


def test_metadata_round_trip_preserves_optional_fields(tmp_path):
    stage = StageMetadata(
        stage_name="one",
        status=StageStatus.FAILED,
        started_at="start",
        completed_at="end",
        artifact_path="artifact",
        artifact_hash="hash",
        metadata_json={"count": 2},
        error_detail="boom",
        retry_count=2,
    )
    run = _run_metadata(tmp_path, {"one": stage})
    run.completed_at = "end"
    run.completed_stage = "one"
    run.processor_host = "host"
    run.error_summary = "boom"

    restored = RunMetadata.from_dict(run.to_dict())

    assert restored == run
    assert restored.stages["one"].to_dict()["metadata"] == {"count": 2}


def test_checkpoint_save_load_and_invalid_file(tmp_path):
    runner = StageRunner("run-1", tmp_path, ["one", "two"])
    metadata = _run_metadata(
        tmp_path,
        {"one": StageMetadata("one", StageStatus.COMPLETED)},
    )

    runner.save_checkpoint(metadata)
    restored = runner.load_checkpoint()

    assert restored == metadata
    assert runner.get_next_stage() == "two"
    (tmp_path / "run-metadata.json").write_text("{bad json", encoding="utf-8")
    assert runner.load_checkpoint() is None


def test_missing_checkpoint_and_basic_accessors(tmp_path):
    runner = StageRunner("run-2", tmp_path, ["one"])

    assert runner.load_checkpoint() is None
    assert runner.run_id == "run-2"
    assert runner.working_dir == tmp_path
    assert runner.get_stage_dir("one").is_dir()
    assert runner.get_stage_status("unknown") is StageStatus.PENDING
    assert runner.is_stage_complete("one") is False


async def test_run_stage_completes_and_skips_repeated_execution(tmp_path):
    runner = StageRunner("run", tmp_path, ["one"])
    stage_func = AsyncMock(return_value={"ok": True})

    result = await runner.run_stage("one", stage_func, metadata={"source": "unit"})
    skipped = await runner.run_stage("one", stage_func)

    assert result == {"ok": True}
    assert skipped is None
    assert runner.get_stage_status("one") is StageStatus.COMPLETED
    assert runner._stages["one"].metadata_json == {"source": "unit"}
    stage_func.assert_awaited_once()


async def test_run_stage_retries_then_succeeds_without_real_delay(tmp_path):
    runner = StageRunner("run", tmp_path, ["one"])
    stage_func = AsyncMock(side_effect=[ValueError("transient"), "done"])

    with patch("infrastructure.stage_runner.asyncio.sleep", new=AsyncMock()) as sleep:
        result = await runner.run_stage(
            "one", stage_func, retry_on_failure=True, max_retries=2
        )

    assert result == "done"
    assert runner._stages["one"].retry_count == 1
    assert runner._stages["one"].error_detail is None
    sleep.assert_awaited_once_with(1)


async def test_run_stage_marks_terminal_failure(tmp_path):
    runner = StageRunner("run", tmp_path, ["one"])

    with pytest.raises(RuntimeError, match="permanent"):
        await runner.run_stage(
            "one",
            AsyncMock(side_effect=RuntimeError("permanent")),
            retry_on_failure=False,
        )

    stage = runner._stages["one"]
    assert stage.status is StageStatus.FAILED
    assert stage.error_detail == "permanent"
    assert stage.completed_at is not None


async def test_run_pipeline_resumes_and_skips_missing_functions(tmp_path):
    runner = StageRunner("run", tmp_path, ["one", "two", "three"])
    two = AsyncMock(return_value=2)

    results = await runner.run_pipeline({"two": two}, start_from="two")

    assert results == {"two": 2}
    assert runner.get_next_stage() == "one"


def test_run_summary_counts_known_states(tmp_path):
    runner = StageRunner("run", tmp_path, ["done", "failed", "pending", "absent"])
    runner._stages = {
        "done": StageMetadata("done", StageStatus.COMPLETED),
        "failed": StageMetadata("failed", StageStatus.FAILED),
        "pending": StageMetadata("pending", StageStatus.PENDING),
    }

    summary = runner.get_run_summary()

    assert summary["completed"] == 1
    assert summary["failed"] == 1
    assert summary["pending"] == 1
    assert summary["next_stage"] == "failed"
    assert summary["total_stages"] == 4


def test_save_checkpoint_swallows_write_error(tmp_path):
    runner = StageRunner("run", tmp_path, ["one"])

    with patch("builtins.open", side_effect=OSError("disk full")):
        runner.save_checkpoint(_run_metadata(tmp_path))

    assert runner._run_metadata.status is RunStatus.RUNNING
