# FastAPI Application for Motorcycle RAG Local Processing Service

from fastapi import FastAPI, UploadFile, File, HTTPException, BackgroundTasks
from fastapi.responses import JSONResponse
from fastapi.middleware.cors import CORSMiddleware
import uvicorn
import os
import sys
import logging
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


# Health check endpoint
@app.get("/health")
async def health_check():
    """Check service health and dependencies"""
    try:
        # Test Ollama connectivity
        ollama_status = await embedder.check_ollama_status()

        return JSONResponse(
            content={
                "status": "healthy",
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
    uvicorn.run("main:app", host="0.0.0.0", port=port, reload=True, log_level="info")
