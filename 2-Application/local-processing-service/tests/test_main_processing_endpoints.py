"""FastAPI route tests for the admin-launched local processor contract."""

import importlib
import sys
import uuid
from pathlib import Path
from unittest.mock import AsyncMock

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
    monkeypatch.setenv("EMBEDDING_MODEL", "text-embedding-qwen3-embedding-8b")
    monkeypatch.setenv("TOKENIZER_MODEL_PATH", str(tmp_path / "tokenizer"))
    monkeypatch.setenv("MCR_API_BASE_URL", "https://localhost:7215")
    monkeypatch.setenv("AZURE_STORAGE_ACCOUNT_URL", "https://storage.example")
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "")
    monkeypatch.setenv("GRAPH_EXTRACTION_ENDPOINT", "http://127.0.0.1:1234")
    monkeypatch.setenv("GRAPH_EXTRACTION_MODEL", "qwen3.5-0.8b")
    from embeddings import embedder_factory
    from embeddings.model_discovery import ModelDiscoveryResult

    embedder_factory.reset_embedder()
    monkeypatch.setattr(
        embedder_factory,
        "discover_embedding_models_sync",
        lambda endpoint: ModelDiscoveryResult(
            "openai-compatible",
            endpoint,
            ["text-embedding-qwen3-embedding-8b"],
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
    assert main.os.environ["EMBEDDING_MODEL"] == "text-embedding-qwen3-embedding-8b"
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
