"""Path validation helpers for local processor request inputs."""

import os
from pathlib import Path

from fastapi import HTTPException

LOCAL_PROCESSOR_INPUT_DIR_ENV = "LOCAL_PROCESSOR_INPUT_DIR"


def _resolve_local_path(local_file_path: str, allowed_suffix: str) -> Path:
    """Resolve a caller-supplied local path under the configured input root."""
    input_root_value = os.getenv(LOCAL_PROCESSOR_INPUT_DIR_ENV)
    if not input_root_value:
        raise HTTPException(
            status_code=400,
            detail=(
                "local_file_path is disabled. Configure "
                f"{LOCAL_PROCESSOR_INPUT_DIR_ENV} to enable local file processing."
            ),
        )

    try:
        # codeql[py/path-injection]: input_root itself is operator config (env var), not
        # caller-supplied; the actual caller-supplied value (local_file_path, below) is
        # contained by the resolve()+relative_to() check at the bottom of this function.
        input_root = Path(input_root_value).expanduser().resolve(strict=True)
    except (OSError, RuntimeError) as exc:
        raise HTTPException(
            status_code=500,
            detail=(
                f"{LOCAL_PROCESSOR_INPUT_DIR_ENV} does not point to an existing "
                "directory"
            ),
        ) from exc

    if not input_root.is_dir():
        raise HTTPException(
            status_code=500,
            detail=f"{LOCAL_PROCESSOR_INPUT_DIR_ENV} does not point to an existing directory",
        )

    candidate_path = Path(local_file_path).expanduser()  # codeql[py/path-injection]: canonicalized then containment-checked against input_root below via relative_to()
    try:
        resolved_path = candidate_path.resolve(strict=True)
    except FileNotFoundError as exc:
        if candidate_path.is_symlink():
            raise HTTPException(
                status_code=400,
                detail="local_file_path contains invalid path components",
            ) from exc

        raise HTTPException(
            status_code=400,
            detail="local_file_path does not point to an existing file",
        ) from exc
    except (OSError, RuntimeError) as exc:
        raise HTTPException(
            status_code=400,
            detail="local_file_path contains invalid path components",
        ) from exc

    # Verify the resolved path is inside the input root (prevents symlink traversal)
    try:
        resolved_path.relative_to(input_root)
    except ValueError as exc:
        raise HTTPException(
            status_code=400,
            detail="local_file_path must be inside the configured input directory",
        ) from exc

    if not resolved_path.is_file():
        raise HTTPException(
            status_code=400,
            detail="local_file_path does not point to an existing file",
        )

    if resolved_path.suffix.lower() != allowed_suffix:
        raise HTTPException(
            status_code=400,
            detail=f"Only {allowed_suffix} files are supported",
        )

    return resolved_path


def resolve_local_csv_path(local_file_path: str) -> Path:
    """Resolve a caller-supplied CSV path under the configured local input root."""
    return _resolve_local_path(local_file_path, ".csv")


def resolve_local_pdf_path(local_file_path: str) -> Path:
    """Resolve a caller-supplied PDF path under the configured local input root."""
    return _resolve_local_path(local_file_path, ".pdf")
