# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]
**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

The feature enables content administrators to ingest motorcycle service manuals (PDFs) and specification datasets (CSVs) via the .NET MAUI Admin App. The MAUI app will securely upload these files to Azure Blob Storage / OneLake and trigger a Microsoft Fabric pipeline via the .NET API. The Fabric pipeline chunks the documents and ingests them into a dual-engine Graph RAG system (Azure AI Search for vectors + SQL Server Graph for entity relationships) to support highly accurate, cross-linked maintenance queries with page-level citations.

## Technical Context

**Language/Version**: C# 13, .NET 10.0  
**Primary Dependencies**: Microsoft Agent Framework, Azure OpenAI, Azure AI Search, Azure Blob Storage, Microsoft Fabric REST API, .NET MAUI  
**Storage**: SQL Server 2017+ (Graph tables via Dapper), Azure Blob Storage / OneLake, Azure AI Search  
**Testing**: xUnit, Moq, Integration tests with TestContainers/InMemory  
**Target Platform**: Windows 11 (MAUI Admin App), Azure App Service (API), Microsoft Fabric (Pipelines)  
**Project Type**: Multi-layer .NET backend API with MAUI Desktop Admin App  
**Performance Goals**: <= 6 hours for 800-2000 page manual ingestion end-to-end  
**Constraints**: Monthly cloud operating cost <= $50/month (use trial capacities where possible)  
**Scale/Scope**: 800-2000 page manuals, thousands of specs, heavy Graph RAG linkage  

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Security (I)**: The .NET API will use `DefaultAzureCredential` (no hardcoded secrets). The API will enforce the `mcr-api-admin` policy for all ingestion endpoints. File paths will not be passed from the client; instead, an opaque blob storage URI will be used.
- **Clean Architecture (II)**: MAUI app depends only on the API. API controllers delegate to Application use cases (`IngestDocumentCommand`). Fabric REST API and SQL Graph (`Dapper`) implementations remain isolated in the `4-Persistence` layer.
- **Code Quality (III)**: Code will strictly adhere to one class per file, zero warnings, and fully asynchronous I/O across the MAUI app and API.
- **Testing (IV)**: Unit and integration tests will cover the Fabric API client, SQL Graph repository, and the MAUI ViewModel upload flow.
- **Observability (V)**: Structured logging will be used. The API will track `IngestionJob` status and return Job IDs to the MAUI app for progress tracking.
- **Resilience (VI)**: The MAUI app will use resilient HTTP clients (Polly) for large file uploads. Fabric API calls will have timeouts and retries.
- **Process (VII)**: Fabric capacity and workspace deployment will be performed manually via the UI. This is an explicit exception to the IaC (Pulumi) rule requested by the user for educational/preview purposes. Step-by-step instructions are provided.

## Project Structure

### Documentation (this feature)

```text
specs/001-fabric-ingestion-pipeline/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
1-Presentation/
├── MotorcycleRAG.API/
│   └── Controllers/
│       └── IngestionJobsController.cs
├── MotorcycleRAG.Admin/ (MAUI App)
│   ├── ViewModels/
│   │   └── IngestionViewModel.cs
│   └── Views/
│       └── IngestionPage.xaml

2-Application/
├── MotorcycleRAG.Application/
│   └── Pipeline/
│       ├── Commands/
│       │   └── StartFabricIngestionCommand.cs
│       └── Interfaces/
│           └── IFabricPipelineService.cs

3-Domain/
├── MotorcycleRAG.Domain/
│   └── Entities/
│       ├── GraphNode.cs
│       └── GraphEdge.cs
└── MotorcycleRAG.Contracts/
    └── Repositories/
        └── IGraphRepository.cs

4-Persistence/
└── MotorcycleRAG.Persistence/
    ├── ExternalServices/
    │   └── FabricPipelineService.cs
    └── Sql/
        └── Repositories/
            └── SqlGraphRepository.cs
```

**Structure Decision**: The solution extends the existing Clean Architecture layout. The MAUI Admin app (Presentation) is updated with ingestion views/viewmodels. The Application layer orchestrates the Fabric pipeline triggers, and the Persistence layer handles the Fabric REST API calls and SQL Graph/Dapper implementations.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
