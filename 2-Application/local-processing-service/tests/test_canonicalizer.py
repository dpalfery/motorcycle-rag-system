"""Unit tests for document canonicalization tool selection and failures."""

from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

from infrastructure.canonicalizer import Canonicalizer, DocumentFormat


def _canonicalizer(*, libreoffice=False, pandoc=False):
    with (
        patch.object(Canonicalizer, "_check_libreoffice", return_value=libreoffice),
        patch.object(Canonicalizer, "_check_pandoc", return_value=pandoc),
    ):
        return Canonicalizer()


@pytest.mark.parametrize(
    ("name", "expected"),
    [
        ("manual.PDF", DocumentFormat.PDF),
        ("manual.docx", DocumentFormat.DOCX),
        ("manual.doc", DocumentFormat.DOC),
        ("notes.txt", DocumentFormat.TXT),
        ("page.html", DocumentFormat.HTML),
        ("page.htm", DocumentFormat.HTML),
        ("readme.md", DocumentFormat.MARKDOWN),
        ("readme.markdown", DocumentFormat.MARKDOWN),
        ("archive.bin", None),
    ],
)
def test_get_file_format_maps_supported_extensions(name, expected):
    assert _canonicalizer().get_file_format(Path(name)) == expected


def test_pdf_needs_no_tool_but_other_formats_do():
    canonicalizer = _canonicalizer()

    assert canonicalizer.needs_canonicalization(Path("manual.pdf")) is False
    assert canonicalizer.can_canonicalize(Path("manual.pdf")) is True
    assert canonicalizer.needs_canonicalization(Path("manual.docx")) is True
    assert canonicalizer.can_canonicalize(Path("manual.docx")) is False


@pytest.mark.parametrize("checker", ["_check_libreoffice", "_check_pandoc"])
def test_tool_check_handles_missing_executable(checker):
    canonicalizer = object.__new__(Canonicalizer)

    with patch("subprocess.run", side_effect=FileNotFoundError):
        assert getattr(canonicalizer, checker)() is False


@pytest.mark.parametrize("checker", ["_check_libreoffice", "_check_pandoc"])
def test_tool_check_uses_successful_exit_code(checker):
    canonicalizer = object.__new__(Canonicalizer)

    with patch("subprocess.run", return_value=MagicMock(returncode=0)) as run:
        assert getattr(canonicalizer, checker)() is True

    assert run.call_args.kwargs["timeout"] == 5


async def test_canonicalize_returns_existing_pdf_unchanged(tmp_path):
    source = tmp_path / "manual.pdf"
    source.write_bytes(b"%PDF")

    result = await _canonicalizer().canonicalize(source)

    assert result == source


async def test_canonicalize_rejects_non_pdf_without_tools(tmp_path):
    with pytest.raises(RuntimeError, match="no conversion tools"):
        await _canonicalizer().canonicalize(tmp_path / "manual.docx")


async def test_canonicalize_prefers_libreoffice_and_creates_output_dir(tmp_path):
    canonicalizer = _canonicalizer(libreoffice=True, pandoc=True)
    expected = tmp_path / "out" / "manual.pdf"
    canonicalizer._convert_with_libreoffice = AsyncMock(return_value=expected)
    canonicalizer._convert_with_pandoc = AsyncMock()

    result = await canonicalizer.canonicalize(tmp_path / "manual.docx", tmp_path / "out")

    assert result == expected
    assert expected.parent.is_dir()
    canonicalizer._convert_with_libreoffice.assert_awaited_once()
    canonicalizer._convert_with_pandoc.assert_not_awaited()


async def test_canonicalize_uses_pandoc_when_libreoffice_unavailable(tmp_path):
    canonicalizer = _canonicalizer(pandoc=True)
    expected = tmp_path / "manual.pdf"
    canonicalizer._convert_with_pandoc = AsyncMock(return_value=expected)

    assert await canonicalizer.canonicalize(tmp_path / "manual.md", tmp_path) == expected


class _Process:
    def __init__(self, returncode=0, stderr=b""):
        self.returncode = returncode
        self._stderr = stderr

    async def communicate(self):
        return b"stdout", self._stderr


@pytest.mark.parametrize(
    ("method", "executable"),
    [
        ("_convert_with_libreoffice", "soffice"),
        ("_convert_with_pandoc", "pandoc"),
    ],
)
async def test_converter_returns_created_pdf(tmp_path, method, executable):
    source = tmp_path / "manual.docx"
    output = tmp_path / "manual.pdf"
    output.write_bytes(b"%PDF")
    canonicalizer = _canonicalizer()

    with patch(
        "asyncio.create_subprocess_exec", new=AsyncMock(return_value=_Process())
    ) as create:
        result = await getattr(canonicalizer, method)(source, tmp_path)

    assert result == output
    assert create.await_args.args[0] == executable


@pytest.mark.parametrize(
    ("method", "message"),
    [
        ("_convert_with_libreoffice", "LibreOffice conversion failed"),
        ("_convert_with_pandoc", "Pandoc conversion failed"),
    ],
)
async def test_converter_wraps_nonzero_exit(tmp_path, method, message):
    canonicalizer = _canonicalizer()

    with patch(
        "asyncio.create_subprocess_exec",
        new=AsyncMock(return_value=_Process(returncode=1, stderr=b"bad input")),
    ):
        with pytest.raises(RuntimeError, match=message):
            await getattr(canonicalizer, method)(tmp_path / "manual.docx", tmp_path)


@pytest.mark.parametrize(
    ("method", "message"),
    [
        ("_convert_with_libreoffice", "did not produce output"),
        ("_convert_with_pandoc", "did not produce output"),
    ],
)
async def test_converter_requires_output_file(tmp_path, method, message):
    canonicalizer = _canonicalizer()

    with patch(
        "asyncio.create_subprocess_exec", new=AsyncMock(return_value=_Process())
    ):
        with pytest.raises(RuntimeError, match=message):
            await getattr(canonicalizer, method)(tmp_path / "manual.docx", tmp_path)
