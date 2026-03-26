from typing import Dict, Any, Optional, List
from pydantic import BaseModel, Field
from datetime import datetime


class Metadata(BaseModel):
    make: Optional[str] = Field(None, description="Motorcycle make/manufacturer")
    model: Optional[str] = Field(None, description="Motorcycle model")
    year: Optional[int] = Field(None, description="Model year")
    document_type: Optional[str] = Field(
        None, description="Type of document (e.g., ServiceManual)"
    )
    language: Optional[str] = Field("en", description="Document language")

    # Additional custom metadata
    custom: Dict[str, Any] = Field(
        default_factory=dict, description="Additional metadata fields"
    )


class ProcessPDFRequest(BaseModel):
    upload_id: str = Field(
        ..., description="Unique identifier for the uploaded file in Azure Blob Storage"
    )
    document_type: str = Field(
        ...,
        description='Type of document being processed (e.g., "manual-pdf", "spec-dataset")',
    )
    blob_container: str = Field(..., description="Azure Blob Storage container name")
    metadata: Metadata = Field(
        default_factory=Metadata, description="Document metadata"
    )


class ProcessCSVRequest(BaseModel):
    upload_id: str = Field(
        ..., description="Unique identifier for the uploaded CSV file"
    )
    blob_container: str = Field(..., description="Azure Blob Storage container name")
    metadata: Metadata = Field(
        default_factory=Metadata, description="Document metadata"
    )


class ProcessBikeGraphRequest(BaseModel):
    upload_id: str = Field(
        ..., description="UUID used as the import batch identifier and sourceDocumentId for all created nodes"
    )
    blob_container: Optional[str] = Field(
        None, description="Azure Blob Storage container that holds the source CSV as <upload_id>.csv"
    )
    local_file_path: Optional[str] = Field(
        None, description="Absolute path to the motorcycle spec CSV file on the local machine"
    )


class Chunk(BaseModel):
    id: str = Field(..., description="Unique chunk identifier")
    title: str = Field(..., description="Chunk title")
    content: str = Field(..., description="Chunk content/text")
    document_type: str = Field(
        ..., description="Type of document this chunk belongs to"
    )
    make: Optional[str] = Field(None, description="Motorcycle make")
    model: Optional[str] = Field(None, description="Motorcycle model")
    year: Optional[int] = Field(None, description="Model year")
    source_file: str = Field(..., description="Original source file name")
    section: str = Field(..., description="Document section this chunk belongs to")
    page_number: int = Field(..., description="Page number in original document")
    page_range: str = Field(..., description='Page range (e.g., "5-6")')
    primary_section: str = Field(..., description="Primary document section")
    section_level: int = Field(..., description="Hierarchy level of section")
    section_headings: List[str] = Field(
        default_factory=list, description="Full section heading path"
    )
    table_caption: Optional[str] = Field(
        None, description="Table caption if this chunk is from a table"
    )
    chunk_index: int = Field(..., description="Index of this chunk in the document")
    tags: List[str] = Field(default_factory=list, description="Metadata tags")
    content_vector: List[float] = Field(
        default_factory=list, description="Embedding vector"
    )
    created_at: datetime = Field(
        default_factory=datetime.utcnow, description="Creation timestamp"
    )
    updated_at: datetime = Field(
        default_factory=datetime.utcnow, description="Last update timestamp"
    )


class GraphEntity(BaseModel):
    id: str = Field(..., description="Unique entity identifier")
    name: str = Field(..., description="Entity name/display name")
    entity_type: str = Field(
        ..., description="Type of entity (Procedure, Component, Motorcycle, etc.)"
    )
    description: Optional[str] = Field(None, description="Entity description")
    source_document_id: Optional[str] = Field(None, description="Source document ID")
    relationships: List[Dict[str, Any]] = Field(
        default_factory=list, description="Extracted relationships to other entities"
    )
    confidence: float = Field(1.0, description="Extraction confidence score")
    extracted_at: datetime = Field(
        default_factory=datetime.utcnow, description="Extraction timestamp"
    )


class ProcessingStatusResponse(BaseModel):
    job_id: str = Field(..., description="Unique job identifier")
    status: str = Field(
        ...,
        description="Current processing status (queued, processing, completed, failed)",
    )
    message: str = Field(..., description="Status message")
    progress: float = Field(0.0, description="Processing progress (0.0 - 1.0)")

    # Optional fields for completed jobs
    chunks_processed: Optional[int] = Field(
        None, description="Number of chunks processed"
    )
    pages_processed: Optional[int] = Field(
        None, description="Number of pages processed"
    )
    total_pages: Optional[int] = Field(None, description="Total pages in document")
    completion_time: Optional[datetime] = Field(
        None, description="Completion timestamp"
    )
    error: Optional[str] = Field(None, description="Error message if failed")

    # Output locations for completed jobs
    chunks_blob_url: Optional[str] = Field(
        None, description="URL to chunks JSON Lines file in blob storage"
    )
    graph_entities_blob_url: Optional[str] = Field(
        None, description="URL to graph entities JSON file in blob storage"
    )


class HealthResponse(BaseModel):
    status: str = Field(..., description="Service health status")
    services: Dict[str, Any] = Field(
        default_factory=dict, description="Status of individual services"
    )


class JobStatus(BaseModel):
    job_id: str = Field(..., description="Unique job identifier")
    status: str = Field(..., description="Current processing status")
    message: str = Field(..., description="Status message")
    progress: float = Field(0.0, description="Processing progress")
    created_at: datetime = Field(
        default_factory=datetime.utcnow, description="Job creation timestamp"
    )
    updated_at: datetime = Field(
        default_factory=datetime.utcnow, description="Last update timestamp"
    )

    # Optional fields
    completion_time: Optional[datetime] = Field(
        None, description="Completion timestamp"
    )
    error: Optional[str] = Field(None, description="Error message if failed")
    chunks_processed: Optional[int] = Field(
        None, description="Number of chunks processed"
    )
    pages_processed: Optional[int] = Field(
        None, description="Number of pages processed"
    )
