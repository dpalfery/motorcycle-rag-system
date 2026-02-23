# Quickstart: Fabric Ingestion Pipeline

This quickstart is for local validation and environment setup. Do not store secrets in files; use environment variables.

## Prerequisites

- .NET SDK 10.0 (as pinned by `global.json`)
- Azure Subscription (for Blob Storage and Fabric Capacity)
- Microsoft Fabric Tenant Access

## Microsoft Fabric Setup

### Step 1: Enable Fabric (Tenant Level)
1. Navigate to the Power BI Admin Portal (`app.powerbi.com` -> Settings icon -> Admin portal).
2. Go to **Tenant settings** -> **Microsoft Fabric**.
3. Toggle **Users can create Fabric items** to **Enabled**.

### Step 2: Deploy Fabric Capacity (Manual via Azure Portal)
1. Log into the Azure Portal (`portal.azure.com`).
2. Search for **Microsoft Fabric** and click **Create Fabric Capacity**.
3. Fill out the details:
   - **Subscription & Resource Group**: `rg-motorcyclerag-fabric`
   - **Capacity Name**: `mcrag-fabric`
   - **Size**: Select an F-SKU (e.g., F2 for dev; F64 required for Copilot features).
   - **Capacity Administrator**: Add your account.
4. Click **Review + Create** and wait for provisioning to finish.

### Step 3: Create Workspace
1. Go to `app.fabric.microsoft.com` -> **Workspaces** -> **New workspace**.
2. Name it `MotorcycleRAG-Data-Prod`.
3. Under **Advanced**, select **Fabric capacity** and choose the capacity created in Step 2.

## Graph RAG Setup (SQL Server)

Run the SQL migration script to create the `GraphNodes` and `GraphEdges` tables in your SQL Server database. This enables the Dapper-based traversal engine.

```sql
-- Create the NODE table
CREATE TABLE GraphNodes (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Name NVARCHAR(255) NOT NULL,
    Type NVARCHAR(100) NOT NULL,
    Description NVARCHAR(MAX),
    SourceDocumentId UNIQUEIDENTIFIER
) AS NODE;

-- Create the EDGE table
CREATE TABLE GraphEdges (
    RelationshipType NVARCHAR(100) NOT NULL,
    Weight FLOAT DEFAULT 1.0,
    Context NVARCHAR(MAX)
) AS EDGE;
```

## Run Applications

### 1. API Backend
```powershell
dotnet run --project 1-Presentation/MotorcycleRAG.API
```

### 2. MAUI Admin App
```powershell
dotnet run --project 1-Presentation/MotorcycleRAG.Admin -f net10.0-windows10.0.19041.0
```

## Validation (Manual)

1) Open the MAUI Admin app on your Windows machine.
2) Navigate to the "Ingestion" page.
3) Upload a CSV dataset or PDF Manual.
4) Verify the file is uploaded to Azure Blob / OneLake and the Fabric pipeline is triggered via the API.
5) Check status using the Admin app job tracker UI.

## What to verify

- Large manuals (800 to 2000 pages) stream successfully without API memory limits.
- Fabric Pipeline triggers correctly via REST API calls.
- Graph RAG extracts relationships to the SQL Database (Nodes/Edges).
- Manual page retrieval is protected by entitlement (FR-005c).
