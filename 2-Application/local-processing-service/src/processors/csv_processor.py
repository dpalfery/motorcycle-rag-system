import pandas as pd
import asyncio
import json
import uuid
import os
import io
import logging
from api.api_client import ApiClient
from search.azure_search_uploader import AzureSearchDirectUploader
from datetime import datetime, timezone

logger = logging.getLogger(__name__)
_ACTIVE_JOB_STATUSES = {"queued", "processing", "running", "inprogress"}

_jobs: dict[str, dict] = {}

MAX_CHUNK_SIZE_TOKENS = int(os.getenv("MAX_CHUNK_SIZE_TOKENS", "512"))


def _estimate_tokens(text: str) -> int:
    return len(text) // 4


def _format_row(row: pd.Series) -> str:
    return " | ".join(f"{col}: {val}" for col, val in row.items() if pd.notna(val))


def _split_text_into_chunks(text: str, max_tokens: int) -> list[str]:
    if _estimate_tokens(text) <= max_tokens:
        return [text]
    lines = text.split("\n")
    chunks = []
    current_lines: list[str] = []
    current_tokens = 0
    for line in lines:
        line_tokens = _estimate_tokens(line)
        if current_tokens + line_tokens > max_tokens and current_lines:
            chunks.append("\n".join(current_lines))
            current_lines = []
            current_tokens = 0
        current_lines.append(line)
        current_tokens += line_tokens
    if current_lines:
        chunks.append("\n".join(current_lines))
    return chunks


class CSVProcessor:
    def __init__(self, blob_writer, embedder, api_client: ApiClient):
        self.blob_writer = blob_writer
        self.embedder = embedder
        self._api_client = api_client

    async def process_csv_async(
        self, upload_id: str, blob_container: str, metadata=None
    ) -> str:
        job_id = str(uuid.uuid4())
        now = datetime.now(timezone.utc).isoformat()
        _jobs[job_id] = {
            "job_id": job_id,
            "upload_id": upload_id,
            "document_type": "spec-dataset",
            "status": "processing",
            "message": "CSV processing started",
            "progress": 0.0,
            "created_at": now,
            "updated_at": now,
        }
        asyncio.create_task(
            self._process_background(job_id, upload_id, blob_container, metadata)
        )
        return job_id

    async def get_job_status(self, job_id: str) -> dict | None:
        return _jobs.get(job_id)

    async def list_jobs(self) -> list[dict]:
        return list(_jobs.values())

    async def clear_terminal_jobs(self) -> int:
        terminal_job_ids = [
            job_id
            for job_id, job in _jobs.items()
            if str(job.get("status", "")).strip().lower() not in _ACTIVE_JOB_STATUSES
        ]

        for job_id in terminal_job_ids:
            _jobs.pop(job_id, None)

        return len(terminal_job_ids)

    async def _process_background(
        self, job_id: str, upload_id: str, blob_container: str, metadata
    ) -> None:
        try:
            csv_bytes = await self.blob_writer.download_blob(
                blob_container, f"{upload_id}.csv"
            )
            df = pd.read_csv(io.BytesIO(csv_bytes))

            if df.empty:
                _jobs[job_id].update(
                    {
                        "status": "failed",
                        "message": "Empty CSV file",
                        "progress": 1.0,
                        "updated_at": datetime.now(timezone.utc).isoformat(),
                    }
                )
                return

            df.columns = [col.strip().lower() for col in df.columns]

            grouping_cols = [c for c in ["make", "model", "year"] if c in df.columns]

            if grouping_cols:
                grouped = df.groupby(grouping_cols, dropna=False)
            else:
                grouped = [("all", df)]

            chunks: list[dict] = []
            chunk_index = 0

            meta_make = getattr(metadata, "make", None) if metadata else None
            meta_model = getattr(metadata, "model", None) if metadata else None
            meta_year = getattr(metadata, "year", None) if metadata else None

            groups = list(grouped) if grouping_cols else grouped
            total_groups = len(groups)

            for i, (group_key, group_df) in enumerate(groups):
                if grouping_cols:
                    key_dict = (
                        dict(zip(grouping_cols, group_key))
                        if isinstance(group_key, tuple)
                        else {grouping_cols[0]: group_key}
                    )
                else:
                    key_dict = {}

                make = str(key_dict.get("make", meta_make or "Unknown"))
                model = str(key_dict.get("model", meta_model or "Unknown"))
                year_val = key_dict.get("year", meta_year)
                year = (
                    int(year_val) if year_val is not None and pd.notna(year_val) else 0
                )

                row_texts = [_format_row(row) for _, row in group_df.iterrows()]
                group_text = "\n".join(row_texts)

                sub_chunks = _split_text_into_chunks(group_text, MAX_CHUNK_SIZE_TOKENS)

                if len(sub_chunks) > 1:
                    title_base = (
                        f"{make} {model} {year} - Specifications"
                        if make != "Unknown"
                        else "Motorcycle Specifications"
                    )
                else:
                    title_base = (
                        f"{make} {model} {year} - Specifications"
                        if make != "Unknown"
                        else "Motorcycle Specifications"
                    )

                for text in sub_chunks:
                    embedding = await self.embedder.generate_embedding(text)
                    now = datetime.now(timezone.utc).isoformat()

                    chunk = {
                        "id": f"{upload_id}-csv-{chunk_index}",
                        "title": title_base,
                        "content": text,
                        "documentType": "spec-dataset",
                        "make": make,
                        "model": model,
                        "year": year,
                        "sourceFile": upload_id,
                        "section": "Specifications",
                        "pageNumber": 0,
                        "pageRange": "N/A",
                        "primarySection": "Specifications",
                        "sectionLevel": 1,
                        "sectionHeadings": ["Specifications"],
                        "tableCaption": None,
                        "chunkIndex": chunk_index,
                        "tags": ["csv", "specifications"],
                        "contentVector": embedding,
                        "createdAt": now,
                        "updatedAt": now,
                    }
                    chunks.append(chunk)
                    chunk_index += 1

                _jobs[job_id]["progress"] = round((i + 1) / total_groups * 0.9, 2)
                _jobs[job_id]["updated_at"] = datetime.now(timezone.utc).isoformat()

            chunks_bytes = ("\n".join(json.dumps(c) for c in chunks)).encode("utf-8")
            await self._api_client.upload_artifact(
                chunks_bytes, upload_id, "search-chunks", "application/x-ndjson"
            )

            # Direct push to Azure AI Search (no-op if AZURE_SEARCH_ENDPOINT not set)
            uploader = AzureSearchDirectUploader()
            uploader.upload(chunks)

            _jobs[job_id].update(
                {
                    "status": "completed",
                    "message": f"Processed {len(chunks)} chunks from CSV",
                    "progress": 1.0,
                    "chunks_processed": len(chunks),
                    "updated_at": datetime.now(timezone.utc).isoformat(),
                }
            )
            logger.info(
                "CSV processing completed for upload %s: %d chunks",
                upload_id,
                len(chunks),
            )

        except Exception as exc:
            logger.exception("CSV processing failed for upload %s", upload_id)
            _jobs[job_id].update(
                {
                    "status": "failed",
                    "message": f"CSV processing failed — {type(exc).__name__}: {str(exc)[:300]}",
                    "progress": 0.0,
                    "updated_at": datetime.now(timezone.utc).isoformat(),
                }
            )
