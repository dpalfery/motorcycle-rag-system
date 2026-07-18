"""Authentication contracts for the local processor's control surface."""

import ast
import importlib
import secrets
import sys
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock

import httpx
import pytest

CONTROL_TOKEN_ENV = "MCR_LOCAL_PROCESSOR_CONTROL_TOKEN"


def _load_main(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    *,
    control_token: str | None,
):
    monkeypatch.setenv("WATCH_FOLDER_DISABLED", "1")
    monkeypatch.setenv("LOCAL_PROCESSOR_INPUT_DIR", str(tmp_path))
    monkeypatch.setenv("EMBEDDING_PROVIDER_ENDPOINT", "")
    monkeypatch.setenv("EMBEDDING_BACKEND", "ollama")
    monkeypatch.setenv("MCR_API_BASE_URL", "https://api.example.test")
    monkeypatch.setenv("PYTHON_UPLOAD_JOB_SECRET", "")
    monkeypatch.setenv(
        "AZURE_STORAGE_CONNECTION_STRING",
        "UseDevelopmentStorage=true",
    )
    monkeypatch.delenv("AZURE_STORAGE_ACCOUNT_URL", raising=False)
    if control_token is None:
        monkeypatch.delenv(CONTROL_TOKEN_ENV, raising=False)
    else:
        monkeypatch.setenv(CONTROL_TOKEN_ENV, control_token)

    sys.modules.pop("main", None)
    sys.modules.pop("security.local_control_auth", None)
    main = importlib.import_module("main")

    processor = MagicMock()
    processor.list_jobs = AsyncMock(return_value=[])
    processor.get_job_status = AsyncMock(return_value=None)
    processor.stop_job = AsyncMock(return_value=None)
    processor.clear_terminal_jobs = AsyncMock(return_value=0)
    monkeypatch.setattr(main, "pdf_processor", processor)
    monkeypatch.setattr(main, "csv_processor", processor)
    monkeypatch.setattr(main, "bike_graph_processor", processor)
    monkeypatch.setattr(
        main,
        "embedder",
        SimpleNamespace(check_status=AsyncMock(return_value="connected")),
    )
    monkeypatch.setattr(main, "blob_writer", SimpleNamespace(is_connected=lambda: True))
    monkeypatch.setattr(main, "api_client", SimpleNamespace(is_configured=lambda: True))
    monkeypatch.setattr(
        main,
        "describe_chunker_tokenizer",
        lambda: {"tokenizer_status": "configured"},
    )
    monkeypatch.setattr(
        main,
        "discover_embedding_models",
        AsyncMock(
            return_value=SimpleNamespace(
                provider="test",
                endpoint="http://127.0.0.1:1234/v1",
                models=[],
            )
        ),
    )

    async def do_not_shutdown() -> None:
        return None

    monkeypatch.setattr(main, "_wait_for_graceful_shutdown", do_not_shutdown)
    return main


async def _request(
    app,
    method: str,
    path: str,
    *,
    authorization: str | None = None,
) -> httpx.Response:
    headers = {} if authorization is None else {"Authorization": authorization}
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(
        transport=transport, base_url="http://testserver"
    ) as client:
        return await client.request(method, path, headers=headers)


CONTROL_ROUTES = [
    ("GET", "/health"),
    ("GET", "/embedding/models?endpoint=http%3A%2F%2F127.0.0.1%3A1234%2Fv1"),
    ("POST", "/process/pdf"),
    ("POST", "/process/csv"),
    ("POST", "/process/bike-graph"),
    ("GET", "/jobs"),
    ("GET", "/jobs/unknown-job"),
    ("POST", "/jobs/unknown-job/stop"),
    ("DELETE", "/jobs"),
    ("POST", "/jobs/cleanup"),
    ("POST", "/control/shutdown"),
]


@pytest.mark.asyncio
@pytest.mark.parametrize(("method", "path"), CONTROL_ROUTES)
async def test_local_control_routes_reject_missing_bearer_token(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    method: str,
    path: str,
):
    main = _load_main(monkeypatch, tmp_path, control_token=secrets.token_urlsafe(32))

    response = await _request(main.app, method, path)

    assert response.status_code == 401


@pytest.mark.asyncio
@pytest.mark.parametrize(("method", "path"), CONTROL_ROUTES)
async def test_local_control_routes_reject_invalid_bearer_token(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
    method: str,
    path: str,
):
    main = _load_main(monkeypatch, tmp_path, control_token=secrets.token_urlsafe(32))

    response = await _request(
        main.app,
        method,
        path,
        authorization=f"Bearer {secrets.token_urlsafe(32)}",
    )

    assert response.status_code == 401


@pytest.mark.asyncio
async def test_local_control_health_accepts_configured_bearer_token(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    control_token = secrets.token_urlsafe(32)
    main = _load_main(monkeypatch, tmp_path, control_token=control_token)

    response = await _request(
        main.app,
        "GET",
        "/health",
        authorization=f"Bearer {control_token}",
    )

    assert response.status_code in {200, 503}


@pytest.mark.asyncio
async def test_local_control_routes_fail_closed_when_runtime_token_is_missing(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main(monkeypatch, tmp_path, control_token=None)

    response = await _request(
        main.app,
        "GET",
        "/health",
        authorization=f"Bearer {secrets.token_urlsafe(32)}",
    )

    assert response.status_code == 503


@pytest.mark.asyncio
async def test_local_control_auth_uses_constant_time_digest_comparison(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    control_token = secrets.token_urlsafe(32)
    main = _load_main(monkeypatch, tmp_path, control_token=control_token)
    local_control_auth = importlib.import_module("security.local_control_auth")
    original_compare_digest = local_control_auth.secrets.compare_digest
    comparison_calls: list[tuple[str, str]] = []

    def capture_compare_digest(provided: str, expected: str) -> bool:
        comparison_calls.append((provided, expected))
        return original_compare_digest(provided, expected)

    monkeypatch.setattr(
        local_control_auth.secrets,
        "compare_digest",
        capture_compare_digest,
    )

    response = await _request(
        main.app,
        "GET",
        "/health",
        authorization=f"Bearer {control_token}",
    )

    assert response.status_code in {200, 503}
    assert comparison_calls == [(control_token, control_token)]


def test_direct_processor_entry_binds_only_to_loopback(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
):
    main = _load_main(monkeypatch, tmp_path, control_token=secrets.token_urlsafe(32))
    source_tree = ast.parse(Path(main.__file__).read_text(encoding="utf-8"))
    uvicorn_config_calls = [
        node
        for node in ast.walk(source_tree)
        if isinstance(node, ast.Call)
        and isinstance(node.func, ast.Attribute)
        and isinstance(node.func.value, ast.Name)
        and node.func.value.id == "uvicorn"
        and node.func.attr == "Config"
    ]

    assert len(uvicorn_config_calls) == 1
    host = next(
        keyword.value.value
        for keyword in uvicorn_config_calls[0].keywords
        if keyword.arg == "host" and isinstance(keyword.value, ast.Constant)
    )
    assert host == "127.0.0.1"
