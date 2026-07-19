"""Path validation helpers for local processor request inputs."""

import os
from pathlib import Path

from fastapi import HTTPException

LOCAL_PROCESSOR_INPUT_DIR_ENV = "LOCAL_PROCESSOR_INPUT_DIR"


def _resolve_local_path(local_file_path: str, allowed_suffix: str) -> Path:
    """Resolve a caller-supplied local path under the configured input root.

    Absolute paths are kept absolute (Admin Desktop contract / D3). Relative
    paths are joined under ``LOCAL_PROCESSOR_INPUT_DIR`` before canonicalization.
    Resolved paths must remain inside the input root after symlink resolution.
    """
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
        # codeql[py/path-injection]: input_root itself is operator config (env var),
        # not caller-supplied. Caller-supplied paths are joined/canonicalized and
        # containment-checked via commonpath + relative_to below.
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

    user_path = Path(local_file_path).expanduser()
    # Relative → join under input_root before resolve (CodeQL join-under-root).
    # Absolute → keep absolute (D3); still containment-checked after resolve.
    if user_path.is_absolute():
        candidate_path = user_path
    else:
        candidate_path = input_root / user_path

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

    # CodeQL-visible containment barrier (os.path.commonpath).
    input_root_str = os.path.realpath(str(input_root))
    resolved_str = os.path.realpath(str(resolved_path))
    try:
        if os.path.commonpath([input_root_str, resolved_str]) != input_root_str:
            raise ValueError("path escapes input root")
    except ValueError as exc:
        raise HTTPException(
            status_code=400,
            detail="local_file_path must be inside the configured input directory",
        ) from exc

    # Secondary pathlib component-boundary check (symlink / prefix-collision).
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
