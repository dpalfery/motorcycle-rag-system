# Implementation Plan: Motorcycle RAG System Baseline

**Branch**: `001-system-spec` | **Date**: 2025-12-31 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-system-spec/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

The Motorcycle RAG System is a multi-agent RAG (Retrieval-Augmented Generation) system built on Azure AI Foundry platform for intelligent motorcycle information retrieval. It uses a sequential search pattern across heterogeneous data sources including CSV specifications, PDF manuals, and web sources. The system implements clean architecture with .NET 10.0, Semantic Kernel for agent orchestration, and Azure AI services for search, document processing, and AI models.

**Primary Requirement**: Provide a unified, intelligent search experience that returns accurate, well-sourced answers to motorcycle-related questions with traceable citations.

**Technical Approach**: 
- Multi-agent architecture with specialized search agents (QueryPlanner, VectorSearch, WebSearch)
- Hybrid vector/keyword search on indexed motorcycle data
- Real-time web search augmentation for comprehensive results
- PDF document processing for technical manuals and specifications
- RESTful API with comprehensive Swagger documentation
- Windows-first MAUI admin application with Material.Components.Maui UI components
- Cross-platform MAUI mobile app with Material.Components.Maui UI components
- Production-ready deployment using Azure Container Apps
- Comprehensive monitoring and telemetry via Application Insights

## Technical Context

**Language/Version**: C# 13 / .NET 10.0  
**Primary Dependencies**: ASP.NET Core Web API, .NET MAUI, Azure AI Foundry (OpenAI, AI Search, Document Intelligence), Semantic Kernel, Material.Components.Maui, Polly, Serilog  
**Storage**: Azure AI Search (vector store), Azure SQL Database (user/usage/metadata), Azure App Configuration (MCP config), Azure Key Vault (secrets)  
**Testing**: xUnit, Moq, Playwright (E2E UI tests)  
**Target Platform**: 
- Backend: Azure Container Apps (Linux)
- Admin UI: Windows 10+ (primary), macOS, Linux (secondary)
- Mobile UI: Android 21+, iOS 15+, Windows 10+
- Web UI: Modern browsers (React SPA)
**Project Type**: Multi-tier web application with native mobile/desktop clients  
**Performance Goals**: 
- API endpoints < 500ms (95th percentile)
- Database queries < 100ms average
- Real-time operations < 100ms
- UI interactions < 100ms perceived latency
**Constraints**: 
- OWASP ASVS v5.0.0 Level 2 compliance
- Zero build warnings
- No secrets in source control
- Clean Architecture layer separation
- 80% meaningful test coverage
**Scale/Scope**: 
- 10k+ users (Free/Plus/Pro plans)
- 1M+ documents indexed
- 50+ MAUI screens/pages
- Multi-region Azure deployment

### UI Framework Standardization

**Material.Components.Maui** is the standardized UI component library for all MAUI applications in this project:

- **Admin Application** (`MotorcycleRAG.Admin`): Windows-first admin interface for data ingress, web source management, and MCP tool configuration
- **Mobile Application** (`MotorcycleRAG.MobileApp`): Cross-platform user-facing app for querying, chat, and profile management

**Navigation Shell Design**:
- **Left Menu Navigation**: Flyout-based navigation menu for primary application sections
- **Top Header**: Application title, search (where applicable), and profile/login area on the right
- **Profile/Login**: Top-right corner with user avatar, name, and sign-out action
- **Responsive Design**: Collapsible flyout on mobile, always-visible on desktop

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Constitution Principles Compliance

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Security (NON-NEGOTIABLE)** | ✅ Compliant | Secrets via env vars/KeyVault; input validation; HTTPS enforced; authorization required |
| **II. Clean Architecture** | ✅ Compliant | Layered structure (0-Base through 7-Deployment); dependencies flow downward |
| **III. Code Quality** | ✅ Compliant | Zero warnings policy; no placeholder code; async patterns; single responsibility |
| **IV. Testing** | ✅ Compliant | 80% meaningful coverage target; unit/integration/E2E tests; Red-Green-Refactor |
| **V. Observability** | ✅ Compliant | Structured logging; Application Insights; health checks; metrics/tracing |
| **VI. Resilience** | ✅ Compliant | Polly retry/circuit breaker; graceful degradation; rate limiting; timeouts |
| **VII. Process & Workflow** | ✅ Compliant | Task tracking; code review checklist; commit discipline; PowerShell scripts |

### Pre-Commit Requirements Verification

- [x] Build passes with **ZERO warnings**
- [x] **ALL tests pass** (unit, integration, E2E)
- [x] No secrets in source control
- [x] Files in correct numbered layer structure
- [x] Dependencies flow downward only
- [x] Input validation implemented
- [x] Unit tests cover new functionality
- [x] Documentation updated

## Project Structure

### Documentation (this feature)

```text
specs/001-system-spec/
├── spec.md              # Feature specification (user stories, requirements, success criteria)
├── plan.md              # This file (implementation plan)
├── research.md          # Phase 0 output (research findings)
├── data-model.md        # Phase 1 output (domain data models)
├── quickstart.md        # Phase 1 output (developer quickstart guide)
├── contracts/           # Phase 1 output (API contracts, schemas)
│   ├── openapi.yaml     # OpenAPI 3.1 specification
│   └── schemas/        # JSON Schema definitions
├── checklists/          # Compliance checklists
│   └── asvs-v5-level2.md  # OWASP ASVS Level 2 evidence
└── tasks.md             # Phase 2 output (implementation tasks)
```

### Source Code (repository root)

```text
# Clean Architecture Layered Structure

0-Base/
└── MotorcycleRAG.Core/           # Shared kernel (utilities, exceptions, results)

1-Presentation/
├── MotorcycleRAG.API/             # ASP.NET Core Web API (backend)
│   ├── Controllers/               # API endpoints
│   ├── Middleware/                # Exception handling, security headers, auth
│   ├── Configuration/             # DI setup, service configuration
│   └── Services/                 # Current user resolution
├── MotorcycleRAG.Admin/          # .NET MAUI Admin App (Windows-first)
│   ├── Pages/                    # Admin pages (Upload, Jobs, Tools, WebSources)
│   ├── ViewModels/               # MVVM view models
│   ├── Services/                 # API client, auth, processing
│   ├── Processing/               # Local chunking, CSV/PDF parsing, ONNX embeddings
│   ├── Resources/                # Fonts, images, styles, raw assets
│   └── AppShell.xaml             # Navigation shell (flyout + top header)
├── MotorcycleRAG.MobileApp/      # .NET MAUI Mobile App (cross-platform)
│   ├── Views/                    # User pages (Chat, Auth, Profile, Conversations)
│   ├── ViewModels/               # MVVM view models
│   ├── Services/                 # API client, auth, storage, PDF viewer
│   ├── Persistence/              # Local SQLite database
│   ├── Resources/                # Fonts, images, styles
│   └── AppShell.xaml             # Navigation shell (flyout + top header)
├── MotorcycleRag.WebUI/          # React SPA (web client)
│   ├── src/
│   │   ├── components/           # React components
│   │   ├── pages/                # Page components
│   │   ├── contexts/             # React contexts (Auth)
│   │   └── lib/                  # Utilities
│   └── package.json
└── MotorcycleRag.WebUI.BFF/     # ASP.NET Core BFF for React SPA
    ├── Controllers/               # Auth endpoints
    └── wwwroot/                  # Static React build output

2-Application/
└── MotorcycleRAG.Application/     # Business logic, use cases, agents
    ├── Features/                 # CQRS features (Commands, Queries)
    │   ├── Documents/            # Document indexing
    │   ├── Motorcycles/          # Query orchestration
    │   └── Users/                # User management
    ├── Services/                 # Application services
    │   ├── AgentOrchestrator.cs  # Multi-agent coordination
    │   ├── MotorcycleRAGService.cs # Main query service
    │   ├── ModelValidationService.cs # Claim verification
    │   ├── PlanPolicyService.cs   # SKU/limit enforcement
    │   ├── UsageTrackingService.cs # Usage tracking
    │   └── *AdminServices.cs     # Admin operations
    ├── Agents/                   # Semantic Kernel agents
    │   ├── QueryPlannerAgent.cs
    │   ├── VectorSearchAgent.cs
    │   └── WebSearchAgent.cs
    ├── Authorization/             # Authorization handlers
    └── Interfaces/               # External service interfaces

3-Domain/
├── MotorcycleRAG.Domain/          # Core domain entities, value objects
│   ├── Entities/                 # Aggregate roots (User, Document, IngestionJob)
│   ├── ValueObjects/              # Immutable types (SearchQuery, Embedding)
│   ├── Services/                 # Domain services
│   ├── Events/                   # Domain events
│   └── Models/                   # Domain models (QueryModels, UserModels, etc.)
└── MotorcycleRAG.Contracts/       # Domain-owned interfaces
    ├── Repositories/             # Repository interfaces
    ├── Services/                 # External service interfaces
    └── DTOs/                    # Data transfer objects

4-Persistence/
└── MotorcycleRAG.Persistence/     # Infrastructure implementations
    ├── Azure/                    # Azure service wrappers
    │   ├── AzureOpenAIClientWrapper.cs
    │   ├── AzureSearchClientWrapper.cs
    │   └── DocumentIntelligenceClientWrapper.cs
    ├── Sql/                      # SQL persistence
    │   ├── Repositories/          # ADO.NET repositories
    │   ├── SqlConnectionFactory.cs
    │   └── schema.sql
    ├── Search/                   # Vector search operations
    │   └── MotorcycleIndexingService.cs
    ├── DataProcessing/            # Document processing
    │   ├── MotorcycleCSVProcessor.cs
    │   └── MotorcyclePDFProcessor.cs
    ├── Configuration/             # Configuration stores
    │   ├── McpConfigurationStore.cs
    │   └── WebTrustPolicyStore.cs
    ├── Resilience/               # Polly policies
    │   ├── ResilienceService.cs
    │   └── CorrelationService.cs
    └── Telemetry/                # Application Insights
        └── TelemetryService.cs

5-Test/
├── MotorcycleRAG.Domain.Tests/   # Domain unit tests
├── MotorcycleRAG.Application.Tests/ # Application unit tests
├── MotorcycleRAG.Api.Tests/     # API integration tests
├── MotorcycleRAG.IntegrationTests/ # Full integration tests
├── MotorcycleRAG.MobileApp.Tests/ # Mobile app tests
└── MotorcycleRAG.Playwright-UI.Tests/ # E2E UI tests

6-Docs/                           # Documentation
├── architecture-general.md        # Clean architecture guide
├── adr/                          # Architecture Decision Records
├── diagrams/                     # System diagrams
└── deployment.md                  # Deployment guide

7-Deployment/                      # Infrastructure as Code
├── infrastructure/               # Pulumi or Bicep/Terraform
├── docker/                       # Dockerfiles
├── scripts/                      # Deployment scripts
└── .github/workflows/             # CI/CD pipelines

8-Agent-Instructions/              # AI agent guidance
```

**Structure Decision**: This structure follows Clean Architecture principles with clear layer separation. The numbered folders (0-Base through 8-Agent-Instructions) enforce the dependency rule: dependencies flow inward only. All MAUI applications (Admin and MobileApp) reside in 1-Presentation layer as delivery mechanisms. The API project provides the backend REST interface. All business logic is in 2-Application, domain models in 3-Domain, and infrastructure in 4-Persistence.

## Complexity Tracking

> **No Constitution violations requiring justification. All complexity is justified by the multi-agent RAG architecture, clean architecture requirements, and cross-platform UI needs.**

## Phase 0: Research

### Research Areas Identified

1. **Material.Components.Maui Integration**
   - Package version compatibility with .NET MAUI 10.0
   - Available components (Flyout, TopAppBar, Navigation, Cards, Buttons, Inputs)
   - Theming and styling capabilities
   - Platform-specific considerations

2. **Navigation Shell Architecture**
   - MAUI Shell + Flyout pattern for left menu navigation
   - TopAppBar integration for header with profile/login
   - Responsive design patterns (desktop vs mobile)
   - State management for authentication state

3. **Local Processing Requirements**
   - ONNX Runtime for local embeddings on Windows
   - PDF chunking libraries compatible with MAUI
   - CSV parsing libraries
   - Performance considerations for local processing

4. **Azure AI Foundry Integration**
   - Azure OpenAI service wrapper patterns
   - Azure AI Search hybrid vector/keyword search
   - Azure Document Intelligence for PDF OCR
   - Semantic Kernel agent orchestration

5. **Authentication & Authorization**
   - Microsoft Entra External ID / B2C for users
   - Microsoft Entra ID (workforce) for admins
   - MSAL integration for MAUI apps
   - Token management and refresh flows

6. **Multi-Agent RAG Architecture**
   - Semantic Kernel agent framework patterns
   - Sequential agent orchestration
   - Claim extraction and verification
   - Citation generation and formatting

### Research Findings

See [`research.md`](./research.md) for detailed findings.

## Phase 1: Design

### Design Documents

See [`data-model.md`](./data-model.md) for domain data models and [`quickstart.md`](./quickstart.md) for developer setup guide.

### API Contracts

See [`contracts/`](./contracts/) directory for OpenAPI specification and JSON schemas.

## Phase 2: Implementation Planning

See [`tasks.md`](./tasks.md) for the detailed implementation task list organized by user story and phase.

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0 (Research)**: No dependencies. Must complete before Phase 1.
- **Phase 1 (Design)**: Depends on Phase 0. Must complete before Phase 2.
- **Phase 2 (Implementation)**: Depends on Phase 1. Organized by user story priority.

### User Story Completion Order

1. **P1 (Critical)**: User Story 1 (Ask a motorcycle question), User Story 1a (Sign in and manage profile)
2. **P2 (Important)**: User Story 2 (Ingest structured specifications), User Story 3 (Search maintenance manuals), User Story 3a (Admin UI), User Story 6 (Add websites for indexing)
3. **P3 (Nice-to-have)**: User Story 7 (Configure MCP tools), User Story 4 (Operate reliably under failures), User Story 5 (Keep user data secure)

### UI Standardization / Material.Components.Maui Adoption Execution Order

**Phase 0: UI Standardization / Material.Components.Maui Adoption** occurs immediately before building new UI pages so that new pages start with the Material Design standard. This phase must be completed prior to implementing any MAUI page creation tasks in User Stories 3a (Admin UI), 6 (Web Sources), and 7 (MCP Configuration) to ensure consistent UI components across the application.

## Implementation Strategy

### MVP First

1. Phase 0: Research (Material.Components.Maui, navigation patterns, Azure AI services)
2. Phase 1: Design (data models, contracts, quickstart)
3. Phase 2: Implementation
   - Foundational infrastructure (auth, persistence, error handling, telemetry)
   - User Story 1: Core query functionality
   - User Story 1a: Authentication and profile management
4. Validate MVP: End-to-end query with citations

### Incremental Delivery

- Add ingestion capabilities (US2), then manual search (US3), then admin UI (US3a)
- Add web sources (US6), then MCP configuration (US7)
- Add reliability features (US4), then security hardening (US5)

### UI Framework Standardization

**Material.Components.Maui** will be adopted for both MAUI applications:

1. **Package Installation**: Add `Material.Components.Maui` NuGet package to both projects
2. **Navigation Shell**: Implement unified navigation pattern (Flyout + TopAppBar)
3. **Component Migration**: Gradually migrate existing pages to use Material components
4. **Theming**: Establish consistent Material Design 3 theming across apps
5. **Responsive Design**: Ensure flyout behavior adapts to screen size

## Constitution Compliance Marker

**[Constitution: Compliant]**

All 7 constitution principles are satisfied:
- ✅ Security: Secrets via secure sources, input validation, HTTPS, authorization
- ✅ Clean Architecture: Layered structure, downward dependencies
- ✅ Code Quality: Zero warnings, no placeholders, async patterns
- ✅ Testing: 80% meaningful coverage, unit/integration/E2E tests
- ✅ Observability: Structured logging, Application Insights, health checks
- ✅ Resilience: Polly policies, graceful degradation, rate limiting
- ✅ Process & Workflow: Task tracking, code review checklist, commit discipline
