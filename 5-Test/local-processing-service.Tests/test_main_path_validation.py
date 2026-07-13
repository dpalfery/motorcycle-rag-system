from pathlib import Path

import pytest
from fastapi import HTTPException

from security.path_validation import (
    LOCAL_PROCESSOR_INPUT_DIR_ENV,
    resolve_local_csv_path,
    resolve_local_pdf_path,
)


def test_resolve_local_csv_path_allows_file_under_configured_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    csv_path = input_dir / "source.csv"
    csv_path.write_text("make,model\nHonda,CB500\n", encoding="utf-8")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    assert resolve_local_csv_path(str(csv_path)) == csv_path.resolve()


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


def test_resolve_local_pdf_path_allows_file_under_configured_input_dir(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
):
    input_dir = tmp_path / "inputs"
    input_dir.mkdir()
    pdf_path = input_dir / "source.pdf"
    pdf_path.write_bytes(b"%PDF-1.4\n")
    monkeypatch.setenv(LOCAL_PROCESSOR_INPUT_DIR_ENV, str(input_dir))

    assert resolve_local_pdf_path(str(pdf_path)) == pdf_path.resolve()


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
