"""Path validation helpers for local processor request inputs."""

import os
from pathlib import Path

from fastapi import HTTPException

LOCAL_PROCESSOR_INPUT_DIR_ENV = "LOCAL_PROCESSOR_INPUT_DIR"


def resolve_local_csv_path(local_file_path: str) -> Path:
    """Resolve a caller-supplied CSV path under the configured local input root."""
    input_root_value = os.getenv(LOCAL_PROCESSOR_INPUT_DIR_ENV)
    if not input_root_value:
        raise HTTPException(
            status_code=400,
            detail=(
                "local_file_path is disabled. Configure "
                f"{LOCAL_PROCESSOR_INPUT_DIR_ENV} to enable local file processing."
            ),
        )

    input_root = Path(input_root_value).expanduser().resolve(strict=False)
    if not input_root.is_dir():
        raise HTTPException(
            status_code=500,
            detail=f"{LOCAL_PROCESSOR_INPUT_DIR_ENV} does not point to an existing directory",
        )

    try:
        resolved_path = Path(local_file_path).expanduser().resolve(strict=False)
        resolved_path.relative_to(input_root)
    except (OSError, RuntimeError, ValueError) as exc:
        raise HTTPException(
            status_code=400,
            detail="local_file_path must be inside the configured input directory",
        ) from exc

    if not resolved_path.is_file():
        raise HTTPException(
            status_code=400,
            detail="local_file_path does not point to an existing file",
        )

    if resolved_path.suffix.lower() != ".csv":
        raise HTTPException(status_code=400, detail="Only .csv files are supported")

    return resolved_path
