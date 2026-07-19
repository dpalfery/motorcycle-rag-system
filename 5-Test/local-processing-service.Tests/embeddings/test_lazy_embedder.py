"""Unit tests for LazyEmbedder in isolation from embedder_factory."""

import logging
import threading
import time

import pytest

from embeddings.embedder import Embedder
from embeddings.lazy_embedder import LazyEmbedder


class _FakeEmbedder(Embedder):
    def __init__(self, *, embedding=None):
        self._embedding = embedding or [0.1, 0.2]
        self.generate_embedding_calls: list[str] = []
        self.generate_embeddings_batch_calls: list[list[str]] = []

    async def generate_embedding(self, text: str) -> list[float]:
        self.generate_embedding_calls.append(text)
        return self._embedding

    async def generate_embeddings_batch(self, texts: list[str]) -> list[list[float]]:
        self.generate_embeddings_batch_calls.append(texts)
        return [self._embedding for _ in texts]

    async def check_status(self) -> str:
        return "connected"


def test_construction_does_not_invoke_factory():
    calls = []

    def factory():
        calls.append(1)
        return _FakeEmbedder()

    LazyEmbedder(factory, endpoint="http://host:1234", model="a-model")

    assert calls == []


def test_embedder_property_returns_self_before_resolve():
    lazy = LazyEmbedder(
        lambda: _FakeEmbedder(), endpoint="http://host", host="h", model="m"
    )

    assert lazy._embedder is lazy
    assert lazy._endpoint == "http://host"
    assert lazy._host == "h"
    assert lazy._model == "m"


def test_embedder_property_unwraps_resolved_concrete_provider():
    class _Wrapper:
        def __init__(self, inner):
            self._embedder = inner

    inner = _FakeEmbedder()
    lazy = LazyEmbedder(lambda: _Wrapper(inner))

    resolved = lazy._try_resolve()

    assert resolved is not None
    assert lazy._embedder is inner


def test_try_resolve_caches_successful_result_without_recalling_factory():
    calls = []

    def factory():
        calls.append(1)
        return _FakeEmbedder()

    lazy = LazyEmbedder(factory)

    first = lazy._try_resolve()
    second = lazy._try_resolve()

    assert first is second
    assert calls == [1]


def test_try_resolve_propagates_config_attrs_from_base_url_only_provider():
    class _BaseUrlOnlyEmbedder(_FakeEmbedder):
        def __init__(self):
            super().__init__()
            self._base_url = "http://discovered:9999"

    lazy = LazyEmbedder(_BaseUrlOnlyEmbedder)

    lazy._try_resolve()

    assert lazy._base_url == "http://discovered:9999"


def test_try_resolve_returns_none_and_does_not_raise_on_factory_failure():
    def factory():
        raise ConnectionError("provider unreachable")

    lazy = LazyEmbedder(factory)

    result = lazy._try_resolve()

    assert result is None
    assert isinstance(lazy._init_error, ConnectionError)


def test_try_resolve_logs_warning_once_for_repeated_identical_failure(caplog):
    def factory():
        raise ConnectionError("same failure")

    lazy = LazyEmbedder(factory)
    caplog.set_level(logging.WARNING, logger="embeddings.lazy_embedder")

    lazy._try_resolve()
    lazy._try_resolve()

    warnings = [r for r in caplog.records if r.levelname == "WARNING"]
    assert len(warnings) == 1


def test_try_resolve_logs_warning_again_when_failure_changes(caplog):
    calls = {"count": 0}

    def factory():
        calls["count"] += 1
        raise ConnectionError(f"failure {calls['count']}")

    lazy = LazyEmbedder(factory)
    caplog.set_level(logging.WARNING, logger="embeddings.lazy_embedder")

    lazy._try_resolve()
    lazy._try_resolve()

    warnings = [r for r in caplog.records if r.levelname == "WARNING"]
    assert len(warnings) == 2


def test_try_resolve_returns_result_found_inside_lock_without_calling_factory():
    """Covers the double-checked-locking re-check: another thread resolves
    the embedder between this thread's unlocked check and its lock
    acquisition, so the factory must not be invoked again."""
    calls = []

    def factory():
        calls.append(1)
        return _FakeEmbedder()

    lazy = LazyEmbedder(factory)
    sentinel = _FakeEmbedder()

    lazy._lock.acquire()
    result_holder = {}

    def worker():
        result_holder["result"] = lazy._try_resolve()

    thread = threading.Thread(target=worker)
    thread.start()
    time.sleep(0.05)
    lazy._resolved = sentinel
    lazy._lock.release()
    thread.join(timeout=5)

    assert result_holder["result"] is sentinel
    assert calls == []


def test_require_resolved_returns_resolved_embedder_on_success():
    fake = _FakeEmbedder()
    lazy = LazyEmbedder(lambda: fake)

    resolved = lazy._require_resolved()

    assert resolved is fake


def test_require_resolved_raises_runtime_error_with_detail_on_failure():
    lazy = LazyEmbedder(lambda: (_ for _ in ()).throw(ConnectionError("down")))

    with pytest.raises(RuntimeError, match="failed to initialise"):
        lazy._require_resolved()


async def test_generate_embedding_delegates_to_resolved_provider():
    fake = _FakeEmbedder(embedding=[1.0, 2.0])
    lazy = LazyEmbedder(lambda: fake)

    result = await lazy.generate_embedding("hello")

    assert result == [1.0, 2.0]
    assert fake.generate_embedding_calls == ["hello"]


async def test_generate_embedding_raises_when_resolution_fails():
    lazy = LazyEmbedder(lambda: (_ for _ in ()).throw(ConnectionError("down")))

    with pytest.raises(RuntimeError, match="failed to initialise"):
        await lazy.generate_embedding("hello")


async def test_generate_embeddings_batch_delegates_to_resolved_provider():
    fake = _FakeEmbedder(embedding=[3.0])
    lazy = LazyEmbedder(lambda: fake)

    result = await lazy.generate_embeddings_batch(["a", "b"])

    assert result == [[3.0], [3.0]]
    assert fake.generate_embeddings_batch_calls == [["a", "b"]]


async def test_generate_embeddings_batch_raises_when_resolution_fails():
    lazy = LazyEmbedder(lambda: (_ for _ in ()).throw(ConnectionError("down")))

    with pytest.raises(RuntimeError, match="failed to initialise"):
        await lazy.generate_embeddings_batch(["a"])


async def test_check_status_returns_disconnected_when_resolution_fails():
    lazy = LazyEmbedder(lambda: (_ for _ in ()).throw(ConnectionError("down")))

    status = await lazy.check_status()

    assert status == "disconnected"


async def test_check_status_delegates_to_resolved_provider():
    fake = _FakeEmbedder()
    lazy = LazyEmbedder(lambda: fake)

    status = await lazy.check_status()

    assert status == "connected"
