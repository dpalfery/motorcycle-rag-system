# FastAPI Application for Motorcycle RAG Local Processing Service

from fastapi import FastAPI, HTTPException, BackgroundTasks, Query
from fastapi.responses import JSONResponse
from fastapi.middleware.cors import CORSMiddleware
import uvicorn
import os
import sys
import logging
import logging.handlers
import asyncio
from pathlib import Path
from dotenv import load_dotenv

load_dotenv()  # Load .env file if present — no-op when env vars already set (production)


def _configure_logging() -> None:
    """Configure root logger with console and daily rolling file handlers.

    Called at module load time so logs are captured even if startup fails.
    """
    log_dir = os.getenv("LOCAL_PROCESSOR_LOG_DIR", "./logs")
    os.makedirs(log_dir, exist_ok=True)

    fmt = logging.Formatter("%(asctime)s %(levelname)-8s %(name)s — %(message)s")

    console_handler = logging.StreamHandler()
    console_handler.setFormatter(fmt)

    file_handler = logging.handlers.TimedRotatingFileHandler(
        filename=os.path.join(log_dir, "local-processor.log"),
        when="midnight",
        backupCount=14,
        encoding="utf-8",
    )
    file_handler.setFormatter(fmt)

    root = logging.getLogger()
    root.setLevel(logging.INFO)
    root.addHandler(console_handler)
    root.addHandler(file_handler)


_configure_logging()

logger = logging.getLogger(__name__)

# Add the src directory to Python path
sys.path.append(str(Path(__file__).parent / "src"))

from processors.pdf_processor import PDFProcessor
from processors.csv_processor import CSVProcessor
from processors.bike_graph_processor import BikeGraphProcessor
from embeddings.embedder_factory import get_embedder
from embeddings.model_discovery import ModelDiscoveryError, discover_embedding_models
from extraction.graph_extractor import GraphExtractor
from storage.blob_writer import BlobWriter
from api.api_client import ApiClient
from models.schemas import (
    ProcessPDFRequest,
    ProcessCSVRequest,
    ProcessBikeGraphRequest,
    ProcessingStatusResponse,
)
from security.path_validation import resolve_local_csv_path

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
api_client = ApiClient()
embedder = get_embedder()
graph_extractor = GraphExtractor()

# Initialize processors
pdf_processor = PDFProcessor(
    blob_writer=blob_writer, embedder=embedder, graph_extractor=graph_extractor, api_client=api_client
)

csv_processor = CSVProcessor(blob_writer=blob_writer, embedder=embedder, api_client=api_client)
bike_graph_processor = BikeGraphProcessor(blob_writer=blob_writer, api_client=api_client)

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


def _build_health_response(
    *,
    embedding_provider_status: str,
    active_jobs: int,
) -> JSONResponse:
    blob_storage_connected = blob_writer.is_connected()
    api_client_configured = api_client.is_configured()

    if embedding_provider_status != "connected":
        return JSONResponse(
            content={
                "status": "unhealthy",
                "accepting_work": False,
                "shutdown_requested": shutdown_requested,
                "active_jobs": active_jobs,
                "message": (
                    "Embedding provider unavailable. Start an embedding provider "
                    "before submitting work."
                ),
                "api_client_configured": api_client_configured,
                "services": {
                    "embedding_provider": embedding_provider_status,
                    "blob_storage": blob_storage_connected,
                    "service_uptime": "running",
                },
            },
            status_code=503,
        )

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
            "api_client_configured": api_client_configured,
            "services": {
                "embedding_provider": embedding_provider_status,
                "blob_storage": blob_storage_connected,
                "service_uptime": "running",
            },
        },
        status_code=200,
    )


# Health check endpoint
@app.get("/health")
async def health_check():
    """Check service health and dependencies"""
    try:
        embedding_provider_status = await embedder.check_status()
        active_jobs = await _count_active_jobs()
        return _build_health_response(
            embedding_provider_status=embedding_provider_status,
            active_jobs=active_jobs,
        )
    except Exception as e:
        logger.exception("Health check failed")
        return JSONResponse(
            content={
                "status": "unhealthy",
                "accepting_work": not shutdown_requested,
                "shutdown_requested": shutdown_requested,
                "active_jobs": 0,
                "message": "Processor health check failed",
                "services": {
                    "embedding_provider": "unknown",
                    "blob_storage": "unknown",
                    "service_uptime": "running",
                },
            },
            status_code=503,
        )


@app.get("/embedding/models")
async def list_embedding_models(
    endpoint: str = Query(..., description="Embedding provider endpoint to inspect")
):
    """Probe an embedding provider endpoint and return its available models."""
    try:
        discovery = await discover_embedding_models(endpoint)
        return JSONResponse(
            content={
                "provider": discovery.provider,
                "endpoint": endpoint,
                "models": discovery.models,
            },
            status_code=200,
        )
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except ModelDiscoveryError as exc:
        logger.warning("Embedding model discovery failed for %s: %s", endpoint, exc)
        raise HTTPException(status_code=502, detail=str(exc)) from exc


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

        local_file_path = None
        if request.local_file_path:
            local_file_path = str(resolve_local_csv_path(request.local_file_path))
        elif not request.blob_container:
            raise HTTPException(status_code=400, detail="Either blob_container or local_file_path is required")

        # Start background processing
        job_id = await csv_processor.process_csv_async(
            upload_id=request.upload_id,
            blob_container=request.blob_container,
            metadata=request.metadata,
            local_file_path=local_file_path,
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
            file_path = resolve_local_csv_path(request.local_file_path)
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

    # Get port from environment or use default
    port = int(os.getenv("PORT", 8100))

    # Run the FastAPI app
    config = uvicorn.Config(app, host="0.0.0.0", port=port, reload=False, log_level="info")
    uvicorn_server = uvicorn.Server(config)
    uvicorn_server.run()
