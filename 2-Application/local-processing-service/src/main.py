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
from embeddings.ollama_embedder import OllamaEmbedder
from extraction.graph_extractor import GraphExtractor
from storage.blob_writer import BlobWriter
from models.schemas import (
    ProcessPDFRequest,
    ProcessCSVRequest,
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
