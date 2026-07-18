import importlib
import json
import sys
from unittest.mock import patch

import pytest


def _load_main(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "")
    monkeypatch.setenv("EMBEDDING_BACKEND", "ollama")
    monkeypatch.setenv("MCR_API_BASE_URL", "https://api.example.test")
    monkeypatch.delenv("AZURE_STORAGE_ACCOUNT_URL", raising=False)
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "")
    monkeypatch.setenv(
        "AZURE_STORAGE_CONNECTION_STRING",
        "DefaultEndpointsProtocol=http;"
        "AccountName=devstoreaccount1;"
        "AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCzYI6tq/K1SZFPTOtr/KBHBeksoGMGw==;"
        "BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;",
    )
    sys.modules.pop("main", None)
    return importlib.import_module("main")


class _FakeEmbedder:
    def __init__(
        self,
        status: str,
        endpoint: str = "http://localhost:1234/v1",
        model: str = "qwen3-embedding",
    ):
        self._status = status
        self._endpoint = endpoint
        self._model = model

    async def check_status(self) -> str:
        return self._status


class _FakeBlobWriter:
    def __init__(self, connected: bool):
        self._connected = connected

    def is_connected(self) -> bool:
        return self._connected


class _FakeApiClient:
    def __init__(self, configured: bool):
        self._configured = configured

    def is_configured(self) -> bool:
        return self._configured


@pytest.mark.asyncio
async def test_health_check_returns_unhealthy_when_embedding_provider_disconnected(
    monkeypatch: pytest.MonkeyPatch,
):
    main = _load_main(monkeypatch)

    monkeypatch.setattr(main, "embedder", _FakeEmbedder("disconnected"))
    monkeypatch.setattr(main, "blob_writer", _FakeBlobWriter(True))
    monkeypatch.setattr(main, "api_client", _FakeApiClient(True))
    monkeypatch.setattr(main, "shutdown_requested", False)
    monkeypatch.setattr(
        main,
        "describe_chunker_tokenizer",
        lambda: {"tokenizer_status": "configured", "tokenizer_model": "m", "tokenizer_source": "s", "tokenizer_path": None},
    )

    async def _count_active_jobs() -> int:
        return 0

    monkeypatch.setattr(main, "_count_active_jobs", _count_active_jobs)

    response = await main.health_check()
    payload = json.loads(response.body)

    assert response.status_code == 503
    assert payload["status"] == "unhealthy"
    assert payload["accepting_work"] is False
    assert payload["services"]["embedding_provider"] == "disconnected"
    assert "Embedding provider unavailable" in payload["message"]


@pytest.mark.asyncio
async def test_health_check_returns_unhealthy_when_blob_storage_disconnected(
    monkeypatch: pytest.MonkeyPatch,
):
    main = _load_main(monkeypatch)

    monkeypatch.setattr(main, "embedder", _FakeEmbedder("connected"))
    monkeypatch.setattr(main, "blob_writer", _FakeBlobWriter(False))
    monkeypatch.setattr(main, "api_client", _FakeApiClient(True))
    monkeypatch.setattr(main, "shutdown_requested", False)
    monkeypatch.setattr(
        main,
        "describe_chunker_tokenizer",
        lambda: {"tokenizer_status": "configured", "tokenizer_model": "m", "tokenizer_source": "s", "tokenizer_path": None},
    )

    async def _count_active_jobs() -> int:
        return 0

    monkeypatch.setattr(main, "_count_active_jobs", _count_active_jobs)

    response = await main.health_check()
    payload = json.loads(response.body)

    assert response.status_code == 503
    assert payload["status"] == "unhealthy"
    assert payload["accepting_work"] is False
    assert payload["services"]["blob_storage"] is False
    assert "Blob storage is not configured" in payload["message"]


@pytest.mark.asyncio
async def test_health_check_returns_unhealthy_when_api_client_not_configured(
    monkeypatch: pytest.MonkeyPatch,
):
    main = _load_main(monkeypatch)

    monkeypatch.setattr(main, "embedder", _FakeEmbedder("connected"))
    monkeypatch.setattr(main, "blob_writer", _FakeBlobWriter(True))
    monkeypatch.setattr(main, "api_client", _FakeApiClient(False))
    monkeypatch.setattr(main, "shutdown_requested", False)
    monkeypatch.setattr(
        main,
        "describe_chunker_tokenizer",
        lambda: {"tokenizer_status": "configured", "tokenizer_model": "m", "tokenizer_source": "s", "tokenizer_path": None},
    )

    async def _count_active_jobs() -> int:
        return 0

    monkeypatch.setattr(main, "_count_active_jobs", _count_active_jobs)

    response = await main.health_check()
    payload = json.loads(response.body)

    assert response.status_code == 503
    assert payload["status"] == "unhealthy"
    assert payload["accepting_work"] is False
    assert payload["api_client_configured"] is False
    assert "Artifact upload is not configured" in payload["message"]


@pytest.mark.asyncio
async def test_health_check_returns_healthy_when_embedding_provider_connected(
    monkeypatch: pytest.MonkeyPatch,
):
    main = _load_main(monkeypatch)

    monkeypatch.setattr(main, "embedder", _FakeEmbedder("connected"))
    monkeypatch.setattr(main, "blob_writer", _FakeBlobWriter(True))
    monkeypatch.setattr(main, "api_client", _FakeApiClient(True))
    monkeypatch.setattr(main, "shutdown_requested", False)
    monkeypatch.setattr(
        main,
        "describe_chunker_tokenizer",
        lambda: {"tokenizer_status": "configured", "tokenizer_model": "m", "tokenizer_source": "s", "tokenizer_path": None},
    )

    async def _count_active_jobs() -> int:
        return 0

    monkeypatch.setattr(main, "_count_active_jobs", _count_active_jobs)

    response = await main.health_check()
    payload = json.loads(response.body)

    assert response.status_code == 200
    assert payload["status"] == "healthy"
    assert payload["accepting_work"] is True
    assert payload["services"]["embedding_provider"] == "connected"
    assert payload["message"] == "Processor ready"


@pytest.mark.asyncio
async def test_health_check_includes_active_embedding_configuration(
    monkeypatch: pytest.MonkeyPatch,
):
    main = _load_main(monkeypatch)

    monkeypatch.setattr(
        main,
        "embedder",
        _FakeEmbedder(
            "connected",
            endpoint="http://localhost:1234/v1",
            model="text-embedding-qwen",
        ),
    )
    monkeypatch.setattr(main, "blob_writer", _FakeBlobWriter(True))
    monkeypatch.setattr(main, "api_client", _FakeApiClient(True))
    monkeypatch.setattr(main, "shutdown_requested", False)
    monkeypatch.setattr(
        main,
        "describe_chunker_tokenizer",
        lambda: {"tokenizer_status": "configured", "tokenizer_model": "m", "tokenizer_source": "s", "tokenizer_path": None},
    )

    async def _count_active_jobs() -> int:
        return 0

    monkeypatch.setattr(main, "_count_active_jobs", _count_active_jobs)

    response = await main.health_check()
    payload = json.loads(response.body)

    assert response.status_code == 200
    assert payload["services"]["embedding_endpoint"] == "http://localhost:1234/v1"
    assert payload["services"]["embedding_model"] == "text-embedding-qwen"


@pytest.mark.asyncio
async def test_health_check_returns_degraded_when_tokenizer_missing(
    monkeypatch: pytest.MonkeyPatch,
):
    """200/degraded is returned when the tokenizer cannot be resolved.

    CSV and bike-graph operators should not be blocked by a missing PDF tokenizer.
    """
    main = _load_main(monkeypatch)

    monkeypatch.setattr(main, "embedder", _FakeEmbedder("connected"))
    monkeypatch.setattr(main, "blob_writer", _FakeBlobWriter(True))
    monkeypatch.setattr(main, "api_client", _FakeApiClient(True))
    monkeypatch.setattr(main, "shutdown_requested", False)
    monkeypatch.setattr(
        main,
        "describe_chunker_tokenizer",
        lambda: {
            "tokenizer_status": "missing",
            "tokenizer_model": None,
            "tokenizer_source": None,
            "tokenizer_path": None,
            "tokenizer_error": "No tokenizer model is configured.",
        },
    )

    async def _count_active_jobs() -> int:
        return 0

    monkeypatch.setattr(main, "_count_active_jobs", _count_active_jobs)

    response = await main.health_check()
    payload = json.loads(response.body)

    assert response.status_code == 200
    assert payload["status"] == "degraded"
    assert payload["accepting_work"] is True
    assert "Tokenizer is not configured" in payload["message"]


@pytest.mark.asyncio
async def test_health_check_includes_tokenizer_metadata_in_services(
    monkeypatch: pytest.MonkeyPatch,
):
    """The tokenizer resolution fields should be present in services when healthy."""
    main = _load_main(monkeypatch)

    monkeypatch.setattr(main, "embedder", _FakeEmbedder("connected"))
    monkeypatch.setattr(main, "blob_writer", _FakeBlobWriter(True))
    monkeypatch.setattr(main, "api_client", _FakeApiClient(True))
    monkeypatch.setattr(main, "shutdown_requested", False)
    monkeypatch.setattr(
        main,
        "describe_chunker_tokenizer",
        lambda: {
            "tokenizer_status": "configured",
            "tokenizer_model": "/home/user/.lmstudio/models/Qwen3-Embedding",
            "tokenizer_source": "LM_STUDIO_MODELS_DIR",
            "tokenizer_path": "/home/user/.lmstudio/models/Qwen3-Embedding",
        },
    )

    async def _count_active_jobs() -> int:
        return 0

    monkeypatch.setattr(main, "_count_active_jobs", _count_active_jobs)

    response = await main.health_check()
    payload = json.loads(response.body)

    assert response.status_code == 200
    assert payload["services"]["tokenizer_status"] == "configured"
    assert payload["services"]["tokenizer_source"] == "LM_STUDIO_MODELS_DIR"
    assert "Qwen3-Embedding" in payload["services"]["tokenizer_model"]
