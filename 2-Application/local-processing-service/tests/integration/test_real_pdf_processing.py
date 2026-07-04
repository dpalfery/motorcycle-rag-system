"""Real PDF ingestion tests for the local processor.

These tests intentionally exercise Docling and HybridChunker with the configured
tokenizer. Do not skip tokenizer failures here; missing tokenizer/model assets are
part of what this integration test is meant to expose.
"""

import asyncio
import json
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, MagicMock

import pytest

from processors.pdf_processor import PDFProcessor, _jobs


REAL_MANUAL_PATH = Path(
    "/Users/dave/Library/CloudStorage/OneDrive-Personal/Code/"
    "Motorcycle-manuals/ml.remawmom.amjc2626omen.pdf"
)
LM_STUDIO_TOKENIZER_CANDIDATES = [
    Path("/Users/dave/.lmstudio/models/mlx-community/Qwen3.5-0.8B-8bit"),
    Path("/Users/dave/.lmstudio/models/mlx-community/Qwen3.5-9B-8bit"),
    Path("/Users/dave/.lmstudio/models/lmstudio-community/Qwen3.5-9B-MLX-4bit"),
    Path("/Users/dave/.lmstudio/models/lmstudio-community/Qwen2.5-0.5B-Instruct-MLX-4bit"),
]


def _resolve_local_tokenizer_path() -> Path | None:
    for candidate in LM_STUDIO_TOKENIZER_CANDIDATES:
        if (candidate / "tokenizer.json").exists() or (candidate / "vocab.json").exists():
            return candidate
    return None


class DeterministicEmbedder:
    """Creates fixed-size vectors without calling an external embedding service."""

    async def generate_embedding(self, text: str) -> list[float]:
        seed = min(len(text), 999) / 999.0
        return [seed] * 3584


class NoOpGraphExtractor:
    async def extract(self, text: str, source_document_id: str) -> dict:
        return {"nodes": [], "edges": []}


class InMemoryVectorDatabase:
    def __init__(self) -> None:
        self.records: list[dict] = []

    def upsert_jsonl(self, payload: bytes) -> None:
        for line in payload.decode("utf-8").splitlines():
            if not line.strip():
                continue

            record = json.loads(line)
            vector = record.get("contentVector")
            if not isinstance(vector, list) or len(vector) != 3584:
                raise AssertionError(
                    f"Chunk {record.get('id')} does not contain a 3584-dim vector"
                )

            self.records.append(record)

    def contains_text(self, *terms: str) -> bool:
        combined = "\n".join(str(record.get("content", "")) for record in self.records)
        normalized = combined.lower()
        return all(term.lower() in normalized for term in terms)


@pytest.fixture(autouse=True)
def _clear_jobs():
    _jobs.clear()
    yield
    _jobs.clear()


async def _wait_for_terminal_status(processor: PDFProcessor, job_id: str) -> dict:
    deadline = asyncio.get_running_loop().time() + 180
    status: dict | None = None

    while asyncio.get_running_loop().time() < deadline:
        status = await processor.get_job_status(job_id)
        if status and str(status.get("status", "")).lower() in {
            "completed",
            "failed",
            "error",
        }:
            return status

        await asyncio.sleep(1)

    pytest.fail(f"PDF processing job did not finish within 180s. Last status: {status}")


@pytest.mark.slow
async def test_real_manual_pdf_is_chunked_and_indexed_into_vector_database(
    monkeypatch: pytest.MonkeyPatch,
):
    if not REAL_MANUAL_PATH.exists():
        pytest.fail(f"Required manual PDF is missing: {REAL_MANUAL_PATH}")

    tokenizer_path = _resolve_local_tokenizer_path()
    if tokenizer_path is None:
        pytest.skip("No local LM Studio tokenizer cache was found for integration testing")

    monkeypatch.setenv("TOKENIZER_MODEL_PATH", str(tokenizer_path))

    vector_database = InMemoryVectorDatabase()
    artifacts: dict[str, bytes] = {}
    api_client = MagicMock()
    api_client.is_configured = MagicMock(return_value=False)
    api_client.download_source = AsyncMock(return_value=REAL_MANUAL_PATH.read_bytes())

    async def upload_artifact(
        data: bytes,
        upload_id: str,
        artifact_type: str,
        content_type: str,
    ) -> None:
        artifacts[artifact_type] = data
        if artifact_type == "search-chunks":
            vector_database.upsert_jsonl(data)

    api_client.upload_artifact = AsyncMock(side_effect=upload_artifact)

    processor = PDFProcessor(
        blob_writer=MagicMock(),
        embedder=DeterministicEmbedder(),
        graph_extractor=NoOpGraphExtractor(),
        api_client=api_client,
    )

    upload_id = "real-cbr600rr-manual"
    job_id = await processor.process_pdf_async(
        upload_id=upload_id,
        document_type="manual-pdf",
        blob_container="raw-uploads",
        metadata=SimpleNamespace(make="Honda", model="CBR600RR", year=2026),
        source_access_token="local-test-token",
    )

    status = await _wait_for_terminal_status(processor, job_id)

    assert status["status"] == "completed", status
    assert status["chunks_processed"] > 0
    assert "search-chunks" in artifacts
    assert "graph-entities" in artifacts
    assert len(vector_database.records) == status["chunks_processed"]
    assert vector_database.contains_text("2026 CBR600RR", "Honda")
    assert all(record["sourceFile"] == upload_id for record in vector_database.records)
    assert any(record["model"] == "CBR600RR" for record in vector_database.records)
