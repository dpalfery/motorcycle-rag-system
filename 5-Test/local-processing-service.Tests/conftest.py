"""Shared pytest configuration for the local-processing-service tests."""

import os

import pytest


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
