# Implementation Plan: Motorcycle RAG System Baseline

**Branch**: `001-system-spec` | **Date**: 2026-01-01 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-system-spec/spec.md`

## Summary

The Motorcycle RAG System is a multi-agent RAG (Retrieval-Augmented Generation) system built on Azure AI Foundry platform for intelligent motorcycle information retrieval. It uses a sequential search pattern across heterogeneous data sources including CSV specifications, PDF manuals, and trusted web sources. The system implements Clean Architecture with .NET 10.0, Microsoft Agent Framework for agent orchestration, and Azure AI services for search, document processing, and AI models.

**Primary Requirement**: Provide a unified, intelligent search experience that returns accurate, well-sourced answers to motorcycle-related questions with traceable citations.

**Technical Approach**:
- Multi-agent architecture with specialized search agents (QueryPlanner, VectorSearch, WebSearch)
- Hybrid vector/keyword search on indexed motorcycle data
- Trusted web search augmentation for comprehensive results
- PDF document processing for technical manuals and specifications
- RESTful API with Swagger documentation
- Windows-first MAUI admin application
- React Web UI with BFF
- Production-ready deployment using Azure Container Apps
- Monitoring and telemetry via Application Insights

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: ASP.NET Core Web API, .NET MAUI, Azure AI Foundry (Azure OpenAI, Azure AI Search, Azure Document Intelligence), Microsoft Agent Framework, .NET MAUI Community Toolkit (`CommunityToolkit.Maui`), Polly, Serilog
**Storage**: Azure AI Search (vector store), Azure SQL Database (user/usage/metadata), Azure App Configuration (MCP config), Azure Key Vault (secrets)
**Testing**: xUnit, Moq, Playwright (E2E UI tests)
**Target Platform**:
- Backend: Azure Container Apps (Linux)
- Admin UI: Windows 10+ (primary)
- Mobile UI: Android 21+, iOS 15+, Windows 10+
- Web UI: Modern browsers (React SPA)
**Project Type**: Multi-tier web application with native mobile/desktop clients
**Performance Goals**:
- API endpoints < 500ms (95th percentile)
- Database queries < 100ms average
- UI interactions < 100ms perceived latency
**Constraints**:
- OWASP ASVS v5.0.0 Level 2 compliance
- Zero build warnings
- No secrets in source control
- Clean Architecture layer separation
- Domain-independent transport DTOs live in `3-Domain/MotorcycleRAG.Contracts.Models` (Presentation/Application should not use `MotorcycleRAG.Domain.DTOs` for request/response contracts)
**Scale/Scope**:
- 10k+ users (Free/Plus/Pro plans)
- 1M+ documents indexed
- 50+ MAUI screens/pages

### MAUI UI Standardization

**.NET MAUI Community Toolkit** (`CommunityToolkit.Maui`) is the standardized MAUI toolkit dependency for UI behaviors and helper views.

**Architecture Reference**: All MAUI development MUST adhere to the guidelines in [6-Docs/MAUI_ARCHITECT.md](../../../6-Docs/MAUI_ARCHITECT.md). This document defines the "Golden Path" architecture for the Motorcycle RAG System's .NET MAUI applications, synthesizing Microsoft's Enterprise Application Patterns, the .NET MAUI Community Toolkit best practices, and the specific requirements of the Motorcycle RAG System.

- **Admin Application** (`MotorcycleRAG.Admin`): Windows-first admin interface for data ingress, web source management, and MCP tool configuration
- **Mobile Application** (`MotorcycleRAG.MobileApp`): Cross-platform user-facing app for querying, chat, and profile management

**Navigation Shell Design**:
- **Left Menu Navigation**: Flyout-based navigation menu for primary application sections
- **Top Header**: Shell `TitleView` using built-in MAUI controls (title left; profile/login actions right)
- **Responsive Design**: Collapsible flyout on mobile, always-visible on desktop

**Core Patterns**:
- **MVVM**: Strict Model-View-ViewModel pattern using `CommunityToolkit.Mvvm` source generators.
- **Dependency Injection**: All services and ViewModels must be registered in `MauiProgram.cs`.
- **Navigation**: Shell-first approach wrapped in an `INavigationService` for testability.
- **Resilience**: Mandatory use of `Microsoft.Extensions.Http.Resilience` for robust API interactions.
- **Configuration & Settings**: Abstraction via `ISettingsService` to support unit testing.
- **Authentication**: OIDC/OAuth2 with PKCE using MSAL.NET, targeting Entra ID (Admin) and Entra External ID (Mobile).
- **UI Standards**: Use of built-in controls and Community Toolkit helpers, with centralized theming.
- **Unit Testing**: Strategy focused on ViewModels and Services, mocking dependencies.

**Specific Requirements for Motorcycle RAG**:
- **Local Processing (Admin App)**: PDF/CSV chunking using `PdfPig` / `CsvHelper`, embeddings using `ONNX Runtime`.
- **Tooling (Admin App)**: MCP Configuration managed via `ISettingsService` and synced with API.
- **Offline Support (Mobile App)**: Chat History cached locally (SQLite), sync with server when connection is restored.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Constitution Principles Compliance

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Security (NON-NEGOTIABLE)** | ✅ Compliant | Secrets via env vars/KeyVault; input validation; HTTPS enforced; authorization required |
| **II. Clean Architecture** | ✅ Compliant | Layered structure (0-Base through 8-Agent-Instructions); dependencies flow inward only |
| **III. Code Quality** | ✅ Compliant | Zero warnings policy; async patterns; single responsibility |
| **IV. Testing** | ✅ Compliant | Unit/integration/E2E tests required; meaningful coverage target |
| **V. Observability** | ✅ Compliant | Structured logging; Application Insights; health checks |
| **VI. Resilience** | ✅ Compliant | Polly retry/circuit breaker; graceful degradation; rate limiting; timeouts |
| **VII. Process & Workflow** | ✅ Compliant | Task tracking; code review checklist; PowerShell scripts |

### Pre-Commit Requirements Verification

- [ ] Build passes with **ZERO warnings**
- [ ] **ALL tests pass** (unit, integration, E2E)
- [ ] No secrets in source control
- [ ] Files in correct numbered layer structure
- [ ] Dependencies flow inward only
- [ ] Input validation implemented
- [ ] Unit tests cover new functionality
- [ ] Documentation updated

## Phase 0: Research

See [research.md](./research.md) for findings.

## Phase 1: Design

See [data-model.md](./data-model.md), [contracts/](./contracts/), and [quickstart.md](./quickstart.md).

## Phase 2: Implementation Planning

See [tasks.md](./tasks.md) for the detailed implementation task list organized by user story and phase.
