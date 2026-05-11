"""Document canonicalization utilities.

Provides functionality to convert various document formats to canonical PDF
format before processing.
"""

import logging
import os
import tempfile
from pathlib import Path
from typing import Optional

logger = logging.getLogger(__name__)


class DocumentFormat:
    """Supported document formats for canonicalization."""
    PDF = "pdf"
    DOCX = "docx"
    DOC = "doc"
    TXT = "txt"
    HTML = "html"
    MARKDOWN = "md"


class Canonicalizer:
    """Converts documents to canonical PDF format.

    The canonicalizer ensures all documents are in PDF format before
    downstream processing, providing a stable source format for
    chunking, embedding, and graph extraction.
    """

    def __init__(self):
        """Initialize canonicalizer."""
        # Check for available conversion tools
        self._has_libreoffice = self._check_libreoffice()
        self._has_pandoc = self._check_pandoc()

        if not self._has_libreoffice and not self._has_pandoc:
            logger.warning(
                "No document conversion tools found. "
                "Only PDF files will be processed directly."
            )

    def _check_libreoffice(self) -> bool:
        """Check if LibreOffice is available for conversion."""
        try:
            import subprocess
            result = subprocess.run(
                "soffice",
                "--version",
                capture_output=True,
                text=True,
                timeout=5,
            )
            return result.returncode == 0
        except (FileNotFoundError, subprocess.TimeoutExpired):
            return False
        except subprocess.TimeoutExpired:
            return False

    def _check_pandoc(self) -> bool:
        """Check if Pandoc is available for conversion."""
        try:
            import subprocess
            result = subprocess.run(
                "pandoc",
                "--version",
                capture_output=True,
                text=True,
                timeout=5,
            )
            return result.returncode == 0
        except (FileNotFoundError, subprocess.TimeoutExpired):
            return False
        except subprocess.TimeoutExpired:
            return False

    def needs_canonicalization(self, file_path: Path) -> bool:
        """Check if a file needs to be canonicalized to PDF.

        Args:
            file_path: Path to the source file

        Returns:
            True if file is not already a PDF, False otherwise
        """
        file_ext = file_path.suffix.lower().lstrip(".")
        return file_ext != DocumentFormat.PDF

    def can_canonicalize(self, file_path: Path) -> bool:
        """Check if canonicalization is possible for this file.

        Args:
            file_path: Path to the source file

        Returns:
            True if conversion tools are available, False otherwise
        """
        if not self.needs_canonicalization(file_path):
            return True  # Already PDF, no conversion needed

        return self._has_libreoffice or self._has_pandoc

    async def canonicalize(
        self,
        source_path: Path,
        output_dir: Optional[Path] = None,
    ) -> Path:
        """Convert document to canonical PDF format.

        Args:
            source_path: Path to the source document
            output_dir: Directory to write output PDF (default: temp dir)

        Returns:
            Path to the canonicalized PDF file

        Raises:
            RuntimeError: If conversion fails or no tools available
        """
        if not self.can_canonicalize(source_path):
            raise RuntimeError(
                f"Cannot canonicalize {source_path}: no conversion tools available"
            )

        if not self.needs_canonicalization(source_path):
            logger.info("File is already PDF, skipping canonicalization")
            return source_path

        # Create output directory if needed
        if output_dir is None:
            output_dir = Path(tempfile.gettempdir())
        output_dir.mkdir(parents=True, exist_ok=True)

        # Determine output path
        output_path = output_dir / f"{source_path.stem}.pdf"

        # Convert based on available tools
        file_ext = source_path.suffix.lower().lstrip(".")

        if self._has_libreoffice:
            output_path = await self._convert_with_libreoffice(
                source_path, output_dir
            )
        elif self._has_pandoc:
            output_path = await self._convert_with_pandoc(
                source_path, output_dir
            )
        else:
            raise RuntimeError("No conversion tools available")

        logger.info(
            "Canonicalized %s to %s",
            source_path,
            output_path,
        )

        return output_path

    async def _convert_with_libreoffice(
        self,
        source_path: Path,
        output_dir: Path,
    ) -> Path:
        """Convert document using LibreOffice headless mode.

        Args:
            source_path: Path to source document
            output_dir: Directory for output PDF

        Returns:
            Path to converted PDF

        Raises:
            RuntimeError: If conversion fails
        """
        import subprocess
        import asyncio

        output_path = output_dir / f"{source_path.stem}.pdf"

        try:
            # Run LibreOffice in headless mode
            process = await asyncio.create_subprocess_exec(
                "soffice",
                "--headless",
                "--convert-to", "pdf",
                "--outdir", str(output_dir),
                str(source_path),
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
            )

            stdout, stderr = await process.communicate()

            if process.returncode != 0:
                raise RuntimeError(
                    f"LibreOffice conversion failed: {stderr.decode('utf-8', errors='ignore')}"
                )

            if not output_path.exists():
                raise RuntimeError(f"LibreOffice did not produce output file")

            return output_path

        except FileNotFoundError:
            raise RuntimeError("LibreOffice not found")
        except Exception as e:
            raise RuntimeError(f"LibreOffice conversion error: {e}")

    async def _convert_with_pandoc(
        self,
        source_path: Path,
        output_dir: Path,
    ) -> Path:
        """Convert document using Pandoc.

        Args:
            source_path: Path to source document
            output_dir: Directory for output PDF

        Returns:
            Path to converted PDF

        Raises:
            RuntimeError: If conversion fails
        """
        import subprocess
        import asyncio

        output_path = output_dir / f"{source_path.stem}.pdf"

        try:
            # Run Pandoc
            process = await asyncio.create_subprocess_exec(
                "pandoc",
                str(source_path),
                "-o", str(output_path),
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
            )

            stdout, stderr = await process.communicate()

            if process.returncode != 0:
                raise RuntimeError(
                    f"Pandoc conversion failed: {stderr.decode('utf-8', errors='ignore')}"
                )

            if not output_path.exists():
                raise RuntimeError(f"Pandoc did not produce output file")

            return output_path

        except FileNotFoundError:
            raise RuntimeError("Pandoc not found")
        except Exception as e:
            raise RuntimeError(f"Pandoc conversion error: {e}")

    def get_file_format(self, file_path: Path) -> Optional[str]:
        """Detect document format from file extension.

        Args:
            file_path: Path to the file

        Returns:
            Document format string or None if unknown
        """
        ext = file_path.suffix.lower().lstrip(".")
        format_map = {
            "pdf": DocumentFormat.PDF,
            "docx": DocumentFormat.DOCX,
            "doc": DocumentFormat.DOC,
            "txt": DocumentFormat.TXT,
            "html": DocumentFormat.HTML,
            "htm": DocumentFormat.HTML,
            "md": DocumentFormat.MARKDOWN,
            "markdown": DocumentFormat.MARKDOWN,
        }
        return format_map.get(ext)
