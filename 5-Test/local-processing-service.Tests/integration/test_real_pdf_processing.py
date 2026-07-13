"""End-to-end manual processing system test.

This test intentionally does not instantiate processor classes or mock the API.
It drives the same local-admin flow used by Admin Desktop:

1. POST /api/ingestion/jobs with an Admin-generated uploadId and processorRunId.
2. Copy the selected PDF into the processor watch folder.
3. Publish the Admin-shaped watch-folder manifest.
4. Wait for the real local processor to detect the manifest and upload artifacts.
5. Poll the real backend API until the persisted ingestion job completes.
6. Query the configured in-memory Azure Search shim for indexed manual content.

Required environment:
    MCR_E2E_API_BASE_URL
    MCR_E2E_ADMIN_BEARER_TOKEN
    MCR_E2E_PROCESSOR_BASE_URL
    MCR_E2E_WATCH_FOLDER
    MCR_E2E_SEARCH_SHIM_URL

Optional environment:
    MCR_E2E_MANUAL_PDF_PATH
    MCR_E2E_TIMEOUT_SECONDS
    MCR_E2E_SEARCH_QUERY
    MCR_E2E_CA_BUNDLE
    MCR_E2E_ADMIN_QUEUE_COMMAND

The search shim must be the API's configured Azure Search replacement. It is
expected to expose GET /health and GET /search?q=...&uploadId=..., returning
JSON with either {"results": [...]} or a top-level list. The test does not send
documents to the shim; the backend artifact upload path must do that.
The backend API must be started with Search:ChunkIndexingProvider=InMemoryShim
and Search:InMemoryShimEndpoint pointing at this same shim URL.
"""

from __future__ import annotations

import asyncio
import json
import os
import shlex
import subprocess
import uuid
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import httpx
import pytest


REAL_MANUAL_PATH = Path(
    "/Users/dave/Library/CloudStorage/OneDrive-Personal/Code/"
    "Motorcycle-manuals/ml.remawmom.amjc2626omen.pdf"
)

EXPECTED_STAGE_ORDER = [
    "copying",
    "parsing",
    "chunking",
    "embedding",
    "uploading-chunks",
    "extracting-graph",
    "uploading-graph",
    "completed",
]


@dataclass(frozen=True)
class SystemTestConfig:
    api_base_url: str
    admin_bearer_token: str
    processor_base_url: str
    watch_folder: Path
    manual_pdf_path: Path
    search_shim_url: str
    timeout_seconds: int
    search_query: str
    http_verify: bool | str
    admin_queue_command: list[str]


def _required_env(name: str) -> str:
    value = os.getenv(name, "").strip()
    if not value:
        pytest.skip(
            f"{name} environment variable not set — skipping manual-processing "
            "system test that requires real local runtime dependencies."
        )
    return value


def _load_config() -> SystemTestConfig:
    ca_bundle = os.getenv("MCR_E2E_CA_BUNDLE", "").strip()
    return SystemTestConfig(
        api_base_url=_required_env("MCR_E2E_API_BASE_URL").rstrip("/"),
        admin_bearer_token=_required_env("MCR_E2E_ADMIN_BEARER_TOKEN"),
        processor_base_url=_required_env("MCR_E2E_PROCESSOR_BASE_URL").rstrip("/"),
        watch_folder=Path(_required_env("MCR_E2E_WATCH_FOLDER")).expanduser().resolve(),
        manual_pdf_path=Path(os.getenv("MCR_E2E_MANUAL_PDF_PATH", str(REAL_MANUAL_PATH)))
        .expanduser()
        .resolve(),
        search_shim_url=_required_env("MCR_E2E_SEARCH_SHIM_URL").rstrip("/"),
        timeout_seconds=int(os.getenv("MCR_E2E_TIMEOUT_SECONDS", "900")),
        search_query=os.getenv("MCR_E2E_SEARCH_QUERY", "Honda CBR600RR"),
        http_verify=ca_bundle or True,
        admin_queue_command=_resolve_admin_queue_command(),
    )


def _resolve_admin_queue_command() -> list[str]:
    configured = os.getenv("MCR_E2E_ADMIN_QUEUE_COMMAND", "").strip()
    if configured:
        return shlex.split(configured)

    repo_root = Path(__file__).resolve().parents[4]
    return [
        "cargo",
        "run",
        "--quiet",
        "--manifest-path",
        str(repo_root / "1-Presentation/MotorcycleRAG.AdminDesktop/src-tauri/Cargo.toml"),
        "--bin",
        "queue-local-ingestion-work-item",
        "--",
    ]


def _assert_manual_exists(path: Path) -> None:
    if not path.is_file():
        pytest.fail(f"Required real manual PDF is missing: {path}")
    if path.suffix.lower() != ".pdf":
        pytest.fail(f"Manual processing system test requires a real PDF file: {path}")


def _assert_watch_folder_is_real_and_writable(path: Path) -> None:
    files_dir = path / "files"
    manifests_dir = path / "manifests"
    try:
        files_dir.mkdir(parents=True, exist_ok=True)
        manifests_dir.mkdir(parents=True, exist_ok=True)
        probe = manifests_dir / f".write-probe-{uuid.uuid4()}"
        probe.write_text("ok", encoding="utf-8")
        probe.unlink()
    except OSError as exc:
        pytest.fail(f"Configured watch folder is not writable: {path} ({exc})")


def _admin_headers(config: SystemTestConfig) -> dict[str, str]:
    return {"Authorization": f"Bearer {config.admin_bearer_token}"}


async def _assert_api_ready(client: httpx.AsyncClient, config: SystemTestConfig) -> None:
    try:
        response = await client.get(f"{config.api_base_url}/health", timeout=30.0)
    except httpx.HTTPError as exc:
        pytest.fail(f"Backend API is not reachable at {config.api_base_url}: {exc}")

    if response.status_code >= 500:
        pytest.fail(
            f"Backend API health failed with HTTP {response.status_code}: "
            f"{response.text[:1000]}"
        )


async def _assert_processor_ready(
    client: httpx.AsyncClient, config: SystemTestConfig
) -> None:
    try:
        response = await client.get(f"{config.processor_base_url}/health", timeout=30.0)
    except httpx.HTTPError as exc:
        pytest.fail(
            f"Local processor is not reachable at {config.processor_base_url}: {exc}"
        )

    if response.status_code != 200:
        pytest.fail(
            f"Local processor health must be healthy before the system test. "
            f"HTTP {response.status_code}: {response.text[:1000]}"
        )

    payload = response.json()
    services = payload.get("services", {})
    diagnostics = {
        "accepting_work": payload.get("accepting_work"),
        "api_client_configured": payload.get("api_client_configured"),
        "embedding_provider": services.get("embedding_provider"),
        "embedding_model": services.get("embedding_model"),
        "tokenizer_status": services.get("tokenizer_status"),
        "tokenizer_source": services.get("tokenizer_source"),
        "blob_storage": services.get("blob_storage"),
    }
    required = {
        "accepting_work": True,
        "api_client_configured": True,
        "embedding_provider": "connected",
        "tokenizer_status": "configured",
        "blob_storage": True,
    }
    missing = {
        key: {"expected": expected, "actual": diagnostics.get(key)}
        for key, expected in required.items()
        if diagnostics.get(key) != expected
    }
    if missing:
        pytest.fail(
            "Local processor is missing required runtime dependencies: "
            f"{json.dumps(missing, indent=2)}\n"
            f"Full diagnostics: {json.dumps(diagnostics, indent=2)}"
        )
    if not services.get("embedding_model"):
        pytest.fail(f"Embedding model is not configured: {json.dumps(diagnostics)}")


async def _assert_search_shim_ready(
    client: httpx.AsyncClient, config: SystemTestConfig
) -> None:
    try:
        response = await client.get(f"{config.search_shim_url}/health", timeout=30.0)
    except httpx.HTTPError as exc:
        pytest.fail(
            f"In-memory Azure Search shim is not reachable at "
            f"{config.search_shim_url}: {exc}"
        )

    if response.status_code >= 400:
        pytest.fail(
            f"In-memory Azure Search shim health failed with HTTP "
            f"{response.status_code}: {response.text[:1000]}"
        )

    reset_response = await client.post(f"{config.search_shim_url}/reset", timeout=30.0)
    if reset_response.status_code >= 400:
        pytest.fail(
            f"In-memory Azure Search shim reset failed with HTTP "
            f"{reset_response.status_code}: {reset_response.text[:1000]}"
        )


async def _start_admin_job(
    client: httpx.AsyncClient,
    config: SystemTestConfig,
    upload_id: str,
    processor_run_id: str,
) -> dict[str, Any]:
    response = await client.post(
        f"{config.api_base_url}/api/ingestion/jobs",
        headers=_admin_headers(config),
        json={
            "uploadId": upload_id,
            "documentType": "manual-pdf",
            "processorRunId": processor_run_id,
            "configuration": {"processingMode": "local"},
        },
        timeout=60.0,
    )
    if response.status_code != 202:
        pytest.fail(
            f"Admin job creation failed. HTTP {response.status_code}: "
            f"{response.text[:2000]}"
        )
    return response.json()


def _publish_admin_watch_item(
    config: SystemTestConfig,
    *,
    job_id: str,
    upload_id: str,
    processor_run_id: str,
) -> dict[str, Any]:
    command = [
        *config.admin_queue_command,
        "--watch-folder",
        str(config.watch_folder),
        "--source-path",
        str(config.manual_pdf_path),
        "--job-id",
        job_id,
        "--upload-id",
        upload_id,
        "--processor-run-id",
        processor_run_id,
        "--document-type",
        "manual-pdf",
        "--created-at-utc",
        datetime.now(timezone.utc).isoformat(),
        "--source-file-name",
        config.manual_pdf_path.name,
        "--size",
        str(config.manual_pdf_path.stat().st_size),
    ]
    completed = subprocess.run(
        command,
        check=False,
        capture_output=True,
        text=True,
        timeout=60,
    )
    if completed.returncode != 0:
        pytest.fail(
            "Admin Desktop queue command failed. "
            f"Command={command!r}, stdout={completed.stdout[:1000]!r}, "
            f"stderr={completed.stderr[:1000]!r}"
        )

    try:
        result = json.loads(completed.stdout)
    except json.JSONDecodeError:
        pytest.fail(
            "Admin Desktop queue command returned invalid JSON. "
            f"stdout={completed.stdout[:1000]!r}, stderr={completed.stderr[:1000]!r}"
        )

    if not isinstance(result, dict):
        pytest.fail(f"Admin Desktop queue command returned invalid result: {result!r}")

    return result


async def _wait_for_job_completion(
    client: httpx.AsyncClient,
    config: SystemTestConfig,
    job_id: str,
    processor_run_id: str,
) -> tuple[dict[str, Any], list[str]]:
    deadline = asyncio.get_running_loop().time() + config.timeout_seconds
    sampled_stages: list[str] = []
    last_status: dict[str, Any] | None = None

    while asyncio.get_running_loop().time() < deadline:
        response = await client.get(
            f"{config.api_base_url}/api/ingestion/jobs/{job_id}",
            headers=_admin_headers(config),
            timeout=30.0,
        )
        if response.status_code != 200:
            pytest.fail(
                f"Job status poll failed. HTTP {response.status_code}: "
                f"{response.text[:1000]}"
            )

        last_status = response.json()
        stage = (last_status.get("currentStage") or "").strip().lower()
        if stage and (not sampled_stages or sampled_stages[-1] != stage):
            sampled_stages.append(stage)

        processor_response = await client.get(
            f"{config.processor_base_url}/jobs/{processor_run_id}", timeout=30.0
        )
        if processor_response.status_code == 200:
            processor_status = processor_response.json()
            processor_stage = (
                processor_status.get("stage")
                or processor_status.get("currentStage")
                or ""
            ).strip().lower()
            if processor_stage and (
                not sampled_stages or sampled_stages[-1] != processor_stage
            ):
                sampled_stages.append(processor_stage)

        status = (last_status.get("status") or "").strip().lower()
        if status in {"failed", "cancelled", "partiallycompleted"}:
            return last_status, sampled_stages
        if status == "completed" and stage == "completed":
            return last_status, sampled_stages

        await asyncio.sleep(2)

    pytest.fail(
        "Manual processing job did not finish before timeout. "
        f"Sampled stages={sampled_stages}. Last status={last_status}"
    )


async def _assert_processor_job_completed(
    client: httpx.AsyncClient,
    config: SystemTestConfig,
    processor_run_id: str,
) -> dict[str, Any]:
    deadline = asyncio.get_running_loop().time() + config.timeout_seconds
    last_payload: dict[str, Any] | None = None

    while asyncio.get_running_loop().time() < deadline:
        response = await client.get(
            f"{config.processor_base_url}/jobs/{processor_run_id}", timeout=30.0
        )
        if response.status_code == 404:
            await asyncio.sleep(1)
            continue
        if response.status_code != 200:
            pytest.fail(
                f"Processor job status endpoint failed for processorRunId "
                f"{processor_run_id}. HTTP {response.status_code}: {response.text[:1000]}"
            )

        last_payload = response.json()
        status = (last_payload.get("status") or "").lower()
        if status == "completed":
            return last_payload
        if status in {"failed", "error", "cancelled"}:
            pytest.fail(
                f"Processor job reached terminal failure: "
                f"{json.dumps(last_payload, indent=2)}"
            )

        await asyncio.sleep(2)

    pytest.fail(
        "Processor job did not complete before timeout. "
        f"Last status: {json.dumps(last_payload, indent=2)}"
    )


def _assert_stage_order(processor_job: dict[str, Any]) -> None:
    history = processor_job.get("stage_history")
    if not isinstance(history, list) or not history:
        pytest.fail(
            "Processor job status did not include durable stage_history. "
            f"Processor status: {json.dumps(processor_job, indent=2)}"
        )

    observed = [
        str(entry.get("stage", "")).strip().lower()
        for entry in history
        if isinstance(entry, dict) and entry.get("stage")
    ]
    stage_positions = {stage: index for index, stage in enumerate(EXPECTED_STAGE_ORDER)}
    unknown = [stage for stage in observed if stage not in stage_positions]
    if unknown:
        pytest.fail(f"Unexpected pipeline stages reported by API: {unknown}")

    numeric = [stage_positions[stage] for stage in observed]
    if numeric != sorted(numeric):
        pytest.fail(f"Pipeline stages were reported out of order: {observed}")
    missing = [stage for stage in EXPECTED_STAGE_ORDER if stage not in observed]
    if missing:
        pytest.fail(
            "Not every expected manual-processing stage was observed. "
            f"Missing stages={missing}. Stage history={history}"
        )


async def _assert_search_shim_has_indexed_manual(
    client: httpx.AsyncClient,
    config: SystemTestConfig,
    upload_id: str,
) -> list[dict[str, Any]]:
    response = await client.get(
        f"{config.search_shim_url}/search",
        params={"q": config.search_query, "uploadId": upload_id},
        timeout=30.0,
    )
    if response.status_code != 200:
        pytest.fail(
            f"In-memory Azure Search shim query failed. HTTP "
            f"{response.status_code}: {response.text[:1000]}"
        )

    payload = response.json()
    results = payload.get("results", payload) if isinstance(payload, dict) else payload
    if not isinstance(results, list) or not results:
        pytest.fail(
            "In-memory Azure Search shim returned no results for the indexed manual. "
            f"Query={config.search_query!r}, uploadId={upload_id}, payload={payload}"
        )

    first = results[0]
    if not isinstance(first, dict):
        pytest.fail(f"Search shim result shape is invalid: {first!r}")

    vector = first.get("contentVector")
    content = str(first.get("content", ""))
    if not isinstance(vector, list) or not vector or not all(
        isinstance(value, (float, int)) for value in vector[:10]
    ):
        pytest.fail(
            "Search shim did not preserve production-shaped contentVector values "
            f"in the indexed result: {first.keys()}"
        )
    if not content.strip():
        pytest.fail("Search shim result did not preserve manual content.")

    return results


async def _delete_created_job(
    client: httpx.AsyncClient,
    config: SystemTestConfig,
    job_id: str | None,
) -> None:
    if not job_id:
        return

    await client.delete(
        f"{config.api_base_url}/api/ingestion/jobs/{job_id}",
        headers=_admin_headers(config),
        timeout=30.0,
    )


@pytest.mark.slow
async def test_admin_manual_processing_flow_completes_with_real_runtime_dependencies():
    config = _load_config()
    _assert_manual_exists(config.manual_pdf_path)
    _assert_watch_folder_is_real_and_writable(config.watch_folder)

    upload_id = str(uuid.uuid4())
    processor_run_id = str(uuid.uuid4())

    async with httpx.AsyncClient(verify=config.http_verify) as client:
        await _assert_api_ready(client, config)
        await _assert_processor_ready(client, config)
        await _assert_search_shim_ready(client, config)

        created_job = await _start_admin_job(
            client, config, upload_id, processor_run_id
        )
        job_id = created_job.get("jobId")
        if not job_id:
            pytest.fail(f"API did not return jobId: {created_job}")

        try:
            queue_result = _publish_admin_watch_item(
                config,
                job_id=job_id,
                upload_id=upload_id,
                processor_run_id=processor_run_id,
            )
        except BaseException:
            await _delete_created_job(client, config, job_id)
            raise

        manifest_path = (
            Path(str(queue_result["watchFolder"]))
            / "manifests"
            / str(queue_result["manifestFileName"])
        )
        assert manifest_path.exists() or manifest_path.with_suffix(".accepted").exists()

        final_job, _sampled_stages = await _wait_for_job_completion(
            client, config, job_id, processor_run_id
        )
        processor_job = await _assert_processor_job_completed(
            client, config, processor_run_id
        )
        search_results = await _assert_search_shim_has_indexed_manual(
            client, config, upload_id
        )

    _assert_stage_order(processor_job)
    assert (final_job.get("status") or "").lower() == "completed", final_job
    assert final_job.get("docIngestionRunId") == processor_run_id
    assert final_job.get("expectedChunkCount", 0) > 0
    assert final_job.get("indexedChunkCount") == final_job.get("expectedChunkCount")
    assert processor_job.get("chunks_processed", 0) == final_job.get("expectedChunkCount")
    assert processor_job.get("total_chunks", 0) == final_job.get("expectedChunkCount")
    assert len(search_results) > 0
