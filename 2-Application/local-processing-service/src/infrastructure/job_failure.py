"""Format job failure payloads with full tracebacks for local admin diagnostics."""

from __future__ import annotations

import traceback
from typing import Any


def format_failure(exc: BaseException) -> dict[str, Any]:
    """Return status fields with a full traceback in ``error``."""
    return {
        "status": "failed",
        "error": traceback.format_exc(),
        "message": f"{type(exc).__name__}: {exc}",
    }
