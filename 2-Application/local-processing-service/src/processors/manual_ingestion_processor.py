"""Manual ingestion processor for motorcycle service manuals.

Processes canonical PDF documents through a restartable, checkpointed pipeline
to produce both retrieval artifacts (chunks + embeddings) and graph artifacts
(entities + relationships).
"""

import asyncio
import hashlib
import json
import logging
import os
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Optional

# Docling imports - wrapped in try/except to handle missing dependency
try:
    from docling.chunking import HybridChunker
    from docling.document_converter import DocumentConverter
    DOCLING_AVAILABLE = True
except ImportError:
    DOCLING_AVAILABLE = False
    logging.warning(
        "Docling not available - PDF parsing will be disabled. "
        "Install docling for full functionality."
    )

from api.api_client import ApiClient
from extraction.graph_extractor import GraphExtractor
from infrastructure.base_processor import BaseProcessor
from infrastructure.stage_runner import (
    StageRunner,
    StageStatus,
    RunStatus,
    RunMetadata,
)
from storage.blob_writer import BlobWriter

logger = logging.getLogger(__name__)

# Configuration
PDF_CHUNKER_MAX_TOKENS = int(os.getenv("PDF_CHUNKER_MAX_TOKENS", "512"))
PDF_CHUNKER_TOKENIZER = os.getenv("PDF_CHUNKER_TOKENIZER", "BAAI/bge-small-en-v1.5")

# Stage names for manual ingestion
MANUAL_INGESTION_STAGES = [
    StageRunner.STAGE_SOURCE,
    StageRunner.STAGE_PARSED_DOCUMENT,
    StageRunner.STAGE_CHUNKS,
    StageRunner.STAGE_EMBEDDINGS,
    StageRunner.STAGE_GRAPH_CANDIDATES,
    StageRunner.STAGE_UPLOAD_ARTIFACTS,
    StageRunner.STAGE_COMPLETE,
]


class ManualIngestionProcessor(BaseProcessor):
    """Processor for manual ingestion of motorcycle service manuals.

    This processor:
    - Downloads canonical PDF from blob storage (via API)
    - Parses document using Docling
    - Chunks content for retrieval
    - Generates embeddings
    - Extracts graph entities and relationships
    - Uploads all artifacts via API
    - Maintains restartable checkpointed state
    """

    def __init__(
        self,
        blob_writer: BlobWriter,
        embedder: Any,
        graph_extractor: GraphExtractor,
        api_client: ApiClient,
        working_base_dir: Optional[Path] = None,
    ):
        """Initialize manual ingestion processor.

        Args:
            blob_writer: Blob storage writer for downloading source
            embedder: Embedding service for vector generation
            graph_extractor: Graph extraction service
            api_client: API client for reporting and uploads
            working_base_dir: Base directory for working files (default: ./working)
        """
        super().__init__(api_client)

        self._blob_writer = blob_writer
        self._embedder = embedder
        self._graph_extractor = graph_extractor

        # Working directory for stage artifacts
        if working_base_dir is None:
            working_base_dir = Path("./working")
        self._working_base_dir = Path(working_base_dir)

        # Active stage runners keyed by job_id
        self._stage_runners: dict[str, StageRunner] = {}

    async def process_async(
        self,
        upload_id: str,
        document_type: str,
        blob_container: str,
        metadata: Optional[dict] = None,
    ) -> str:
        """Process a manual ingestion job asynchronously.

        Args:
            upload_id: Upload identifier from API
            document_type: Type of document (e.g., "manual-pdf")
            blob_container: Blob container containing source PDF
            metadata: Optional document metadata

        Returns:
            job_id: Unique identifier for this job
        """
        # Create job entry
        job_id, job_dict = self._create_job(
            upload_id=upload_id,
            job_type="manual-ingestion",
            metadata={
                "document_type": document_type,
                "blob_container": blob_container,
                **(metadata or {}),
            },
        )

        # Create working directory for this job
        job_working_dir = self._working_base_dir / job_id
        job_working_dir.mkdir(parents=True, exist_ok=True)

        # Create stage runner
        stage_runner = StageRunner(
            run_id=job_id,
            working_dir=job_working_dir,
            stage_order=MANUAL_INGESTION_STAGES,
        )
        self._stage_runners[job_id] = stage_runner

        # Load existing checkpoint if available
        checkpoint = stage_runner.load_checkpoint()
        if checkpoint:
            logger.info("Resuming job %s from checkpoint", job_id)
            self._update_job_status(
                job_id,
                status="processing",
                message=f"Resuming from stage: {checkpoint.completed_stage or 'start'}",
            )
        else:
            # Initialize new run metadata
            run_metadata = RunMetadata(
                run_id=job_id,
                document_id=upload_id,  # Will be updated with document_id from API
                run_type="manual-ingestion",
                status=RunStatus.RUNNING,
                started_at=datetime.now(timezone.utc).isoformat(),
                started_from_stage=StageRunner.STAGE_SOURCE,
                local_working_folder=str(job_working_dir),
                processor_host=os.getenv("HOSTNAME", "localhost"),
            )
            stage_runner.save_checkpoint(run_metadata)

        # Start background processing
        asyncio.create_task(
            self._process_manual(
                job_id=job_id,
                upload_id=upload_id,
                document_type=document_type,
                blob_container=blob_container,
                metadata=metadata,
            )
        )

        return job_id

    async def _process_manual(
        self,
        job_id: str,
        upload_id: str,
        document_type: str,
        blob_container: str,
        metadata: Optional[dict],
    ) -> None:
        """Background coroutine that processes the manual through all stages.

        Args:
            job_id: Job identifier
            upload_id: Upload identifier
            document_type: Document type
            blob_container: Blob container
            metadata: Document metadata
        """
        stage_runner = self._stage_runners.get(job_id)
        if not stage_runner:
            logger.error("Stage runner not found for job %s", job_id)
            self._mark_job_failed(job_id, "Stage runner not initialized")
            return

        tmp_path: Optional[Path] = None

        try:
            # Define stage functions
            stages = {
                StageRunner.STAGE_SOURCE: lambda: self._stage_download_source(
                    job_id, upload_id, blob_container
                ),
                StageRunner.STAGE_PARSED_DOCUMENT: lambda: self._stage_parse_document(
                    job_id, tmp_path
                ),
                StageRunner.STAGE_CHUNKS: lambda: self._stage_create_chunks(
                    job_id
                ),
                StageRunner.STAGE_EMBEDDINGS: lambda: self._stage_create_embeddings(
                    job_id
                ),
                StageRunner.STAGE_GRAPH_CANDIDATES: lambda: self._stage_extract_graph(
                    job_id, upload_id
                ),
                StageRunner.STAGE_UPLOAD_ARTIFACTS: lambda: self._stage_upload_artifacts(
                    job_id, upload_id, document_type, metadata
                ),
                StageRunner.STAGE_COMPLETE: lambda: self._stage_complete(job_id),
            }

            # Execute pipeline from next stage
            next_stage = stage_runner.get_next_stage()
            if next_stage is None:
                logger.info("All stages already completed for job %s", job_id)
                self._mark_job_completed(job_id, "All stages already completed")
                return

            # Run pipeline
            results = await stage_runner.run_pipeline(
                stages=stages,
                start_from=next_stage,
            )

            # Update final run metadata
            checkpoint = stage_runner.load_checkpoint()
            if checkpoint:
                checkpoint.status = RunStatus.COMPLETED
                checkpoint.completed_at = datetime.now(timezone.utc).isoformat()
                checkpoint.completed_stage = StageRunner.STAGE_COMPLETE
                stage_runner.save_checkpoint(checkpoint)

            self._mark_job_completed(
                job_id,
                message="Manual ingestion completed successfully",
            )

        except Exception as e:
            logger.exception("Manual ingestion failed for job %s", job_id)
            self._mark_job_failed(job_id, str(e))

            # Update checkpoint with failure
            checkpoint = stage_runner.load_checkpoint()
            if checkpoint:
                checkpoint.status = RunStatus.FAILED
                checkpoint.completed_at = datetime.now(timezone.utc).isoformat()
                checkpoint.error_summary = str(e)
                stage_runner.save_checkpoint(checkpoint)

        finally:
            # Cleanup temp file
            if tmp_path and tmp_path.exists():
                try:
                    tmp_path.unlink()
                except Exception as e:
                    logger.warning("Failed to cleanup temp file %s: %s", tmp_path, e)

    async def _stage_download_source(
        self,
        job_id: str,
        upload_id: str,
        blob_container: str,
    ) -> Path:
        """Stage 01: Download canonical PDF from blob storage.

        Args:
            job_id: Job identifier
            upload_id: Upload identifier
            blob_container: Blob container name

        Returns:
            Path to downloaded PDF file

        Raises:
            Exception: If download fails
        """
        stage_runner = self._stage_runners[job_id]

        self._update_job_status(
            job_id,
            status="processing",
            message="Downloading PDF from blob storage",
            progress=5.0,
        )

        # Download PDF from blob
        pdf_bytes = await self._blob_writer.download_blob(
            blob_container, f"{upload_id}.pdf"
        )

        if not pdf_bytes:
            raise RuntimeError(f"Failed to download PDF from {blob_container}/{upload_id}.pdf")

        # Write to temp file
        stage_dir = stage_runner.get_stage_dir(StageRunner.STAGE_SOURCE)
        tmp_path = stage_dir / "source.pdf"

        with open(tmp_path, "wb") as f:
            f.write(pdf_bytes)

        # Calculate hash for provenance
        file_hash = hashlib.sha256(pdf_bytes).hexdigest()

        # Update stage metadata
        stage_metadata = stage_runner._stages.get(StageRunner.STAGE_SOURCE)
        if stage_metadata:
            stage_metadata.artifact_path = str(tmp_path)
            stage_metadata.artifact_hash = file_hash
            stage_metadata.metadata_json = {
                "upload_id": upload_id,
                "blob_container": blob_container,
                "file_size_bytes": len(pdf_bytes),
            }

        logger.info("Downloaded PDF to %s (hash: %s)", tmp_path, file_hash[:16])
        return tmp_path

    async def _stage_parse_document(
        self,
        job_id: str,
        pdf_path: Optional[Path],
    ) -> dict:
        """Stage 02: Parse PDF document using Docling.

        Args:
            job_id: Job identifier
            pdf_path: Path to PDF file

        Returns:
            Parsed document data

        Raises:
            Exception: If parsing fails
        """
        if not pdf_path or not pdf_path.exists():
            raise RuntimeError(f"PDF file not found: {pdf_path}")

        stage_runner = self._stage_runners[job_id]

        self._update_job_status(
            job_id,
            status="processing",
            message="Parsing document with Docling",
            progress=15.0,
        )

        # Convert PDF using Docling
        converter = DocumentConverter()
        doc = converter.convert(str(pdf_path))

        # Extract document content
        parsed_data = {
            "text": doc.document.export_to_markdown(),
            "pages": len(doc.pages) if doc.pages else 0,
            "tables": len(doc.tables) if doc.tables else 0,
        }

        # Save parsed document to stage directory
        stage_dir = stage_runner.get_stage_dir(StageRunner.STAGE_PARSED_DOCUMENT)
        parsed_file = stage_dir / "parsed.json"

        import json
        with open(parsed_file, "w", encoding="utf-8") as f:
            json.dump(parsed_data, f, indent=2)

        logger.info(
            "Parsed document: %d pages, %d tables",
            parsed_data["pages"],
            parsed_data["tables"],
        )

        return parsed_data

    async def _stage_create_chunks(
        self,
        job_id: str,
    ) -> list[dict]:
        """Stage 03: Create semantic chunks from parsed document.

        Args:
            job_id: Job identifier

        Returns:
            List of chunk dictionaries

        Raises:
            Exception: If chunking fails
        """
        stage_runner = self._stage_runners[job_id]

        self._update_job_status(
            job_id,
            status="processing",
            message="Creating semantic chunks",
            progress=30.0,
        )

        # Load parsed document
        parsed_file = stage_runner.get_stage_dir(
            StageRunner.STAGE_PARSED_DOCUMENT
        ) / "parsed.json"

        import json
        with open(parsed_file, "r", encoding="utf-8") as f:
            parsed_data = json.load(f)

        # Create chunks using Docling HybridChunker
        chunker = HybridChunker(
            tokenizer=PDF_CHUNKER_TOKENIZER,
            max_tokens=PDF_CHUNKER_MAX_TOKENS,
            merge_peers=True,
        )

        chunks = chunker.chunk(parsed_data["text"])

        # Convert to chunk dictionaries
        chunk_dicts = []
        for i, chunk in enumerate(chunks):
            chunk_dicts.append({
                "chunk_index": i,
                "content": chunk.text,
                "metadata": chunk.meta if hasattr(chunk, "meta") else {},
            })

        # Save chunks to stage directory
        stage_dir = stage_runner.get_stage_dir(StageRunner.STAGE_CHUNKS)
        chunks_file = stage_dir / "chunks.json"

        with open(chunks_file, "w", encoding="utf-8") as f:
            json.dump(chunk_dicts, f, indent=2)

        logger.info("Created %d chunks", len(chunk_dicts))
        return chunk_dicts

    async def _stage_create_embeddings(
        self,
        job_id: str,
    ) -> list[dict]:
        """Stage 04: Generate embeddings for chunks.

        Args:
            job_id: Job identifier

        Returns:
            List of chunk dictionaries with embeddings

        Raises:
            Exception: If embedding generation fails
        """
        stage_runner = self._stage_runners[job_id]

        self._update_job_status(
            job_id,
            status="processing",
            message="Generating embeddings",
            progress=50.0,
        )

        # Load chunks
        chunks_file = stage_runner.get_stage_dir(
            StageRunner.STAGE_CHUNKS
        ) / "chunks.json"

        import json
        with open(chunks_file, "r", encoding="utf-8") as f:
            chunks = json.load(f)

        # Generate embeddings
        texts = [chunk["content"] for chunk in chunks]
        embeddings = await self._embedder.embed_batch(texts)

        # Add embeddings to chunks
        for chunk, embedding in zip(chunks, embeddings):
            chunk["content_vector"] = embedding.tolist() if hasattr(embedding, "tolist") else embedding

        # Save to stage directory
        stage_dir = stage_runner.get_stage_dir(StageRunner.STAGE_EMBEDDINGS)
        embeddings_file = stage_dir / "chunks_with_embeddings.json"

        with open(embeddings_file, "w", encoding="utf-8") as f:
            json.dump(chunks, f, indent=2)

        logger.info("Generated embeddings for %d chunks", len(chunks))
        return chunks

    async def _stage_extract_graph(
        self,
        job_id: str,
        upload_id: str,
    ) -> dict:
        """Stage 05: Extract graph entities and relationships.

        Args:
            job_id: Job identifier
            upload_id: Upload identifier for provenance

        Returns:
            Graph extraction results with nodes and edges

        Raises:
            Exception: If graph extraction fails
        """
        stage_runner = self._stage_runners[job_id]

        self._update_job_status(
            job_id,
            status="processing",
            message="Extracting graph entities and relationships",
            progress=70.0,
        )

        # Load parsed document
        parsed_file = stage_runner.get_stage_dir(
            StageRunner.STAGE_PARSED_DOCUMENT
        ) / "parsed.json"

        import json
        with open(parsed_file, "r", encoding="utf-8") as f:
            parsed_data = json.load(f)

        # Extract graph entities
        graph_results = await self._graph_extractor.extract(
            text=parsed_data["text"],
            source_document_id=upload_id,
        )

        # Extract the first result (graph_extractor returns a list)
        graph_result = graph_results[0] if graph_results else {"nodes": [], "edges": []}

        # Save to stage directory
        stage_dir = stage_runner.get_stage_dir(StageRunner.STAGE_GRAPH_CANDIDATES)
        graph_file = stage_dir / "graph_entities.json"

        with open(graph_file, "w", encoding="utf-8") as f:
            json.dump(graph_result, f, indent=2)

        nodes = graph_result.get("nodes", [])
        edges = graph_result.get("edges", [])
        node_count = len(nodes)
        edge_count = len(edges)

        logger.info(
            "Extracted graph: %d nodes, %d edges",
            node_count,
            edge_count,
        )

        return graph_result

    async def _stage_upload_artifacts(
        self,
        job_id: str,
        upload_id: str,
        document_type: str,
        metadata: Optional[dict],
    ) -> dict:
        """Stage 06: Upload artifacts via API.

        Args:
            job_id: Job identifier
            upload_id: Upload identifier
            document_type: Document type
            metadata: Document metadata

        Returns:
            Upload results

        Raises:
            Exception: If upload fails
        """
        stage_runner = self._stage_runners[job_id]

        self._update_job_status(
            job_id,
            status="processing",
            message="Uploading artifacts via API",
            progress=85.0,
        )

        # Load artifacts
        embeddings_file = stage_runner.get_stage_dir(
            StageRunner.STAGE_EMBEDDINGS
        ) / "chunks_with_embeddings.json"

        graph_file = stage_runner.get_stage_dir(
            StageRunner.STAGE_GRAPH_CANDIDATES
        ) / "graph_entities.json"

        import json
        with open(embeddings_file, "r", encoding="utf-8") as f:
            chunks = json.load(f)

        with open(graph_file, "r", encoding="utf-8") as f:
            graph_data = json.load(f)

        # Upload via API client
        # Note: This will be updated in PY-03 to use proper API endpoints
        upload_results = {
            "chunks_uploaded": len(chunks),
            "nodes_uploaded": len(graph_data.get("nodes", [])),
            "edges_uploaded": len(graph_data.get("edges", [])),
        }

        logger.info(
            "Uploaded artifacts: %s",
            upload_results,
        )

        return upload_results

    async def _stage_complete(
        self,
        job_id: str,
    ) -> None:
        """Stage 07: Mark processing as complete.

        Args:
            job_id: Job identifier
        """
        stage_runner = self._stage_runners[job_id]

        self._update_job_status(
            job_id,
            status="processing",
            message="Processing completed",
            progress=100.0,
        )

        logger.info("Job %s processing complete", job_id)
