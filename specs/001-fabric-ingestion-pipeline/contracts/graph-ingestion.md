# Graph RAG & Fabric Ingestion API Contracts

## `POST /api/ingestion/jobs/upload`

**Purpose**: Called by the .NET MAUI App to stream large PDFs or CSV files directly to Azure Blob Storage / OneLake. Uses `MultipartReader` instead of `IFormFile` to bypass memory limitations on large manuals (800-2000 pages).

**Request**:
- Content-Type: `multipart/form-data`
- Body: 
  - `file`: `[binary data]`
  - `documentType`: `manual-pdf | spec-dataset`

**Response (`202 Accepted`)**:
```json
{
  "uploadId": "opaque-blob-uri-or-guid",
  "fileName": "Yamaha_R1_Service_Manual.pdf",
  "documentType": "manual-pdf",
  "status": "pending-ingestion"
}
```

## `POST /api/ingestion/jobs`

**Purpose**: Triggers the Microsoft Fabric data pipeline, passing the `uploadId` as a parameter. It does not accept a physical file path for security reasons.

**Request (`application/json`)**:
```json
{
  "uploadId": "opaque-blob-uri-or-guid",
  "documentType": "manual-pdf",
  "configuration": {
    "extractGraphRelationships": true,
    "ocrEnabled": true
  }
}
```

**Response (`202 Accepted`)**:
```json
{
  "jobId": "b1b2f3d4...",
  "status": "Running",
  "fabricRunId": "fabric-pipeline-guid-1234",
  "statusUrl": "/api/ingestion/jobs/b1b2f3d4..."
}
```

## `GET /api/ingestion/jobs/{jobId}`

**Purpose**: Polled by the .NET MAUI Admin App to display progress of the Fabric pipeline execution.

**Response (`200 OK`)**:
```json
{
  "jobId": "b1b2f3d4...",
  "status": "Succeeded",
  "metrics": {
    "totalPages": 850,
    "pagesCapturedViewableCount": 845,
    "pagesWithSearchableTextCount": 840,
    "missingPages": [ 12, 13, 14, 50, 51 ]
  },
  "graphExtraction": {
    "nodesCreated": 1500,
    "edgesCreated": 2300
  }
}
```

## `GET /api/manuals/{manualId}/pages/{pageNumber}`

**Purpose**: Fetches a viewable asset (page) of a manual. Only available to users with the `mcr-api-manuals-view` entitlement.

**Response (`200 OK`)**:
- Content-Type: `image/png` or `application/pdf`
- Body: `[binary data streamed from Azure Blob Storage]`