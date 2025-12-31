# Motorcycle RAG System - Project Context

## Project Overview
This project is a sophisticated **multi-agent RAG (Retrieval-Augmented Generation) system** for motorcycle information retrieval. It is designed to pass **OWASP ASVS Level 2** security standards and follows strict **Clean Architecture** principles.

The system orchestrates specialized agents to search heterogeneous data sources (CSV specs, PDF manuals, Trusted Web) and provides a unified, cited response. It includes a **React WebUI** for users and a **.NET MAUI Admin App** (Windows-first) for data ingestion and management.

## Authoritative Documentation
*   **`AGENTS.md`**: Primary source for Architectural Rules (Folder Structure) and Security Directives.
*   **`specs/001-system-spec/`**: Detailed functional requirements (`spec.md`), remediation plans (`plan.md`), and task tracking (`tasks.md`).

## Technology Stack

### Backend
*   **Framework**: .NET 10.0 (ASP.NET Core Web API)
*   **Language**: C# 13
*   **Architecture**: Clean Architecture + DDD (8 Layers)
*   **AI/RAG**: Semantic Kernel, Azure OpenAI (GPT-4o, text-embedding-3-large), Azure AI Search.
*   **Data**: SQL Server (Dapper), Azure Blob Storage.
*   **Identity**: Microsoft Entra External ID / B2C (OIDC).

### Frontend
*   **User UI**: React 19 + Vite + Tailwind CSS (`1-Presentation/MotorcycleRag.WebUI`).
*   **Admin UI**: .NET MAUI for Windows (`1-Presentation/MotorcycleRAG.Admin`).
*   **BFF**: Backend-for-Frontend pattern using YARP/ASP.NET Core (`1-Presentation/MotorcycleRag.WebUI.BFF`).

## Architecture & Folder Rules (`AGENTS.md`)

The project strictly follows these layers. Dependencies must point **inward** (Presentation -> Application -> Domain <- Persistence).

1.  **0-Base** (`MotorcycleRAG.Shared`): Cross-cutting concerns, shared config, utilities.
2.  **1-Presentation**: API Controllers, WebUI, Admin App. Entry points only.
3.  **2-Application**: Use cases, orchestration, agents, interfaces for infrastructure.
4.  **3-Domain**: Pure business logic, entities, and repository interfaces. **No infrastructure dependencies.**
5.  **4-Persistence**: Implementation of interfaces, Azure SDKs, DB context.
6.  **5-Test**: Unit (`.UnitTests`), Integration (`.IntegrationTests`), and UI tests.
7.  **6-Docs**: Documentation.
8.  **7-Deployment**: IaC (Pulumi), Dockerfiles.

## Security Rules (Mandatory)

*   **Secrets**: NEVER store secrets in code/config files. Use Environment Variables or Key Vault.
*   **Validation**: Sanitize all inputs. Use parameterized SQL queries ONLY.
*   **Authorization**: Explicitly authorize every action (e.g., `[Authorize(Policy = "DataAdmin")]`).
*   **Logging**: Redact sensitive data (PII, query text) from logs.

## Key Features & Requirements (`spec.md`)

*   **Multi-Agent Search**: Query Planner -> Vector Search -> Web Search (Trusted Sources only) -> PDF Search (with page/section citations).
*   **Trust Tiers**: Tier A (OEM Manuals) > Tier B (Reputable Media) > Tier C (Community).
*   **Ingestion Pipeline**: Batch processing of CSVs and PDFs with status tracking.
*   **MCP Support**: Model Context Protocol integration for extensible tools (managed via Admin App).
*   **User Plans**: Free (10 req/day), Plus (100 req/day), Pro (Unlimited).

## Development Status (`tasks.md`)

*   **Done**: Core Auth (Entra ID), SQL Persistence, User Story 1 (Ask Question), User Story 1a (Profiles/Limits).
*   **In Progress/Pending**: PDF Manual Search (US3), MAUI Admin App (US3a), Web Source Management (US6), MCP Config (US7).

## Usage Commands

### Backend & API
```powershell
# Run API
dotnet run --project 1-Presentation/MotorcycleRAG.API

# Run Tests (Unit + Integration)
dotnet test
```

### Frontend (User)
```powershell
cd 1-Presentation/MotorcycleRag.WebUI
npm run dev
```

### Data Ingestion
*   **API**: `POST /api/DataPipeline/upload` (Files), `POST /api/DataPipeline/process` (Trigger).
*   **Admin App**: Launch `1-Presentation/MotorcycleRAG.Admin` (pending implementation).