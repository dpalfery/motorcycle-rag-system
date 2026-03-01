# Phase 1 Data Model: Fabric Ingestion Pipeline

This data model describes entities and relationships implied by `specs/001-fabric-ingestion-pipeline/spec.md` and required to support job tracking, coverage reporting (FR-010a), idempotency, per-page viewable assets (FR-005a/FR-005b), and Graph RAG relationship mapping.

## Core Entities

### IngestionJob

Purpose: a durable record of a pipeline run, its status, and its outputs.

Key fields:
- `IngestionJobId` (GUID)
- `CreatedAtUtc`, `StartedAtUtc`, `CompletedAtUtc`
- `CreatedBySubject` (string; subject id or user id)
- `Status` (queued/running/succeeded/failed/cancelled)
- `FailureReason` (string, nullable)
- `InputType` (manual-pdf/spec-dataset)
- `InputRef` (opaque upload reference; not a filesystem path)
- `ComputeProvider` (fabric; reserved for future: local)
- `FabricRunId` (string, nullable)
- `ManualDocumentId` (nullable; set for PDF runs)
- `Metrics` (counts/durations; can be normalized or stored as JSON)

Required outputs for FR-010a:
- `TotalPages` (int, nullable)
- `PagesCapturedViewableCount` (int, nullable)
- `PagesWithSearchableTextCount` (int, nullable)
- `MissingPages` (list<int> or JSON array; nullable)

### ManualDocument

Purpose: the manual as a first-class source document with identity/provenance.

Key fields:
- `ManualDocumentId` (GUID)
- `Title` (string)
- `SourceFileName` (string; store original name but avoid logging it)
- `Language` (string)
- `UploadedAtUtc`
- `ContentHash` (string; used for idempotency/dedup)
- `TotalPages` (int)
- `AppliesTo` (many-to-many to `BikeModel`)

### ManualPageAsset

Purpose: per-page viewable artifact and associated searchable text for retrieval.

Key fields:
- `ManualPageAssetId` (GUID)
- `ManualDocumentId` (GUID)
- `PageNumber` (int; 1-based)
- `AssetType` (image/png, image/jpeg, application/pdf-page, etc.)
- `BlobKey` (string; storage key)
- `ByteLength` (long)
- `CreatedAtUtc`

Optional fields (useful for FR-010a):
- `HasSearchableText` (bool)
- `TextBlobKey` (string, nullable)
- `OcrConfidence` (float, nullable)
- `ExtractionStatus` (pending/succeeded/failed)
- `FailureReason` (string, nullable)

### ManualSection

Purpose: a structured searchable chunk/section with stable ID and citations.

Key fields:
- `ManualSectionId` (GUID or stable string)
- `ManualDocumentId` (GUID)
- `SectionPath` (string; chapter/section headings)
- `PageStart`, `PageEnd` (int)
- `ChunkIndex` (int)
- `Content` (string; may be stored in search index only)
- `Citation` (page/path reference)

### BikeModel

Purpose: canonical bike model record.

Key fields:
- `BikeModelId` (GUID)
- `Make`, `Model`, `Year` (or year range)
- `Aliases` (collection)

Relationship:
- Many-to-many with `ManualDocument` (FR-008a)

## Graph RAG Entities (SQL Server Graph)

### GraphNode

Purpose: Represents a recognized entity extracted from manuals or CSV datasets.
Key fields:
- `Id` (GUID, Primary Key)
- `Name` (string) e.g., "Yamaha R1", "Engine Oil"
- `Type` (string) e.g., "Motorcycle", "Component", "Spec"
- `Description` (string)
- `SourceDocumentId` (GUID, links to ManualDocument)

### GraphEdge

Purpose: Represents the semantic relationship between two GraphNodes.
Key fields:
- `RelationshipType` (string) e.g., "HAS_PART", "MANUFACTURED_BY", "REQUIRES_MAINTENANCE"
- `Weight` (float) Confidence score from the LLM extraction
- `Context` (string) The specific sentence/text where this relationship was found

## Relationships

- `IngestionJob (0..1) -> ManualDocument (1)` for manual PDF jobs.
- `ManualDocument (1) -> ManualPageAsset (many)`.
- `ManualDocument (1) -> ManualSection (many)`.
- `ManualDocument (many) <-> BikeModel (many)`.
- `GraphNode (many) <-> GraphEdge (many) -> GraphNode (many)`.

## Coverage Reporting (FR-010a)

Definitions used for metrics:
- "Page captured as viewable" = a `ManualPageAsset` exists for that page and its blob is readable.
- "Page with searchable text" = per-page extracted text exists and meets a minimum threshold; for scanned pages this is OCR output.
- Missing pages list = pages in `[1..TotalPages]` without a viewable asset.

Recommended implementation detail:
- Maintain a per-page processing status record (table or JSON) to support resumability (FR-015) and accurate reporting.
