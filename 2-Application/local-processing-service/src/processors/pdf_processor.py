"""PDF processor using Docling HybridChunker for semantic chunking."""

import asyncio
import logging
import os
import tempfile
import uuid
from datetime import datetime, timezone

from docling.chunking import HybridChunker
from docling.document_converter import DocumentConverter

from embeddings.ollama_embedder import OllamaEmbedder
from extraction.graph_extractor import GraphExtractor
from storage.blob_writer import BlobWriter

logger = logging.getLogger(__name__)

_jobs: dict[str, dict] = {}


class PDFProcessor:
    def __init__(
        self,
        blob_writer: BlobWriter,
        embedder: OllamaEmbedder,
        graph_extractor: GraphExtractor,
    ):
        self._blob_writer = blob_writer
        self._embedder = embedder
        self._graph_extractor = graph_extractor

    async def process_pdf_async(
        self, upload_id: str, document_type: str, blob_container: str, metadata
    ) -> str:
        """Returns job_id immediately, fires background task via asyncio.create_task."""
        job_id = str(uuid.uuid4())
        now = datetime.now(timezone.utc).isoformat()
        _jobs[job_id] = {
            "job_id": job_id,
            "status": "processing",
            "progress": 0.0,
            "message": "PDF processing started",
            "chunks_processed": 0,
            "created_at": now,
            "updated_at": now,
        }
        asyncio.create_task(
            self._process_pdf(
                job_id, upload_id, document_type, blob_container, metadata
            )
        )
        return job_id

    async def get_job_status(self, job_id: str) -> dict | None:
        """Returns job dict from _jobs or None if not found."""
        return _jobs.get(job_id)

    async def _process_pdf(
        self,
        job_id: str,
        upload_id: str,
        document_type: str,
        blob_container: str,
        metadata,
    ) -> None:
        """Background coroutine that downloads, chunks, embeds, and uploads."""
        tmp_path: str | None = None
        try:
            # ---- Download PDF from blob to temp file ----
            _jobs[job_id]["message"] = "Downloading PDF from blob storage"
            _jobs[job_id]["updated_at"] = datetime.now(timezone.utc).isoformat()

            pdf_bytes = await self._blob_writer.download_blob(
                blob_container, f"{upload_id}/source.pdf"
            )

            tmp_file = tempfile.NamedTemporaryFile(suffix=".pdf", delete=False)
            tmp_path = tmp_file.name
            tmp_file.write(pdf_bytes)
            tmp_file.close()

            _jobs[job_id]["progress"] = 0.1
            _jobs[job_id]["message"] = "Converting PDF with Docling"
            _jobs[job_id]["updated_at"] = datetime.now(timezone.utc).isoformat()

            # ---- Convert and chunk with Docling ----
            converter = DocumentConverter()
            result = await asyncio.to_thread(converter.convert, str(tmp_path))

            chunker = HybridChunker(tokenizer="BAAI/bge-small-en-v1.5", max_tokens=512)
            chunks = list(await asyncio.to_thread(chunker.chunk, result.document))

            if not chunks:
                _jobs[job_id].update(
                    {
                        "status": "failed",
                        "error": "No chunks extracted from PDF",
                        "progress": 1.0,
                        "updated_at": datetime.now(timezone.utc).isoformat(),
                    }
                )
                return

            _jobs[job_id]["progress"] = 0.3
            _jobs[job_id]["message"] = f"Generating embeddings for {len(chunks)} chunks"
            _jobs[job_id]["updated_at"] = datetime.now(timezone.utc).isoformat()

            # ---- Build chunk records with embeddings ----
            records: list[dict] = []
            total_chunks = len(chunks)

            for i, chunk in enumerate(chunks):
                headings = list(chunk.meta.headings) if chunk.meta.headings else []
                has_prov = chunk.meta.doc_items and chunk.meta.doc_items[0].prov
                page_no = chunk.meta.doc_items[0].prov[0].page_no if has_prov else 0

                embedding = await self._embedder.generate_embedding(chunk.text)
                now = datetime.now(timezone.utc).isoformat()

                record = {
                    "id": str(uuid.uuid4()),
                    "title": headings[0] if headings else f"Chunk {i}",
                    "content": chunk.text,
                    "documentType": document_type,
                    "make": metadata.make or "",
                    "model": metadata.model or "",
                    "year": metadata.year or 0,
                    "sourceFile": upload_id,
                    "section": headings[0] if headings else "",
                    "pageNumber": page_no,
                    "pageRange": str(page_no) if page_no else "0",
                    "primarySection": headings[0] if headings else "",
                    "sectionLevel": len(headings),
                    "sectionHeadings": headings,
                    "tableCaption": None,
                    "chunkIndex": i,
                    "tags": [metadata.make, metadata.model] if metadata.make else [],
                    "contentVector": embedding,
                    "createdAt": now,
                    "updatedAt": now,
                }
                records.append(record)

                # Update progress: embedding phase spans 0.3 → 0.8
                _jobs[job_id]["progress"] = round(0.3 + (i + 1) / total_chunks * 0.5, 2)
                _jobs[job_id]["updated_at"] = datetime.now(timezone.utc).isoformat()

            # ---- Upload chunk JSONL to blob ----
            _jobs[job_id]["progress"] = 0.85
            _jobs[job_id]["message"] = "Uploading chunks to blob storage"
            _jobs[job_id]["updated_at"] = datetime.now(timezone.utc).isoformat()

            await self._blob_writer.upload_jsonl(
                "search-chunks", f"search-chunks/{upload_id}/chunks.jsonl", records
            )

            # ---- Extract graph entities ----
            _jobs[job_id]["progress"] = 0.9
            _jobs[job_id]["message"] = "Extracting graph entities"
            _jobs[job_id]["updated_at"] = datetime.now(timezone.utc).isoformat()

            combined_text = "\n\n".join(chunk.text for chunk in chunks)
            entities = await self._graph_extractor.extract(combined_text, source_document_id=upload_id)

            await self._blob_writer.upload_json(
                "graph-entities",
                f"graph-entities/{upload_id}/entities.json",
                entities,
            )

            # ---- Mark completed ----
            _jobs[job_id].update(
                {
                    "status": "completed",
                    "progress": 1.0,
                    "message": f"Processed {len(records)} chunks from PDF",
                    "chunks_processed": len(records),
                    "updated_at": datetime.now(timezone.utc).isoformat(),
                }
            )
            logger.info(
                "PDF processing completed for upload %s: %d chunks",
                upload_id,
                len(records),
            )

        except Exception as e:
            logger.exception("PDF processing failed for upload %s", upload_id)
            _jobs[job_id].update(
                {
                    "status": "failed",
                    "error": "Processing failed \u2014 see server logs for details",
                    "message": "PDF processing failed",
                    "progress": 0.0,
                    "updated_at": datetime.now(timezone.utc).isoformat(),
                }
            )

        finally:
            if tmp_path and os.path.exists(tmp_path):
                os.unlink(tmp_path)
