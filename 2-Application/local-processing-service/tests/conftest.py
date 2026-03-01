"""Shared pytest configuration for the local-processing-service tests."""

import pytest


# Enable auto mode so @pytest.mark.asyncio is applied to all async test functions.
def pytest_configure(config):
    """Register custom markers and set asyncio_mode."""
    config.addinivalue_line("markers", "asyncio: mark test as async")


# Use auto mode for pytest-asyncio so every async def test_* is detected.
pytest_plugins = []
