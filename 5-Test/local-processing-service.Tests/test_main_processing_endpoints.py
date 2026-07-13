"""FastAPI route tests for the admin-launched local processor contract."""

import importlib
import sys
import uuid
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock, patch

import httpx
import pytest


def _load_main_with_admin_environment(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    monkeypatch.setenv("PORT", "8100")
    monkeypatch.setenv("PYTHONUNBUFFERED", "1")
    monkeypatch.setenv("WATCH_FOLDER_DISABLED", "1")
    monkeypatch.setenv("WATCH_FOLDER", str(tmp_path / "watch"))
    monkeypatch.setenv("LOCAL_PROCESSOR_INPUT_DIR", str(tmp_path))
    monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "http://127.0.0.1:1234")
    monkeypatch.setenv("EMBEDDING_MODEL", "text-embedding-qwen3-embedding-4b")
    monkeypatch.setenv("TOKENIZER_MODEL_PATH", str(tmp_path / "tokenizer"))
    monkeypatch.setenv("MCR_API_BASE_URL", "https://localhost:7215")
    monkeypatch.setenv("AZURE_STORAGE_ACCOUNT_URL", "https://storage.example")
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "")
    monkeypatch.setenv("GRAPH_EXTRACTION_ENDPOINT", "http://127.0.0.1:1234")
    monkeypatch.setenv("GRAPH_EXTRACTION_MODEL", "microsoft/phi-4-reasoning-plus")
    from embeddings import embedder_factory
    from embeddings.model_discovery import ModelDiscoveryResult

    embedder_factory.reset_embedder()
    monkeypatch.setattr(
        embedder_factory,
        "discover_embedding_models_sync",
        lambda endpoint: ModelDiscoveryResult(
            "openai-compatible",
            endpoint,
            ["text-embedding-qwen3-embedding-4b"],
        ),
    )
    sys.modules.pop("main", None)
    return importlib.import_module("main")


class _FakePdfProcessor:
    def __init__(self):
        self.process_pdf_async = AsyncMock(return_value="pdf-job-1")


class _FakeCsvProcessor:
    def __init__(self):
        self.process_csv_async = AsyncMock(return_value="csv-job-1")


class _FakeBikeGraphProcessor:
    def __init__(self):
        self.process_async = AsyncMock(return_value="bike-job-1")


async def _post_json(app, path: str, payload: dict) -> httpx.Response:
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(
        transport=transport, base_url="http://testserver"
    ) as client:
        return await client.post(path, json=payload)


@pytest.mark.asyncio
async def test_pdf_endpoint_uses_admin_environment_and_passes_source_token(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    fake_processor = _FakePdfProcessor()
    monkeypatch.setattr(main, "pdf_processor", fake_processor)

    source_token = str(uuid.uuid4())
    response = await _post_json(
        main.app,
        "/process/pdf",
        {
            "upload_id": "upload-pdf-route",
            "document_type": "manual",
            "blob_container": "raw-uploads",
            "sourceAccessToken": source_token,
            "metadata": {"make": "Honda", "model": "CBR600RR", "year": 2026},
        },
    )

    assert response.status_code == 200
    assert response.json()["job_id"] == "pdf-job-1"
    fake_processor.process_pdf_async.assert_awaited_once()
    kwargs = fake_processor.process_pdf_async.await_args.kwargs
    assert kwargs["upload_id"] == "upload-pdf-route"
    assert kwargs["document_type"] == "manual"
    assert kwargs["blob_container"] == "raw-uploads"
    assert kwargs["source_access_token"] == source_token
    assert kwargs["metadata"].make == "Honda"
    assert main.os.environ["EMBEDDING_PROVIDER_ENDPOINT"] == "http://127.0.0.1:1234"
    assert main.os.environ["EMBEDDING_MODEL"] == "text-embedding-qwen3-embedding-4b"
    assert main.os.environ["TOKENIZER_MODEL_PATH"] == str(tmp_path / "tokenizer")


@pytest.mark.asyncio
async def test_pdf_endpoint_passes_local_file_path_from_admin_route(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    fake_processor = _FakePdfProcessor()
    monkeypatch.setattr(main, "pdf_processor", fake_processor)
    pdf_path = tmp_path / "manual.pdf"
    pdf_path.write_bytes(b"%PDF-1.4\n")

    response = await _post_json(
        main.app,
        "/process/pdf",
        {
            "upload_id": "upload-pdf-local-route",
            "document_type": "manual",
            "blob_container": "raw-uploads",
            "local_file_path": str(pdf_path),
            "metadata": {"make": "Honda", "model": "CBR600RR", "year": 2026},
        },
    )

    assert response.status_code == 200
    assert response.json()["job_id"] == "pdf-job-1"
    fake_processor.process_pdf_async.assert_awaited_once()
    kwargs = fake_processor.process_pdf_async.await_args.kwargs
    assert kwargs["upload_id"] == "upload-pdf-local-route"
    assert kwargs["source_access_token"] is None
    assert kwargs["local_file_path"] == str(pdf_path.resolve())


@pytest.mark.asyncio
async def test_csv_endpoint_passes_local_file_path_from_admin_route(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    fake_processor = _FakeCsvProcessor()
    monkeypatch.setattr(main, "csv_processor", fake_processor)
    csv_path = tmp_path / "specs.csv"
    csv_path.write_text("make,model,year\nHonda,CB500,2020\n", encoding="utf-8")

    response = await _post_json(
        main.app,
        "/process/csv",
        {
            "upload_id": "upload-csv-route",
            "local_file_path": str(csv_path),
            "metadata": {"make": "Honda", "model": "CB500", "year": 2020},
        },
    )

    assert response.status_code == 200
    assert response.json()["job_id"] == "csv-job-1"
    fake_processor.process_csv_async.assert_awaited_once()
    kwargs = fake_processor.process_csv_async.await_args.kwargs
    assert kwargs["upload_id"] == "upload-csv-route"
    assert kwargs["local_file_path"] == str(csv_path.resolve())
    assert kwargs["metadata"].model == "CB500"


@pytest.mark.asyncio
async def test_bike_graph_endpoint_passes_local_file_path_from_admin_route(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    fake_processor = _FakeBikeGraphProcessor()
    monkeypatch.setattr(main, "bike_graph_processor", fake_processor)
    csv_path = tmp_path / "bike-graph.csv"
    csv_path.write_text("Model,Year,Category\nHonda CB500F,2022,Naked\n", encoding="utf-8")

    response = await _post_json(
        main.app,
        "/process/bike-graph",
        {
            "upload_id": "upload-bike-route",
            "local_file_path": str(csv_path),
        },
    )

    assert response.status_code == 200
    assert response.json()["job_id"] == "bike-job-1"
    fake_processor.process_async.assert_awaited_once_with(
        upload_id="upload-bike-route",
        blob_container=None,
        local_file_path=str(csv_path.resolve()),
    )


@pytest.mark.asyncio
async def test_job_helpers_sort_count_find_stop_and_cleanup(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)

    def processor(jobs, *, stop_result=None, removed=0):
        value = MagicMock()
        value.list_jobs = AsyncMock(return_value=jobs)
        value.get_job_status = AsyncMock(return_value=None)
        value.stop_job = AsyncMock(return_value=stop_result)
        value.clear_terminal_jobs = AsyncMock(return_value=removed)
        return value

    pdf = processor(
        [{"job_id": "old", "status": "completed", "created_at": "2026-01-01"}],
        removed=1,
    )
    csv = processor(
        [{"job_id": "active", "status": " processing ", "created_at": "2026-02-01"}],
        stop_result={"job_id": "active", "status": "cancelled"},
        removed=2,
    )
    bike = processor([], removed=0)
    monkeypatch.setattr(main, "pdf_processor", pdf)
    monkeypatch.setattr(main, "csv_processor", csv)
    monkeypatch.setattr(main, "bike_graph_processor", bike)

    assert [job["job_id"] for job in await main.list_jobs()] == ["active", "old"]
    assert await main._count_active_jobs() == 1
    assert (await main.stop_job("active"))["status"] == "cancelled"
    cleanup = await main.clear_finished_jobs()
    assert cleanup["deleted_count"] == 3
    assert cleanup["remaining_jobs"] == 2

    csv.get_job_status.return_value = {"job_id": "active"}
    assert await main.get_job_status("active") == {"job_id": "active"}
    csv.get_job_status.return_value = None
    with pytest.raises(Exception) as exc:
        await main.get_job_status("missing")
    assert exc.value.status_code == 404


@pytest.mark.asyncio
async def test_worker_lifecycle_honors_disabled_and_enabled_configuration(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    monkeypatch.setenv("WATCH_FOLDER_DISABLED", "yes")
    constructor = MagicMock()
    monkeypatch.setattr(main, "WatchFolderWorker", constructor)

    await main._start_watch_folder_worker()
    constructor.assert_not_called()

    monkeypatch.setenv("WATCH_FOLDER_DISABLED", "false")
    worker = MagicMock()
    worker.stop = AsyncMock()
    constructor.return_value = worker
    await main._start_watch_folder_worker()
    worker.start.assert_called_once()
    await main._stop_watch_folder_worker()
    worker.stop.assert_awaited_once()


@pytest.mark.asyncio
async def test_lifespan_starts_and_stops_worker(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    start = AsyncMock()
    stop = AsyncMock()
    monkeypatch.setattr(main, "_start_watch_folder_worker", start)
    monkeypatch.setattr(main, "_stop_watch_folder_worker", stop)

    async with main.lifespan(main.app):
        start.assert_awaited_once()

    stop.assert_awaited_once()


@pytest.mark.asyncio
async def test_shutdown_requests_drain_task(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    monkeypatch.setattr(main, "_count_active_jobs", AsyncMock(return_value=2))
    created = []

    def capture(coroutine):
        created.append(coroutine)
        return MagicMock()

    monkeypatch.setattr(main.asyncio, "create_task", capture)
    result = await main.shutdown()
    created[0].close()

    assert main.shutdown_requested is True
    assert result["active_jobs"] == 2


@pytest.mark.asyncio
async def test_graceful_shutdown_sets_uvicorn_exit_without_killing_process(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    monkeypatch.setattr(main, "_count_active_jobs", AsyncMock(return_value=0))
    server = MagicMock()
    main.uvicorn_server = server

    await main._wait_for_graceful_shutdown()

    assert server.should_exit is True


@pytest.mark.asyncio
async def test_embedding_model_discovery_translates_domain_errors(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    monkeypatch.setattr(
        main, "discover_embedding_models", AsyncMock(side_effect=ValueError("bad url"))
    )
    with pytest.raises(Exception) as invalid:
        await main.list_embedding_models("bad")
    assert invalid.value.status_code == 400

    monkeypatch.setattr(
        main,
        "discover_embedding_models",
        AsyncMock(side_effect=main.ModelDiscoveryError("offline")),
    )
    with pytest.raises(Exception) as unavailable:
        await main.list_embedding_models("http://provider")
    assert unavailable.value.status_code == 502


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("route", "processor_name", "method_name", "payload"),
    [
        (
            "/process/pdf",
            "pdf_processor",
            "process_pdf_async",
            {
                "upload_id": "upload",
                "document_type": "manual",
                "blob_container": "raw",
                "sourceAccessToken": "token",
            },
        ),
        (
            "/process/csv",
            "csv_processor",
            "process_csv_async",
            {"upload_id": "upload", "blob_container": "raw"},
        ),
        (
            "/process/bike-graph",
            "bike_graph_processor",
            "process_async",
            {"upload_id": "upload", "blob_container": "raw"},
        ),
    ],
)
async def test_processing_routes_hide_unexpected_errors(
    monkeypatch,
    tmp_path,
    route,
    processor_name,
    method_name,
    payload,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    processor = MagicMock()
    setattr(processor, method_name, AsyncMock(side_effect=RuntimeError("secret detail")))
    monkeypatch.setattr(main, processor_name, processor)

    response = await _post_json(main.app, route, payload)

    assert response.status_code == 500
    assert response.json()["detail"] == "An unexpected error occurred"


@pytest.mark.asyncio
@pytest.mark.parametrize(
    ("route", "payload", "shutdown_requested", "expected_status", "expected_detail"),
    [
        (
            "/process/pdf",
            {
                "upload_id": "upload",
                "document_type": "manual",
                "blob_container": "raw",
                "sourceAccessToken": "token",
            },
            True,
            409,
            "shutting down",
        ),
        (
            "/process/pdf",
            {
                "upload_id": "",
                "document_type": "manual",
                "blob_container": "raw",
                "sourceAccessToken": "token",
            },
            False,
            400,
            "upload_id is required",
        ),
        (
            "/process/pdf",
            {
                "upload_id": "upload",
                "document_type": "",
                "blob_container": "raw",
                "sourceAccessToken": "token",
            },
            False,
            400,
            "document_type is required",
        ),
        (
            "/process/pdf",
            {
                "upload_id": "upload",
                "document_type": "manual",
                "blob_container": "raw",
            },
            False,
            400,
            "source_access_token is required",
        ),
        (
            "/process/csv",
            {"upload_id": "upload", "blob_container": "raw"},
            True,
            409,
            "shutting down",
        ),
        (
            "/process/csv",
            {"upload_id": "", "blob_container": "raw"},
            False,
            400,
            "upload_id is required",
        ),
        (
            "/process/csv",
            {"upload_id": "upload"},
            False,
            400,
            "Either blob_container or local_file_path is required",
        ),
        (
            "/process/bike-graph",
            {"upload_id": "upload", "blob_container": "raw"},
            True,
            409,
            "shutting down",
        ),
        (
            "/process/bike-graph",
            {"upload_id": "", "blob_container": "raw"},
            False,
            400,
            "upload_id is required",
        ),
        (
            "/process/bike-graph",
            {"upload_id": "upload"},
            False,
            400,
            "Either blob_container or local_file_path is required",
        ),
    ],
)
async def test_processing_routes_reject_invalid_work_without_starting_jobs(
    monkeypatch,
    tmp_path,
    route,
    payload,
    shutdown_requested,
    expected_status,
    expected_detail,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    monkeypatch.setattr(main, "shutdown_requested", shutdown_requested)

    response = await _post_json(main.app, route, payload)

    assert response.status_code == expected_status
    assert expected_detail in response.json()["detail"]


@pytest.mark.asyncio
async def test_embedding_model_discovery_returns_discovered_models(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    monkeypatch.setattr(
        main,
        "discover_embedding_models",
        AsyncMock(
            return_value=SimpleNamespace(
                provider="openai-compatible",
                endpoint="http://provider/v1",
                models=["motorcycle-embed"],
            )
        ),
    )

    response = await main.list_embedding_models("http://provider/v1")

    assert response.status_code == 200
    assert response.body == b'{"provider":"openai-compatible","endpoint":"http://provider/v1","models":["motorcycle-embed"]}'


@pytest.mark.asyncio
async def test_health_check_hides_unexpected_dependency_error(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    failed_embedder = MagicMock()
    failed_embedder.check_status = AsyncMock(side_effect=RuntimeError("provider secret"))
    monkeypatch.setattr(main, "embedder", failed_embedder)
    monkeypatch.setenv("GRAPH_EXTRACTION_ENDPOINT", "http://graph")
    monkeypatch.setenv("GRAPH_EXTRACTION_MODEL", "graph-model")

    response = await main.health_check()

    assert response.status_code == 503
    assert response.body == b'{"status":"unhealthy","accepting_work":true,"shutdown_requested":false,"active_jobs":0,"message":"Processor health check failed","services":{"embedding_provider":"unknown","graph_extraction":{"endpoint":"http://graph","model":"graph-model","status":"healthy"},"blob_storage":"unknown","service_uptime":"running"}}'


@pytest.mark.asyncio
async def test_job_routes_search_bike_graph_and_hide_unexpected_errors(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)

    pdf = MagicMock(get_job_status=AsyncMock(return_value=None), stop_job=AsyncMock(return_value=None))
    csv = MagicMock(get_job_status=AsyncMock(return_value=None), stop_job=AsyncMock(return_value=None))
    bike = MagicMock(
        get_job_status=AsyncMock(return_value={"job_id": "bike-job"}),
        stop_job=AsyncMock(return_value=None),
    )
    monkeypatch.setattr(main, "pdf_processor", pdf)
    monkeypatch.setattr(main, "csv_processor", csv)
    monkeypatch.setattr(main, "bike_graph_processor", bike)

    assert await main.get_job_status("bike-job") == {"job_id": "bike-job"}

    bike.get_job_status.side_effect = RuntimeError("internal detail")
    with pytest.raises(Exception) as status_error:
        await main.get_job_status("broken-job")
    assert status_error.value.status_code == 500
    assert status_error.value.detail == "An unexpected error occurred"

    bike.stop_job.side_effect = RuntimeError("internal detail")
    with pytest.raises(Exception) as stop_error:
        await main.stop_job("broken-job")
    assert stop_error.value.status_code == 500
    assert stop_error.value.detail == "An unexpected error occurred"


@pytest.mark.asyncio
async def test_stop_job_reports_not_found_after_all_processors_decline(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    for processor_name in ("pdf_processor", "csv_processor", "bike_graph_processor"):
        monkeypatch.setattr(
            main,
            processor_name,
            MagicMock(stop_job=AsyncMock(return_value=None)),
        )

    with pytest.raises(Exception) as error:
        await main.stop_job("missing-job")

    assert error.value.status_code == 404
    assert error.value.detail == "Job not found"


@pytest.mark.asyncio
async def test_graceful_shutdown_forces_exit_at_deadline_without_sleeping(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    loop = MagicMock()
    loop.time.side_effect = [0, 30]
    monkeypatch.setattr(main.asyncio, "get_running_loop", lambda: loop)
    monkeypatch.setattr(main, "_count_active_jobs", AsyncMock(return_value=1))
    server = MagicMock()
    main.uvicorn_server = server

    await main._wait_for_graceful_shutdown()

    assert server.should_exit is True


@pytest.mark.asyncio
async def test_graceful_shutdown_signals_process_when_server_is_not_available(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main_with_admin_environment(monkeypatch, tmp_path)
    monkeypatch.setattr(main, "_count_active_jobs", AsyncMock(return_value=0))
    monkeypatch.setattr(main.os, "getpid", lambda: 4242)
    kill = MagicMock()
    monkeypatch.setattr(main.os, "kill", kill)
    main.uvicorn_server = None

    await main._wait_for_graceful_shutdown()

    kill.assert_called_once_with(4242, main.signal.SIGTERM)
