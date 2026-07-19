"""Shared pytest configuration for the local-processing-service tests."""

import os
import sys
from pathlib import Path

import pytest


# Make the service source tree importable when pytest is launched from this
# folder (e.g. VS Code Test Explorer right-click "Run Tests" on this folder).
# The canonical CI / coverage command continues to use
# `pytest -c pyproject.toml --rootdir=.` from `2-Application/local-processing-service/`
# so it inherits `pythonpath = ["src"]` from `pyproject.toml`; this hook covers
# the standalone case so the Test Explorer and any in-folder invocation work
# without changing the source-of-truth config.
_SERVICE_SRC = (
    Path(__file__).resolve().parent.parent.parent
    / "2-Application"
    / "local-processing-service"
    / "src"
)
if _SERVICE_SRC.is_dir():
    src_str = str(_SERVICE_SRC)
    if src_str not in sys.path:
        sys.path.insert(0, src_str)


# Enable auto mode so @pytest.mark.asyncio is applied to all async test functions.
def pytest_configure(config):
    """Register custom markers and set asyncio_mode."""
    config.addinivalue_line("markers", "asyncio: mark test as async")


@pytest.fixture(autouse=True)
def _default_extraction_env() -> None:
    """Set default extraction env vars so tests can create extractors.

    Individual tests that need to test missing-env-var behaviour should
    ``monkeypatch.delenv()`` the relevant variable(s).
    """
    os.environ.setdefault("GRAPH_EXTRACTION_ENDPOINT", "http://localhost:9999/v1")
    os.environ.setdefault("GRAPH_EXTRACTION_MODEL", "test-model")
    # ApiClient validates MCR_API_BASE_URL at construction time and rejects
    # loopback hosts. Keep a public HTTPS default so importing main.py does not
    # fail when a developer .env points at https://localhost.
    os.environ.setdefault("MCR_API_BASE_URL", "https://api.example.test")
