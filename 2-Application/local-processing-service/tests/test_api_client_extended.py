"""Unit tests for extended run and stage API reporting."""

from unittest.mock import MagicMock, patch

import httpx
import pytest

from api.api_client_extended import ApiClientExtended


class _Response:
    def __init__(self, status_code=200, body=None, text=""):
        self.status_code = status_code
        self._body = body or {"id": "result"}
        self.text = text

    def json(self):
        return self._body


class _AsyncClient:
    def __init__(self, response=None, error=None):
        self.response = response or _Response()
        self.error = error
        self.calls = []

    async def __aenter__(self):
        return self

    async def __aexit__(self, exc_type, exc, tb):
        return False

    async def post(self, url, **kwargs):
        self.calls.append(("post", url, kwargs))
        if self.error:
            raise self.error
        return self.response

    async def patch(self, url, **kwargs):
        self.calls.append(("patch", url, kwargs))
        if self.error:
            raise self.error
        return self.response


@pytest.fixture
def base_client():
    client = MagicMock()
    client.is_configured.return_value = True
    client._base_url = "https://api.example"
    client._get_token.return_value = "token"
    return client


def _patch_http(fake):
    return patch("api.api_client_extended.httpx.AsyncClient", return_value=fake)


def test_is_configured_delegates_to_base_client(base_client):
    base_client.is_configured.return_value = False

    assert ApiClientExtended(base_client).is_configured is False
    base_client.is_configured.assert_called_once()


async def test_create_manual_run_posts_expected_contract(base_client):
    fake = _AsyncClient(_Response(body={"run_id": "run-1"}))

    with _patch_http(fake):
        result = await ApiClientExtended(base_client).create_manual_run(
            "doc-1", "graph-seeding", "05-graph-candidates"
        )

    assert result == {"run_id": "run-1"}
    method, url, kwargs = fake.calls[0]
    assert (method, url) == ("post", "https://api.example/api/manual-runs")
    assert kwargs["headers"] == {"Authorization": "Bearer token"}
    assert kwargs["json"] == {
        "documentId": "doc-1",
        "runType": "graph-seeding",
        "startedFromStage": "05-graph-candidates",
    }


async def test_update_run_status_includes_only_provided_optional_fields(base_client):
    fake = _AsyncClient()

    with _patch_http(fake):
        result = await ApiClientExtended(base_client).update_run_status(
            "run-1", "failed", completed_stage="two", error_summary="boom"
        )

    assert result is None
    assert fake.calls[0][0:2] == (
        "patch",
        "https://api.example/api/manual-runs/run-1",
    )
    assert fake.calls[0][2]["json"] == {
        "status": "failed",
        "completedStage": "two",
        "errorSummary": "boom",
    }


async def test_start_stage_posts_and_returns_response(base_client):
    fake = _AsyncClient(_Response(body={"stage_id": "stage-1"}))

    with _patch_http(fake):
        result = await ApiClientExtended(base_client).start_stage("run-1", "one")

    assert result == {"stage_id": "stage-1"}
    assert fake.calls[0][1].endswith("/api/manual-runs/run-1/stages/one/start")
    assert "json" not in fake.calls[0][2]


async def test_complete_stage_builds_full_payload(base_client):
    fake = _AsyncClient(_Response(body={"stage_id": "stage-1"}))

    with _patch_http(fake):
        result = await ApiClientExtended(base_client).complete_stage(
            "run-1",
            "one",
            artifact_path="/tmp/a",
            artifact_hash="abc",
            metadata={"count": 2},
        )

    assert result == {"stage_id": "stage-1"}
    assert fake.calls[0][2]["json"] == {
        "artifactPath": "/tmp/a",
        "artifactHash": "abc",
        "metadata": {"count": 2},
    }


async def test_fail_stage_posts_error_detail(base_client):
    fake = _AsyncClient(_Response(body={"stage_id": "stage-1"}))

    with _patch_http(fake):
        result = await ApiClientExtended(base_client).fail_stage(
            "run-1", "one", "bad data"
        )

    assert result == {"stage_id": "stage-1"}
    assert fake.calls[0][2]["json"] == {"errorDetail": "bad data"}


async def test_register_artifacts_builds_full_payload(base_client):
    fake = _AsyncClient(_Response(body={"artifact_id": "artifact-1"}))

    with _patch_http(fake):
        result = await ApiClientExtended(base_client).register_artifacts(
            "run-1",
            chunk_count=0,
            vector_count=2,
            graph_entity_count=3,
            graph_relation_count=4,
            metadata={"source": "test"},
        )

    assert result == {"artifact_id": "artifact-1"}
    assert fake.calls[0][2]["json"] == {
        "chunkCount": 0,
        "vectorCount": 2,
        "graphEntityCount": 3,
        "graphRelationCount": 4,
        "metadata": {"source": "test"},
    }


@pytest.mark.parametrize(
    ("method", "args", "expected"),
    [
        ("create_manual_run", ("doc",), {"run_id": None}),
        ("update_run_status", ("run", "running"), None),
        ("start_stage", ("run", "one"), {"stage_id": None}),
        ("complete_stage", ("run", "one"), {"stage_id": None}),
        ("fail_stage", ("run", "one", "boom"), {"stage_id": None}),
        ("register_artifacts", ("run",), {"artifact_id": None}),
    ],
)
async def test_methods_skip_network_when_unconfigured(
    base_client, method, args, expected
):
    base_client.is_configured.return_value = False

    with patch("api.api_client_extended.httpx.AsyncClient") as constructor:
        result = await getattr(ApiClientExtended(base_client), method)(*args)

    assert result == expected
    constructor.assert_not_called()


@pytest.mark.parametrize(
    ("method", "args", "message"),
    [
        ("create_manual_run", ("doc",), "Failed to create run: denied"),
        (
            "update_run_status",
            ("run", "failed"),
            "Failed to update run status: denied",
        ),
        ("start_stage", ("run", "one"), "Failed to start stage: denied"),
        ("complete_stage", ("run", "one"), "Failed to complete stage: denied"),
        ("fail_stage", ("run", "one", "boom"), "Failed to fail stage: denied"),
        (
            "register_artifacts",
            ("run",),
            "Failed to register artifacts: denied",
        ),
    ],
)
async def test_methods_translate_error_responses(base_client, method, args, message):
    fake = _AsyncClient(_Response(status_code=400, text="denied"))

    with _patch_http(fake):
        with pytest.raises(RuntimeError, match=message):
            await getattr(ApiClientExtended(base_client), method)(*args)


@pytest.mark.parametrize(
    ("method", "args", "message"),
    [
        ("create_manual_run", ("doc",), "HTTP error creating run"),
        ("update_run_status", ("run", "failed"), "HTTP error updating run status"),
        ("start_stage", ("run", "one"), "HTTP error starting stage"),
        ("complete_stage", ("run", "one"), "HTTP error completing stage"),
        ("fail_stage", ("run", "one", "boom"), "HTTP error failing stage"),
        ("register_artifacts", ("run",), "HTTP error registering artifacts"),
    ],
)
async def test_methods_translate_http_transport_errors(
    base_client, method, args, message
):
    error = httpx.ConnectError(
        "offline", request=httpx.Request("POST", "https://api.example")
    )

    with _patch_http(_AsyncClient(error=error)):
        with pytest.raises(RuntimeError, match=message):
            await getattr(ApiClientExtended(base_client), method)(*args)
