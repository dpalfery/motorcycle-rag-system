# Tasks: Motorcycle RAG System Baseline

**Input**: Design documents from `specs/001-system-spec/`

**Prerequisites**: plan.md (required), spec.md (required), data-model.md, contracts/, research.md, quickstart.md

**Tests**: Required by the feature spec (User Scenarios & Testing). Include/extend unit + integration tests per user story.

**MAUI Architecture**: All MAUI work MUST follow the golden path in `6-Docs/MAUI_ARCHITECT.md` (MVVM via `CommunityToolkit.Mvvm`, Shell-first navigation via `INavigationService`, DI registration in `MauiProgram.cs`, resilience via `Microsoft.Extensions.Http.Resilience`, and settings via `ISettingsService`).

**Transport DTOs**: Canonical request/response models live in `3-Domain/MotorcycleRAG.Contracts.Models` and MUST remain domain-independent. Some older task descriptions reference `3-Domain/MotorcycleRAG.Domain/DTOs` as a historical location.

---

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 Validate quickstart steps in specs/001-system-spec/quickstart.md
- [x] T002 Add ASVS L2 evidence checklist skeleton in specs/001-system-spec/checklists/asvs-v5-level2.md
- [x] T003 [P] Confirm OpenAPI reflects current contract in specs/001-system-spec/contracts/openapi.yaml
- [x] T004 Update agent context via .specify/scripts/powershell/update-agent-context.ps1
- [ ] T005 Update quickstart secrets guidance to forbid .env + connection strings in specs/001-system-spec/quickstart.md

---

## Phase 2: Foundational (Blocking Prerequisites)

- [x] T006 Define core repository/service interfaces in 3-Domain/MotorcycleRAG.Contracts/Interfaces/
- [x] T007 Define core DTOs for query contracts in 3-Domain/MotorcycleRAG.Domain/DTOs/
- [x] T008 Create SQL schema script for core entities in 4-Persistence/MotorcycleRAG.Persistence/Sql/schema.sql
- [x] T009 Implement SQL connection factory in 4-Persistence/MotorcycleRAG.Persistence/Sql/SqlConnectionFactory.cs
- [x] T010 Implement SQL repositories in 4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/
- [x] T011 Wire Persistence + Application services into API DI in 1-Presentation/MotorcycleRAG.API/Configuration/ServiceConfiguration.cs
- [x] T012 Implement ProblemDetails + exception handling middleware in 1-Presentation/MotorcycleRAG.API/Program.cs
- [x] T013 Implement correlation ID propagation in 1-Presentation/MotorcycleRAG.API/Program.cs
- [x] T014 Implement rate limiting policy for public endpoints in 1-Presentation/MotorcycleRAG.API/Program.cs
- [x] T015 Implement security headers baseline in 1-Presentation/MotorcycleRAG.API/Program.cs
- [x] T016 Implement JWT bearer auth (B2C + Entra ID issuers) in 1-Presentation/MotorcycleRAG.API/Program.cs
- [x] T017 Implement admin authorization policies (Entra app roles) in 1-Presentation/MotorcycleRAG.API/Program.cs
- [x] T018 Implement telemetry/log redaction foundations in 4-Persistence/MotorcycleRAG.Persistence/Telemetry/TelemetryService.cs
- [x] T019 Harden BFF cookie/redirect handling in 1-Presentation/MotorcycleRag.WebUI.BFF/Program.cs

---

## Phase 3: User Story 1 — Ask a motorcycle question (Priority: P1) 🎯 MVP

**Independent Test**: `POST /api/motorcycles/query` returns answer + sources, and returns a “no results” response when appropriate.

- [x] T020 [P] [US1] Extend unit tests for verification/citations in 5-Test/tests/MotorcycleRAG.UnitTests/Services/ModelValidationServiceTests.cs
- [x] T021 [P] [US1] Extend unit tests for answer composition + citations in 5-Test/tests/MotorcycleRAG.UnitTests/Services/MotorcycleRAGServiceTests.cs
- [x] T022 [US1] Extend integration tests for query endpoint in 5-Test/tests/MotorcycleRAG.IntegrationTests/Api/MotorcycleApiIntegrationTests.cs
- [x] T023 [P] [US1] Align request/response DTOs (queryId/metrics/citations) in 3-Domain/MotorcycleRAG.Domain/DTOs/MotorcycleQueryRequest.cs
- [x] T024 [P] [US1] Align request/response DTOs (queryId/metrics/citations) in 3-Domain/MotorcycleRAG.Domain/DTOs/MotorcycleQueryResponse.cs
- [x] T025 [P] [US1] Ensure query telemetry fields exist in 3-Domain/MotorcycleRAG.Domain/DTOs/QueryMetrics.cs
- [x] T026 [US1] Implement retrieval orchestration (index → web → pdf fallback) in 2-Application/MotorcycleRAG.Application/Services/AgentOrchestrator.cs
- [x] T027 [US1] Implement answer composition + citations in 2-Application/MotorcycleRAG.Application/Services/MotorcycleRAGService.cs
- [x] T028 [US1] Implement claim verification rules in 2-Application/MotorcycleRAG.Application/Services/ModelValidationService.cs
- [x] T029 [US1] Implement query controller response mapping in 1-Presentation/MotorcycleRAG.API/Controllers/MotorcycleController.cs

---

## Phase 4: User Story 1a — Sign in and manage profile (Priority: P1)

**Independent Test**: Sign in; `GET /api/me` and `GET /api/me/usage` work; exceeding plan limit blocks requests.

- [x] T030 [P] [US1a] Add unit tests for plan limits in 5-Test/tests/MotorcycleRAG.UnitTests/Services/PlanPolicyServiceTests.cs
- [x] T031 [P] [US1a] Add unit tests for usage tracking in 5-Test/tests/MotorcycleRAG.UnitTests/Services/UsageTrackingServiceTests.cs
- [x] T032 [US1a] Add integration tests for profile/usage endpoints in 5-Test/tests/MotorcycleRAG.IntegrationTests/Api/MeApiIntegrationTests.cs
- [x] T033 [US1a] Add integration tests for admin user mgmt + plan assignment in 5-Test/tests/MotorcycleRAG.IntegrationTests/Api/AdminUserManagementIntegrationTests.cs
- [x] T034 [US1a] Define current user abstraction in 3-Domain/MotorcycleRAG.Contracts/Interfaces/ICurrentUserService.cs
- [x] T035 [US1a] Implement current user resolution from claims in 1-Presentation/MotorcycleRAG.API/Services/CurrentUserService.cs
- [x] T036 [US1a] Implement user provisioning/update-on-login in 2-Application/MotorcycleRAG.Application/Services/UserProvisioningService.cs
- [x] T037 [US1a] Implement plan SKU + daily limits in 2-Application/MotorcycleRAG.Application/Services/PlanPolicyService.cs
- [x] T038 [US1a] Implement usage tracking service in 2-Application/MotorcycleRAG.Application/Services/UsageTrackingService.cs
- [x] T039 [US1a] Enforce per-user daily request limits in 1-Presentation/MotorcycleRAG.API/Controllers/MotorcycleController.cs
- [x] T040 [US1a] Implement profile + usage endpoints in 1-Presentation/MotorcycleRAG.API/Controllers/MeController.cs
- [x] T041 [US1a] Implement admin user management endpoints in 1-Presentation/MotorcycleRAG.API/Controllers/UsersAdminController.cs
- [x] T042 [US1a] Implement admin plan endpoints in 1-Presentation/MotorcycleRAG.API/Controllers/PlansAdminController.cs
- [x] T043 [US1a] Implement user admin application service (enable/disable/assign plan) in 2-Application/MotorcycleRAG.Application/Services/UserAdminService.cs
- [x] T044 [US1a] Implement plan persistence in 4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/PlanRepository.cs

---

## Phase 5: User Story 2 — Ingest structured specifications (Priority: P2)

**Independent Test**: Upload CSV, process it, check status/metrics/cancel/scheduled endpoints.

- [x] T045 [P] [US2] Extend unit tests for pipeline orchestration in 5-Test/tests/MotorcycleRAG.UnitTests/Pipeline/DataPipelineOrchestratorTests.cs
- [x] T046 [US2] Extend integration tests for pipeline endpoints in 5-Test/tests/MotorcycleRAG.IntegrationTests/Pipeline/DataPipelineIntegrationTests.cs
- [x] T047 [US2] Implement upload/processing endpoints in 1-Presentation/MotorcycleRAG.API/Controllers/DataPipelineController.cs
- [x] T048 [US2] Implement ingestion job persistence in 4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/IngestionJobRepository.cs
- [x] T049 [US2] Implement pipeline monitoring service in 2-Application/MotorcycleRAG.Application/Pipeline/PipelineMonitoringService.cs
- [x] T050 [US2] Implement pipeline orchestration/cancellation in 2-Application/MotorcycleRAG.Application/Pipeline/DataPipelineOrchestrator.cs
- [x] T051 [US2] Implement scheduled pipeline service in 2-Application/MotorcycleRAG.Application/Pipeline/ScheduledPipelineService.cs
- [x] T052 [US2] Implement indexing write path for structured specs in 4-Persistence/MotorcycleRAG.Persistence/Search/MotorcycleIndexingService.cs

---

## Phase 6: User Story 3 — Search maintenance manuals (Priority: P2)

**Independent Test**: Ingest a PDF manual; query returns citations with page/section when available.

- [x] T053 [P] [US3] Extend PDF processor unit tests in 5-Test/tests/MotorcycleRAG.UnitTests/DataProcessing/MotorcyclePDFProcessorTests.cs
- [x] T054 [US3] Add integration tests for manual citation behavior in 5-Test/tests/MotorcycleRAG.IntegrationTests/PdfProcessing/MotorcycleManualCitationComponentTests.cs
- [x] T055 [US3] Implement PDF processing + locator extraction in 4-Persistence/MotorcycleRAG.Persistence/DataProcessing/MotorcyclePDFProcessor.cs
- [x] T056 [US3] Index manual chunks with locator metadata in 4-Persistence/MotorcycleRAG.Persistence/Search/MotorcycleIndexingService.cs
- [x] T057 [US3] Ensure response includes manual citation locators in 2-Application/MotorcycleRAG.Application/Services/MotorcycleRAGService.cs

---

## Phase 7: User Story 3a — Admin UI for data ingress (Priority: P2)

**Independent Test**: Admin app can sign-in, upload PDF/CSV, monitor jobs, and cancel a job.

- [x] T058 [US3a] Ensure Admin project scaffold builds in 1-Presentation/MotorcycleRAG.Admin/MotorcycleRAG.Admin.csproj
- [x] T059 [US3a] Implement Entra ID admin sign-in service in 1-Presentation/MotorcycleRAG.Admin/Services/AdminAuthService.cs
- [x] T060 [US3a] Implement Admin API client wrapper in 1-Presentation/MotorcycleRAG.Admin/Services/ApiClient.cs
- [x] T061 [P] [US3a] Implement PDF chunker in 1-Presentation/MotorcycleRAG.Admin/Processing/PdfChunker.cs
- [x] T062 [P] [US3a] Implement CSV chunker in 1-Presentation/MotorcycleRAG.Admin/Processing/CsvChunker.cs
- [x] T063 [US3a] Implement optional ONNX embedding service in 1-Presentation/MotorcycleRAG.Admin/Processing/OnnxEmbeddingService.cs
- [x] T064 [US3a] Implement ingestion workflow VM in 1-Presentation/MotorcycleRAG.Admin/ViewModels/IngestionViewModel.cs
- [x] T065 [US3a] Implement upload UI page in 1-Presentation/MotorcycleRAG.Admin/Pages/UploadPage.xaml
- [x] T066 [US3a] Implement jobs UI page in 1-Presentation/MotorcycleRAG.Admin/Pages/JobsPage.xaml
- [x] T067 [US3a] Refactor Admin navigation to Shell Flyout + TitleView in 1-Presentation/MotorcycleRAG.Admin/AppShell.xaml
- [x] T068 [US3a] Add INavigationService wrapper for Shell navigation in 1-Presentation/MotorcycleRAG.Admin/Services/INavigationService.cs
- [x] T069 [US3a] Add ISettingsService wrapping Preferences/SecureStorage in 1-Presentation/MotorcycleRAG.Admin/Services/ISettingsService.cs
- [x] T070 [US3a] Register Admin services/ViewModels/pages in DI in 1-Presentation/MotorcycleRAG.Admin/MauiProgram.cs
- [x] T071 [US3a] Add resilience handler for Admin HttpClient in 1-Presentation/MotorcycleRAG.Admin/MauiProgram.cs
- [x] T072 [US3a] Add connectivity checks for network operations in 1-Presentation/MotorcycleRAG.Admin/ViewModels/IngestionViewModel.cs
- [x] T073 [US3a] Add compiled bindings (x:DataType) for Admin pages in 1-Presentation/MotorcycleRAG.Admin/Pages/UploadPage.xaml
- [x] T074 [US3a] Add compiled bindings (x:DataType) for Admin pages in 1-Presentation/MotorcycleRAG.Admin/Pages/JobsPage.xaml
- [x] T075 [US3a] Add Admin theming tokens in 1-Presentation/MotorcycleRAG.Admin/App.xaml

---

## Phase 8: User Story 6 — Add websites for indexing (Priority: P2) ✅ COMPLETE

**Independent Test**: Admin can add/remove a web source; a scrape/index run adds searchable content; query returns URL citations.

- [x] T076 [P] [US6] Define web trust policy models (tier + allowlist) in 3-Domain/MotorcycleRAG.Domain/Entities/WebTrustPolicy.cs
- [x] T077 [US6] Implement web source registry service in 2-Application/MotorcycleRAG.Application/Services/WebSourceRegistryService.cs
- [x] T078 [US6] Implement web source admin controller in 1-Presentation/MotorcycleRAG.API/Controllers/WebSourcesAdminController.cs
- [x] T079 [US6] Implement scrape/index orchestrator in 2-Application/MotorcycleRAG.Application/Services/WebScrapeOrchestrator.cs
- [x] T080 [US6] Persist scrape/index run outcomes in 4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/WebScrapeRunRepository.cs
- [x] T081 [US6] Persist/serve trusted-domain policy in 4-Persistence/MotorcycleRAG.Persistence/Configuration/WebTrustPolicyStore.cs
- [x] T082 [US6] Enforce allowlist + trust tiers in 2-Application/MotorcycleRAG.Application/Agents/WebSearchAgent.cs
- [x] T083 [US6] Add/extend tests for web trust filtering in 5-Test/tests/MotorcycleRAG.UnitTests/Agents/WebSearchAgentTests.cs
- [x] T084 [US6] Add Admin Web Sources page wired to API in 1-Presentation/MotorcycleRAG.Admin/Pages/WebSourcesPage.xaml

---

## Phase 9: User Story 7 — Configure MCP tools in the MAUI admin application (Priority: P3) ✅ COMPLETE

**Independent Test**: Admin updates tool config; enabled tool set changes are visible to orchestration and audited.

- [x] T085 [P] [US7] Define MCP config domain models in 3-Domain/MotorcycleRAG.Domain/Entities/McpToolConfiguration.cs
- [x] T086 [US7] Implement MCP config store (AppConfig) in 4-Persistence/MotorcycleRAG.Persistence/Configuration/McpConfigurationStore.cs
- [x] T087 [US7] Implement MCP config provider with refresh behavior in 2-Application/MotorcycleRAG.Application/Services/McpConfigurationProvider.cs
- [x] T088 [US7] Implement MCP audit persistence in 4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/ToolConfigurationAuditRepository.cs
- [x] T089 [US7] Implement MCP admin controller in 1-Presentation/MotorcycleRAG.API/Controllers/McpAdminController.cs
- [x] T090 [US7] Wire orchestration to consume enabled MCP tools in 2-Application/MotorcycleRAG.Application/Services/AgentOrchestrator.cs
- [x] T091 [US7] Add Admin Tools page in 1-Presentation/MotorcycleRAG.Admin/Pages/ToolsPage.xaml
- [x] T092 [US7] Add ToolsViewModel with validation in 1-Presentation/MotorcycleRAG.Admin/ViewModels/ToolsViewModel.cs
- [x] T093 [P] [US7] Add unit tests for config provider refresh behavior in 5-Test/tests/MotorcycleRAG.UnitTests/Configuration/McpConfigurationProviderTests.cs

---

## Phase 10: User Story 4 — Operate reliably under failures (Priority: P3)

**Independent Test**: Simulate failures in Search/OpenAI/Web; system returns partial results + clear limitation messaging.

- [x] T094 [US4] Implement resilience service policies in 4-Persistence/MotorcycleRAG.Persistence/Resilience/ResilienceService.cs
- [x] T095 [US4] Add resilience unit tests in 5-Test/tests/MotorcycleRAG.UnitTests/Resilience/ResilienceServiceTests.cs
- [x] T096 [US4] Implement partial-results aggregation in 2-Application/MotorcycleRAG.Application/Services/AgentOrchestrator.cs
- [x] T097 [US4] Ensure limitation messaging surfaced in 2-Application/MotorcycleRAG.Application/Services/MotorcycleRAGService.cs
- [x] T098 [US4] Track degraded-mode telemetry consistently in 4-Persistence/MotorcycleRAG.Persistence/Telemetry/TelemetryService.cs

---

## Phase 11: User Story 5 — Keep user data secure (Priority: P3)

**Independent Test**: No secrets or connection strings in repo/docs; logs redact query text; admin endpoints require authorization.

- [x] T099 [US5] Remove .env workflow and connection string examples from specs/001-system-spec/quickstart.md
- [x] T100 [US5] Add/confirm secrets are read from environment only (no fallbacks) in 1-Presentation/MotorcycleRAG.API/Program.cs
- [x] T101 [US5] Ensure query logging uses redaction/correlation IDs in 4-Persistence/MotorcycleRAG.Persistence/Telemetry/TelemetryService.cs
- [x] T102 [US5] Add audit logging application service in 2-Application/MotorcycleRAG.Application/Services/AuditService.cs
- [x] T103 [US5] Add /health dependency checks in 1-Presentation/MotorcycleRAG.API/Program.cs
- [x] T104 [US5] Add integration test for /health contract in 5-Test/tests/MotorcycleRAG.IntegrationTests/Api/HealthIntegrationTests.cs
- [x] T105 [US5] Update ASVS evidence to reference implemented controls in specs/001-system-spec/checklists/asvs-v5-level2.md

---

## Phase 12: Polish & Cross-Cutting Concerns

- [x] T106 Align plan/spec terminology for citations + queryId in specs/001-system-spec/spec.md
- [x] T107 Validate OpenAPI contract consistency in specs/001-system-spec/contracts/openapi.yaml
- [x] T108 Validate quickstart end-to-end steps in specs/001-system-spec/quickstart.md
- [x] T109 Run dotnet build for MotorcycleRAG.sln in MotorcycleRAG.sln
- [x] T110 Run dotnet test for MotorcycleRAG.sln in MotorcycleRAG.sln
- [x] T111 Run npm build for WebUI in 1-Presentation/MotorcycleRag.WebUI/package.json
- [x] T112 Document deployment config keys in 6-Docs/deployment.md
- [ ] T113 Audit remaining references to `MotorcycleRAG.Domain.DTOs` in Presentation/Application and migrate them to `MotorcycleRAG.Contracts.Models`

---

## Dependencies & Execution Order

- Setup → Foundational → US1/US1a (P1)
- Foundational → US2/US3 (P2)
- US2 + US3 → US3a (P2)
- Foundational → US6 (P2)
- Foundational → US7 (P3)
- Foundational → US4 (P3)
- Foundational → US5 (P3)

## Parallel Execution Examples

- US1: T023/T024/T025 can be parallelized across different DTO files.
- US2: pipeline unit tests (T045) can run in parallel with controller work (T047).
- US3a: Shell refactor (T067) can be parallel with settings/nav services (T068/T069).