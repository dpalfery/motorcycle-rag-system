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
        # input_root is operator config (env var), not caller-supplied.
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

    root_str = os.path.realpath(str(input_root))

    # ------------------------------------------------------------------
    # Containment barrier.
    #
    # This operates on plain strings and must dominate every filesystem access
    # derived from local_file_path. Constructing a Path from caller input is
    # itself the path-injection sink, so a containment check placed after that
    # construction guards nothing -- the traversal has already been expressed.
    # Canonicalize first, then compare on path-component boundaries so that a
    # sibling like "<root>-escape" cannot masquerade as "<root>".
    # ------------------------------------------------------------------
    try:
        expanded = os.path.expanduser(local_file_path)
        joined = expanded if os.path.isabs(expanded) else os.path.join(root_str, expanded)
        candidate_str = os.path.realpath(joined)
        # A trailing symlink is the only way canonicalization can diverge from
        # the parent-resolved path. Capturing that here keeps the "invalid path
        # components" contract for broken links while remaining correct when an
        # ancestor directory is itself a symlink (e.g. /tmp on macOS).
        lexical_str = os.path.join(
            os.path.realpath(os.path.dirname(joined)), os.path.basename(joined)
        )
    except (OSError, RuntimeError, ValueError) as exc:
        raise HTTPException(
            status_code=400,
            detail="local_file_path contains invalid path components",
        ) from exc

    # Single startswith guard against "<root>/" -- the trailing separator makes
    # the comparison component-wise, so "<root>-escape" cannot pass. The input
    # root itself is not a valid target (it is a directory, never a file).
    if not candidate_str.startswith(root_str + os.sep):
        raise HTTPException(
            status_code=400,
            detail="local_file_path must be inside the configured input directory",
        )

    # candidate_str is now canonical and proven to be inside the input root.
    resolved_path = Path(candidate_str)

    if not os.path.exists(candidate_str):
        if candidate_str != lexical_str:
            raise HTTPException(
                status_code=400,
                detail="local_file_path contains invalid path components",
            )
        raise HTTPException(
            status_code=400,
            detail="local_file_path does not point to an existing file",
        )

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
