# FastAPI Application for Motorcycle RAG Local Processing Service

from fastapi import FastAPI, HTTPException, BackgroundTasks
from fastapi.responses import JSONResponse
from fastapi.middleware.cors import CORSMiddleware
import uvicorn
import os
import sys
import logging
import asyncio
from pathlib import Path
from dotenv import load_dotenv

load_dotenv()  # Load .env file if present — no-op when env vars already set (production)

logger = logging.getLogger(__name__)

# Add the src directory to Python path
sys.path.append(str(Path(__file__).parent / "src"))

from processors.pdf_processor import PDFProcessor
from processors.csv_processor import CSVProcessor
from processors.bike_graph_processor import BikeGraphProcessor
from embeddings.ollama_embedder import OllamaEmbedder
from extraction.graph_extractor import GraphExtractor
from storage.blob_writer import BlobWriter
from models.schemas import (
    ProcessPDFRequest,
    ProcessCSVRequest,
    ProcessBikeGraphRequest,
    ProcessingStatusResponse,
)

# Initialize FastAPI app
app = FastAPI(
    title="Motorcycle RAG Local Processing Service",
    description="Local processing service using Docling and Ollama for motorcycle information retrieval",
    version="0.1.0",
)

# CORS middleware
app.add_middleware(
    CORSMiddleware,
    allow_origins=os.getenv("ALLOWED_ORIGINS", "http://localhost:5000").split(","),
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Initialize services
blob_writer = BlobWriter()
embedder = OllamaEmbedder()
graph_extractor = GraphExtractor()

# Initialize processors
pdf_processor = PDFProcessor(
    blob_writer=blob_writer, embedder=embedder, graph_extractor=graph_extractor
)

csv_processor = CSVProcessor(blob_writer=blob_writer, embedder=embedder)
bike_graph_processor = BikeGraphProcessor(blob_writer=blob_writer)

shutdown_requested = False
uvicorn_server: uvicorn.Server | None = None
_ACTIVE_JOB_STATUSES = {"queued", "processing", "running", "inprogress"}


async def _list_all_jobs() -> list[dict]:
    jobs: list[dict] = []
    jobs.extend(await pdf_processor.list_jobs())
    jobs.extend(await csv_processor.list_jobs())
    jobs.extend(await bike_graph_processor.list_jobs())
    jobs.sort(key=lambda job: job.get("created_at", ""), reverse=True)
    return jobs


async def _count_active_jobs() -> int:
    jobs = await _list_all_jobs()
    return sum(
        1
        for job in jobs
        if str(job.get("status", "")).strip().lower() in _ACTIVE_JOB_STATUSES
    )


async def _wait_for_graceful_shutdown() -> None:
    global uvicorn_server
    while await _count_active_jobs() > 0:
        await asyncio.sleep(1)

    if uvicorn_server is not None:
        uvicorn_server.should_exit = True


# Health check endpoint
@app.get("/health")
async def health_check():
    """Check service health and dependencies"""
    try:
        # Test Ollama connectivity
        ollama_status = await embedder.check_ollama_status()

        active_jobs = await _count_active_jobs()
        return JSONResponse(
            content={
                "status": "healthy",
                "accepting_work": not shutdown_requested,
                "shutdown_requested": shutdown_requested,
                "active_jobs": active_jobs,
                "message": (
                    "Shutdown requested - waiting for active jobs to finish"
                    if shutdown_requested
                    else "Processor ready"
                ),
                "services": {
                    "ollama": ollama_status,
                    "blob_storage": blob_writer.is_connected(),
                    "service_uptime": "running",
                },
            },
            status_code=200,
        )
    except Exception as e:
        return JSONResponse(
            content={
                "status": "unhealthy",
                "accepting_work": not shutdown_requested,
                "shutdown_requested": shutdown_requested,
                "active_jobs": 0,
                "message": "Processor health check failed",
                "error": str(e),
                "services": {
                    "ollama": "unknown",
                    "blob_storage": "unknown",
                    "service_uptime": "running",
                },
            },
            status_code=503,
        )


# PDF processing endpoint
@app.post("/process/pdf", response_model=ProcessingStatusResponse)
async def process_pdf(request: ProcessPDFRequest, background_tasks: BackgroundTasks):
    """Process a PDF document from Azure Blob Storage"""
    try:
        if shutdown_requested:
            raise HTTPException(
                status_code=409,
                detail="Processor is shutting down and not accepting new work",
            )
        # Validate request
        if not request.upload_id:
            raise HTTPException(status_code=400, detail="upload_id is required")
        if not request.document_type:
            raise HTTPException(status_code=400, detail="document_type is required")
        if not request.blob_container:
            raise HTTPException(status_code=400, detail="blob_container is required")

        # Start background processing
        job_id = await pdf_processor.process_pdf_async(
            upload_id=request.upload_id,
            document_type=request.document_type,
            blob_container=request.blob_container,
            metadata=request.metadata,
        )

        return ProcessingStatusResponse(
            job_id=job_id,
            status="processing",
            message="PDF processing started in background",
            progress=0,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.exception("Unexpected error in /process/pdf")
        raise HTTPException(
            status_code=500, detail="An unexpected error occurred"
        )


# CSV processing endpoint
@app.post("/process/csv", response_model=ProcessingStatusResponse)
async def process_csv(request: ProcessCSVRequest, background_tasks: BackgroundTasks):
    """Process a CSV document from Azure Blob Storage"""
    try:
        if shutdown_requested:
            raise HTTPException(
                status_code=409,
                detail="Processor is shutting down and not accepting new work",
            )
        # Validate request
        if not request.upload_id:
            raise HTTPException(status_code=400, detail="upload_id is required")
        if not request.blob_container:
            raise HTTPException(status_code=400, detail="blob_container is required")

        # Start background processing
        job_id = await csv_processor.process_csv_async(
            upload_id=request.upload_id,
            blob_container=request.blob_container,
            metadata=request.metadata,
        )

        return ProcessingStatusResponse(
            job_id=job_id,
            status="processing",
            message="CSV processing started in background",
            progress=0,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.exception("Unexpected error in /process/csv")
        raise HTTPException(
            status_code=500, detail="An unexpected error occurred"
        )


# Bike graph CSV processing endpoint
@app.post("/process/bike-graph", response_model=ProcessingStatusResponse)
async def process_bike_graph(request: ProcessBikeGraphRequest, background_tasks: BackgroundTasks):
    """Process a motorcycle spec CSV into graph nodes and edges.

    No LLM or embeddings are used — processing is entirely local and deterministic.
    The resulting nodes/edges JSON is written to blob storage and consumed by
    GraphEntityIngestionService on the C# API side.
    """
    try:
        if shutdown_requested:
            raise HTTPException(
                status_code=409,
                detail="Processor is shutting down and not accepting new work",
            )
        if not request.upload_id:
            raise HTTPException(status_code=400, detail="upload_id is required")

        file_path = None
        if request.local_file_path:
            file_path = Path(request.local_file_path).resolve()
            if not file_path.is_file():
                raise HTTPException(
                    status_code=400,
                    detail="local_file_path does not point to an existing file",
                )
            if file_path.suffix.lower() != ".csv":
                raise HTTPException(status_code=400, detail="Only .csv files are supported")
        elif not request.blob_container:
            raise HTTPException(
                status_code=400,
                detail="Either blob_container or local_file_path is required",
            )

        job_id = await bike_graph_processor.process_async(
            upload_id=request.upload_id,
            blob_container=request.blob_container,
            local_file_path=str(file_path) if file_path else None,
        )

        return ProcessingStatusResponse(
            job_id=job_id,
            status="processing",
            message="Bike graph processing started in background",
            progress=0,
        )

    except HTTPException:
        raise
    except Exception as e:
        logger.exception("Unexpected error in /process/bike-graph")
        raise HTTPException(status_code=500, detail="An unexpected error occurred")


# Job list endpoint
@app.get("/jobs")
async def list_jobs():
    """List known processing jobs across all processors."""
    return await _list_all_jobs()


# Job status endpoint
@app.get("/jobs/{job_id}")
async def get_job_status(job_id: str):
    """Get the status of a processing job"""
    try:
        # Check PDF processor status
        pdf_status = await pdf_processor.get_job_status(job_id)
        if pdf_status:
            return pdf_status

        # Check CSV processor status
        csv_status = await csv_processor.get_job_status(job_id)
        if csv_status:
            return csv_status

        # Check bike graph processor status
        bike_graph_status = await bike_graph_processor.get_job_status(job_id)
        if bike_graph_status:
            return bike_graph_status

        raise HTTPException(status_code=404, detail="Job not found")

    except HTTPException:
        raise
    except Exception as e:
        logger.exception("Unexpected error in /jobs/{job_id}")
        raise HTTPException(
            status_code=500, detail="An unexpected error occurred"
        )


@app.delete("/jobs")
@app.post("/jobs/cleanup")
async def clear_finished_jobs():
    """Delete completed and failed jobs while preserving active work."""
    deleted_count = 0
    deleted_count += await pdf_processor.clear_terminal_jobs()
    deleted_count += await csv_processor.clear_terminal_jobs()
    deleted_count += await bike_graph_processor.clear_terminal_jobs()

    remaining_jobs = len(await _list_all_jobs())
    active_jobs = await _count_active_jobs()

    return {
        "status": "ok",
        "deleted_count": deleted_count,
        "remaining_jobs": remaining_jobs,
        "active_jobs": active_jobs,
        "message": f"Deleted {deleted_count} finished jobs",
    }


@app.post("/control/shutdown")
async def shutdown():
    """Stop accepting work and shut down once active jobs have drained."""
    global shutdown_requested
    shutdown_requested = True
    active_jobs = await _count_active_jobs()
    asyncio.create_task(_wait_for_graceful_shutdown())
    return {
        "status": "stopping",
        "message": "Shutdown requested. Waiting for active jobs to finish.",
        "active_jobs": active_jobs,
    }


# Main entry point
if __name__ == "__main__":
    # Configure logging
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s - %(name)s - %(levelname)s - %(message)s",
    )

    # Get port from environment or use default
    port = int(os.getenv("PORT", 8100))

    # Run the FastAPI app
    config = uvicorn.Config(app, host="0.0.0.0", port=port, reload=False, log_level="info")
    uvicorn_server = uvicorn.Server(config)
    uvicorn_server.run()
