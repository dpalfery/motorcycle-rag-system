"""Path containment contract for local processor ``local_file_path`` resolution.

Contract (plan D3 / T1→T2):
- Absolute paths under ``LOCAL_PROCESSOR_INPUT_DIR`` remain allowed (not basename-only).
- Relative paths are resolved by joining under the configured input root, then
  canonicalized and containment-checked.
- Traversal, symlink escape, and prefix-collision attacks must be rejected.
- T2 must make containment CodeQL-visible (e.g. ``os.path.commonpath`` / join-under-root
  barrier) without weakening the behaviors asserted here.

Red tests below that require join-under-root relative resolution are expected to fail
until T2 updates ``security.path_validation._resolve_local_path``.
"""

from pathlib import Path

import pytest
from fastapi import HTTPException

from security.path_validation import (
    LOCAL_PROCESSOR_INPUT_DIR_ENV,
    resolve_local_csv_path,
    resolve_local_pdf_path,
)


def test_resolve_local_csv_path_allows_absolute_file_under_configured_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    csv_path = input_dir / "source.csv"
    csv_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    assert resolve_local_csv_path(str(csv_path)) == csv_path.resolve()


def test_resolve_local_csv_path_allows_nested_absolute_file_under_configured_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    nested_dir = input_dir / "batch" / "run-1"
    nested_dir.mkdir(parents=True)
    csv_path = nested_dir / "source.csv"
    csv_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    assert resolve_local_csv_path(str(csv_path)) == csv_path.resolve()


def test_resolve_local_csv_path_allows_relative_file_joined_under_configured_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    """T2: relative segments must join under input root (not CWD). Expected red until T2."""
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    csv_path = input_dir / "source.csv"
    csv_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))
    monkeypatch.chdir(tmp_path)

    assert resolve_local_csv_path("source.csv") == csv_path.resolve()


def test_resolve_local_csv_path_allows_nested_relative_file_joined_under_configured_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    """T2: nested relative paths join under input root. Expected red until T2."""
    input_dir = tmp_path / "inputs"
    nested_dir = input_dir / "batch" / "run-1"
    nested_dir.mkdir(parents=True)
    csv_path = nested_dir / "source.csv"
    csv_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))
    monkeypatch.chdir(tmp_path)

    assert resolve_local_csv_path("batch/run-1/source.csv") == csv_path.resolve()


def test_resolve_local_csv_path_rejects_when_input_dir_not_configured(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    csv_path = tmp_path / "source.csv"
    csv_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    monkeypatch.delenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, raising=False)

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path(str(csv_path))

    assert exc_info.value.status_code == 400
    assert "disabled" in exc_info.value.detail


def test_resolve_local_csv_path_rejects_when_configured_input_dir_is_not_a_directory(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    configured_file = tmp_path / "not-a-directory"
    configured_file.write_text("not a directory", encoding="utf-8")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(configured_file))

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path(str(tmp_path / "source.csv"))

    assert exc_info.value.status_code == 500
    assert "does not point to an existing directory" in exc_info.value.detail


def test_resolve_local_csv_path_rejects_file_outside_configured_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    outside_path = tmp_path / "source.csv"
    outside_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path(str(outside_path))

    assert exc_info.value.status_code == 400
    assert "inside the configured input directory" in exc_info.value.detail


def test_resolve_local_csv_path_rejects_missing_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path(str(input_dir / "missing.csv"))

    assert exc_info.value.status_code == 400
    assert "existing file" in exc_info.value.detail


def test_resolve_local_csv_path_rejects_non_csv_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    txt_path = input_dir / "source.txt"
    txt_path.write_text("not,csv\n", encoding="utf-8")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path(str(txt_path))

    assert exc_info.value.status_code == 400
    assert "Only .csv files" in exc_info.value.detail


def test_resolve_local_csv_path_rejects_supported_file_with_wrong_suffix(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    pdf_path = input_dir / "source.pdf"
    pdf_path.write_bytes(b"%PDF-1.4\n")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path(str(pdf_path))

    assert exc_info.value.status_code == 400
    assert "Only .csv files" in exc_info.value.detail


def test_resolve_local_csv_path_rejects_directory_under_configured_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    directory_path = input_dir / "source.csv"
    directory_path.mkdir()
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path(str(directory_path))

    assert exc_info.value.status_code == 400
    assert "existing file" in exc_info.value.detail


def test_resolve_local_csv_path_rejects_broken_symlink_during_strict_canonical_resolution(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    broken_link = input_dir / "source.csv"
    broken_link.symlink_to(input_dir / "missing.csv")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path(str(broken_link))

    assert exc_info.value.status_code == 400
    assert "invalid path components" in exc_info.value.detail


def test_resolve_local_csv_path_rejects_absolute_traversal_outside_configured_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    outside_path = tmp_path / "outside.csv"
    outside_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path(str(input_dir / ".." / outside_path.name))

    assert exc_info.value.status_code == 400
    assert "inside the configured input directory" in exc_info.value.detail


def test_resolve_local_csv_path_rejects_relative_traversal_outside_configured_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    """T2: join-under-root must still reject ``../`` escape. Expected red until T2."""
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    outside_path = tmp_path / "outside.csv"
    outside_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))
    monkeypatch.chdir(input_dir)

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path(f"../{outside_path.name}")

    assert exc_info.value.status_code == 400
    assert "inside the configured input directory" in exc_info.value.detail


def test_resolve_local_csv_path_rejects_symlink_to_file_outside_configured_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    outside_path = tmp_path / "outside.csv"
    outside_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    symlink_path = input_dir / "linked.csv"
    symlink_path.symlink_to(outside_path)
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path(str(symlink_path))

    assert exc_info.value.status_code == 400
    assert "inside the configured input directory" in exc_info.value.detail


def test_resolve_local_csv_path_rejects_relative_symlink_escape_joined_under_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    """T2: relative name that is a symlink escaping the root must fail. Expected red until T2."""
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    outside_path = tmp_path / "outside.csv"
    outside_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    symlink_path = input_dir / "linked.csv"
    symlink_path.symlink_to(outside_path)
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))
    monkeypatch.chdir(tmp_path)

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path("linked.csv")

    assert exc_info.value.status_code == 400
    assert "inside the configured input directory" in exc_info.value.detail


def test_resolve_local_csv_path_rejects_sibling_with_configured_input_dir_prefix(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    prefix_collision_dir = tmp_path / "inputs-escape"
    prefix_collision_dir.mkdir()
    outside_path = prefix_collision_dir / "outside.csv"
    outside_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path(str(outside_path))

    assert exc_info.value.status_code == 400
    assert "inside the configured input directory" in exc_info.value.detail


def test_resolve_local_csv_path_rejects_relative_prefix_collision_joined_under_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    """Containment must use path-component boundaries (commonpath), not string prefix."""
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    # Create a sibling that would fool naive startswith(str(input_dir)).
    # Relative join of an absolute escape must still be rejected after canonicalization.
    prefix_collision_dir = tmp_path / "inputs-escape"
    prefix_collision_dir.mkdir()
    outside_path = prefix_collision_dir / "outside.csv"
    outside_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))
    monkeypatch.chdir(tmp_path)

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_csv_path(str(outside_path))

    assert exc_info.value.status_code == 400
    assert "inside the configured input directory" in exc_info.value.detail


def test_resolve_local_pdf_path_allows_absolute_file_under_configured_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    pdf_path = input_dir / "source.pdf"
    pdf_path.write_bytes(b"%PDF-1.4\n")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    assert resolve_local_pdf_path(str(pdf_path)) == pdf_path.resolve()


def test_resolve_local_pdf_path_allows_relative_file_joined_under_configured_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    """T2: PDF relative paths join under input root. Expected red until T2."""
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    pdf_path = input_dir / "source.pdf"
    pdf_path.write_bytes(b"%PDF-1.4\n")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))
    monkeypatch.chdir(tmp_path)

    assert resolve_local_pdf_path("source.pdf") == pdf_path.resolve()


def test_resolve_local_pdf_path_rejects_non_pdf_file(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    csv_path = input_dir / "source.csv"
    csv_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    with pytest.raises(HTTPException) as exc_info:
        resolve_local_pdf_path(str(csv_path))

    assert exc_info.value.status_code == 400
    assert "Only .pdf files" in exc_info.value.detail
