import importlib
import json
import sys

import pytest


def _load_main(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.delenv("EMBEDDING_PROVIDER_ENDPOINT", raising=False)
    monkeypatch.setenv("EMBEDDING_BACKEND", "ollama")
    monkeypatch.delenv("DEEPINFRA_API_KEY", raising=False)
    sys.modules.pop("main", None)
    return importlib.import_module("main")


class _FakeEmbedder:
    def __init__(self, status: str):
        self._status = status

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
async def test_health_check_returns_healthy_when_embedding_provider_connected(
    monkeypatch: pytest.MonkeyPatch,
):
    main = _load_main(monkeypatch)

    monkeypatch.setattr(main, "embedder", _FakeEmbedder("connected"))
    monkeypatch.setattr(main, "blob_writer", _FakeBlobWriter(True))
    monkeypatch.setattr(main, "api_client", _FakeApiClient(True))
    monkeypatch.setattr(main, "shutdown_requested", False)

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