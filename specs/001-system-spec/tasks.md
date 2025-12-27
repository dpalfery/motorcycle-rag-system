# Tasks: Motorcycle RAG System Baseline

**Input**: Design documents from `/specs/001-system-spec/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required by the project constitution. Add/extend unit + integration tests per user story; ensure Red-Green-Refactor where feasible.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Ensure the repo is ready for incremental implementation and consistent local execution.

- [X] T001 Validate Quickstart commands on Windows in specs/001-system-spec/quickstart.md
- [X] T002 Add ASVS L2 evidence checklist skeleton in specs/001-system-spec/checklists/asvs-v5-level2.md
- [X] T003 [P] Document local dev env vars for API/BFF in specs/001-system-spec/quickstart.md
- [X] T004 [P] Confirm OpenAPI reflects current contract in specs/001-system-spec/contracts/openapi.yaml
- [X] T005 [P] Add MAUI admin app placeholder section in specs/001-system-spec/quickstart.md
- [X] T006 Update agent context after tasks generation via .specify/scripts/powershell/update-agent-context.ps1 (reference: .github/agents/copilot-instructions.md)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-cutting foundations needed before any user story work (auth, persistence, error handling, telemetry, security baseline).

- [X] T007 Define SQL-backed persistence interfaces in 3-Domain/MotorcycleRAG.Contracts/Interfaces (e.g., IUserRepository.cs, IUsageRepository.cs, IWebSourceRepository.cs, IAuditRepository.cs)
- [X] T008 [P] Add domain models for user/usage/web-source/audit in 3-Domain/MotorcycleRAG.Domain/Models (e.g., UserModels.cs, UsageModels.cs, WebSourceModels.cs)
- [X] T009 Create SQL schema script for core entities in 4-Persistence/MotorcycleRAG.Persistence/Sql/schema.sql
- [X] T010 Implement ADO.NET connection factory/options in 4-Persistence/MotorcycleRAG.Persistence/Sql/SqlConnectionFactory.cs
- [X] T011 Implement ADO.NET repositories in 4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories (Users/Usage/WebSources/Audit)
- [X] T012 Wire SQL persistence into DI in 1-Presentation/MotorcycleRAG.API/Configuration/ServiceConfiguration.cs
- [X] T013 Add SQL and AppConfig options binding + validation in 1-Presentation/MotorcycleRAG.API/Program.cs
- [X] T014 Implement JWT bearer auth for B2C + Entra ID issuers in 1-Presentation/MotorcycleRAG.API/Program.cs
- [X] T015 Add admin authorization policies (Entra app roles) in 1-Presentation/MotorcycleRAG.API/Program.cs
- [X] T016 Add ProblemDetails + exception handling middleware in 1-Presentation/MotorcycleRAG.API/Program.cs
- [X] T017 Add request correlation ID propagation in 1-Presentation/MotorcycleRAG.API/Program.cs
- [X] T018 Add rate limiting policy for public endpoints in 1-Presentation/MotorcycleRAG.API/Program.cs
- [X] T019 Add security headers baseline for API in 1-Presentation/MotorcycleRAG.API/Program.cs
- [X] T020 Add cookie/redirect hardening for BFF OIDC in 1-Presentation/MotorcycleRag.WebUI.BFF/Program.cs
- [X] T021 Implement log redaction strategy for query text in 4-Persistence/MotorcycleRAG.Persistence/Telemetry (e.g., TelemetryService.cs)
- [X] T022 Update ASVS evidence checklist to reference implemented controls in specs/001-system-spec/checklists/asvs-v5-level2.md

**Checkpoint**: API has auth, persistence, ProblemDetails, telemetry, and ASVS-traceable baseline controls.

---

## Phase 3: User Story 1 — Ask a motorcycle question (Priority: P1) 🎯 MVP

**Goal**: Provide a single, clear, cited answer for motorcycle questions.

**Independent Test**: Call `POST /api/motorcycles/query` and verify response includes answer + sources/citations and handles “no results”.

- [X] T103 [P] [US1] Extend unit tests for verification/citations in 5-Test/tests/MotorcycleRAG.UnitTests/Services/ModelValidationServiceTests.cs
- [X] T104 [P] [US1] Extend unit tests for answer composition + citations in 5-Test/tests/MotorcycleRAG.UnitTests/Services/MotorcycleRAGServiceTests.cs
- [X] T105 [US1] Extend integration tests for POST /api/motorcycles/query (no-results + citations) in 5-Test/tests/MotorcycleRAG.IntegrationTests/Api/MotorcycleApiIntegrationTests.cs

- [X] T023 [P] [US1] Align request/response models (claims/citations/metrics/queryId) in 3-Domain/MotorcycleRAG.Domain/Models/QueryModels.cs
- [X] T024 [P] [US1] Add citation locator models (ManualPdf/Website/Dataset) in 3-Domain/MotorcycleRAG.Domain/Models/QueryModels.cs
- [X] T025 [US1] Implement sequential retrieval policy (index → web → pdf fallback) in 2-Application/MotorcycleRAG.Application/Services/AgentOrchestrator.cs
- [X] T026 [US1] Implement claim extraction from evidence in 2-Application/MotorcycleRAG.Application/Services/MotorcycleRAGService.cs
- [X] T027 [US1] Implement independent claim verification against citations in 2-Application/MotorcycleRAG.Application/Services/ModelValidationService.cs
- [X] T028 [US1] Enforce "every factual claim has citation or is qualified/omitted" in 2-Application/MotorcycleRAG.Application/Services/MotorcycleRAGService.cs
- [X] T029 [US1] Add "no results + refine suggestions" behavior in 2-Application/MotorcycleRAG.Application/Services/MotorcycleRAGService.cs
- [X] T030 [US1] Return stable queryId + metrics in 1-Presentation/MotorcycleRAG.API/Controllers/MotorcycleController.cs
- [X] T031 [US1] Ensure response includes source attribution + locators in 1-Presentation/MotorcycleRAG.API/Controllers/MotorcycleController.cs
- [X] T032 [US1] Add input validation rules for query + preferences in 1-Presentation/MotorcycleRAG.API/Controllers/MotorcycleController.cs

---

## Phase 4: User Story 1a — Sign in and manage profile (Priority: P1)

**Goal**: Authenticate users via Entra External ID/B2C and expose profile + plan + usage; enforce plan limits.

**Independent Test**: Sign in via BFF; call `GET /api/me` and `GET /api/me/usage`; confirm query calls are tracked and limited.

- [ ] T033 [US1a] Add “current user” resolver abstraction in 3-Domain/MotorcycleRAG.Contracts/Interfaces/ICurrentUserService.cs
- [ ] T034 [US1a] Implement current user resolution from claims in 2-Application/MotorcycleRAG.Application/Services/CurrentUserService.cs
- [ ] T035 [US1a] Implement user provisioning/update-on-login in 2-Application/MotorcycleRAG.Application/Services/UserProvisioningService.cs
- [ ] T036 [US1a] Implement plan SKU + daily limit rules in 2-Application/MotorcycleRAG.Application/Services/PlanPolicyService.cs
- [ ] T037 [US1a] Implement usage tracking repository calls in 2-Application/MotorcycleRAG.Application/Services/UsageTrackingService.cs
- [ ] T038 [US1a] Enforce per-user daily request limits at query entry in 1-Presentation/MotorcycleRAG.API/Controllers/MotorcycleController.cs
- [ ] T039 [US1a] Create profile endpoint in 1-Presentation/MotorcycleRAG.API/Controllers/MeController.cs
- [ ] T040 [US1a] Create usage endpoint in 1-Presentation/MotorcycleRAG.API/Controllers/MeController.cs
- [ ] T041 [US1a] Add minimal profile update endpoint (allowed fields) in 1-Presentation/MotorcycleRAG.API/Controllers/MeController.cs
- [ ] T042 [US1a] Configure B2C OIDC settings for the BFF in 1-Presentation/MotorcycleRag.WebUI.BFF/appsettings.Development.json
- [ ] T043 [US1a] Propagate access token to API via BFF proxy in 1-Presentation/MotorcycleRag.WebUI.BFF/Program.cs

- [ ] T106 [P] [US1a] Add unit tests for plan limit logic in 5-Test/tests/MotorcycleRAG.UnitTests/Services/PlanPolicyServiceTests.cs
- [ ] T107 [P] [US1a] Add unit tests for usage tracking in 5-Test/tests/MotorcycleRAG.UnitTests/Services/UsageTrackingServiceTests.cs
- [ ] T108 [US1a] Add integration tests for GET /api/me + /api/me/usage + limit exceeded response in 5-Test/tests/MotorcycleRAG.IntegrationTests/Api/MeApiIntegrationTests.cs
- [ ] T120 [US1a] Add admin user management controller (enable/disable) in 1-Presentation/MotorcycleRAG.API/Controllers/UsersAdminController.cs
- [ ] T121 [US1a] Add admin plan assignment endpoint in 1-Presentation/MotorcycleRAG.API/Controllers/PlansAdminController.cs
- [ ] T122 [US1a] Implement user enable/disable + plan assignment services in 2-Application/MotorcycleRAG.Application/Services (UserAdminService.cs, PlanAdminService.cs)
- [ ] T123 [US1a] Add persistence for user state + plan assignment in 4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories (UserRepository.cs, PlanRepository.cs)
- [ ] T124 [US1a] Add integration tests for admin user mgmt + plan assignment in 5-Test/tests/MotorcycleRAG.IntegrationTests/Api/AdminUserManagementIntegrationTests.cs

---

## Phase 5: User Story 2 — Ingest structured specifications (Priority: P2)

**Goal**: Upload/process CSV datasets; track job status/metrics/cancel/schedule.

**Independent Test**: Upload sample CSV, process it, confirm status/metrics available and query returns dataset-derived results.

- [ ] T044 [US2] Implement upload constraints response in 1-Presentation/MotorcycleRAG.API/Controllers/DataPipelineController.cs
- [ ] T045 [US2] Implement batch upload endpoint behavior in 1-Presentation/MotorcycleRAG.API/Controllers/DataPipelineController.cs
- [ ] T046 [US2] Implement batch processing endpoint behavior in 1-Presentation/MotorcycleRAG.API/Controllers/DataPipelineController.cs
- [ ] T047 [US2] Persist ingestion job records (execution/status/metrics/errors) via SQL in 4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/IngestionJobRepository.cs
- [ ] T048 [US2] Wire ingestion job persistence into monitoring service in 2-Application/MotorcycleRAG.Application/Services/PipelineMonitoringService.cs
- [ ] T049 [US2] Implement cancellation behavior in 2-Application/MotorcycleRAG.Application/Services/DataPipelineOrchestrator.cs
- [ ] T050 [US2] Implement scheduled run trigger + stats in 2-Application/MotorcycleRAG.Application/Services/ScheduledPipelineService.cs
- [ ] T051 [US2] Ensure structured spec ingestion writes documents/vectors to Azure AI Search in 4-Persistence/MotorcycleRAG.Persistence/Search/MotorcycleIndexingService.cs
- [ ] T052 [US2] Require Entra ID admin auth + roles for pipeline endpoints in 1-Presentation/MotorcycleRAG.API/Controllers/DataPipelineController.cs

- [ ] T109 [US2] Extend integration coverage for upload/process/status/metrics/cancel/scheduled endpoints in 5-Test/tests/MotorcycleRAG.IntegrationTests/Pipeline/DataPipelineIntegrationTests.cs
- [ ] T110 [P] [US2] Extend unit tests for orchestration/cancellation in 5-Test/tests/MotorcycleRAG.UnitTests/Pipeline/DataPipelineOrchestratorTests.cs

---

## Phase 6: User Story 3 — Search maintenance manuals (Priority: P2)

**Goal**: Ingest manuals and answer with precise references (section/page).

**Independent Test**: Ingest a known manual and query for a procedure; verify response includes page/section references.

- [ ] T053 [US3] Ensure PDF processing extracts page/section metadata in 4-Persistence/MotorcycleRAG.Persistence/DataProcessing/MotorcyclePDFProcessor.cs
- [ ] T054 [US3] Ensure manual chunks preserve structure (tables/sections) in 4-Persistence/MotorcycleRAG.Persistence/DataProcessing/MotorcyclePDFProcessor.cs
- [ ] T055 [US3] Index manual chunks with locator metadata in 4-Persistence/MotorcycleRAG.Persistence/Search/MotorcycleIndexingService.cs
- [ ] T056 [US3] Add manual citation locator mapping into response in 2-Application/MotorcycleRAG.Application/Services/MotorcycleRAGService.cs
- [ ] T057 [US3] Enforce manual citation fields (when available) in 2-Application/MotorcycleRAG.Application/Services/ModelValidationService.cs

- [ ] T111 [P] [US3] Extend PDF processor unit tests for page/section extraction in 5-Test/tests/MotorcycleRAG.UnitTests/DataProcessing/MotorcyclePDFProcessorTests.cs
- [ ] T112 [US3] Add/extend integration test asserting manual locators appear in query response in 5-Test/tests/MotorcycleRAG.IntegrationTests/Api/MotorcycleManualCitationIntegrationTests.cs

---

## Phase 7: User Story 3a — Admin UI for data ingress (Priority: P2)

**Goal**: Provide a Windows-first MAUI app to upload/process PDFs/CSVs and monitor ingestion jobs; support local chunking + vectorization.

**Independent Test**: Use MAUI app to upload PDF/CSV, run local processing, submit artifacts, monitor job status, and verify content is searchable.

- [ ] T058 [US3a] Create MAUI project scaffold in 1-Presentation/MotorcycleRAG.Admin/MotorcycleRAG.Admin.csproj
- [ ] T059 [US3a] Add solution/project references and build configuration in MotorcycleRAG.sln
- [ ] T060 [US3a] Create API client wrapper for pipeline/admin endpoints in 1-Presentation/MotorcycleRAG.Admin/Services/ApiClient.cs
- [ ] T061 [US3a] Implement Entra ID sign-in flow (device code / MSAL) in 1-Presentation/MotorcycleRAG.Admin/Services/AdminAuthService.cs
- [ ] T062 [US3a] Implement role-gated admin navigation in 1-Presentation/MotorcycleRAG.Admin/AppShell.xaml
- [ ] T063 [US3a] Implement file picker + validation (type/size) in 1-Presentation/MotorcycleRAG.Admin/Pages/UploadPage.xaml
- [ ] T064 [US3a] Implement local PDF chunking pipeline in 1-Presentation/MotorcycleRAG.Admin/Processing/PdfChunker.cs
- [ ] T065 [US3a] Implement local CSV parsing/chunking pipeline in 1-Presentation/MotorcycleRAG.Admin/Processing/CsvChunker.cs
- [ ] T066 [US3a] Implement ONNX Runtime embedding generation in 1-Presentation/MotorcycleRAG.Admin/Processing/OnnxEmbeddingService.cs
- [ ] T067 [US3a] Package embedding model as app content in 1-Presentation/MotorcycleRAG.Admin/Resources/Raw/embedding-model.onnx
- [ ] T068 [US3a] Implement “upload artifacts then process” workflow in 1-Presentation/MotorcycleRAG.Admin/ViewModels/IngestionViewModel.cs
- [ ] T069 [US3a] Implement job status polling + display in 1-Presentation/MotorcycleRAG.Admin/Pages/JobsPage.xaml
- [ ] T070 [US3a] Implement cancellation action in 1-Presentation/MotorcycleRAG.Admin/Pages/JobsPage.xaml
- [ ] T071 [US3a] Add operational error reporting UX in 1-Presentation/MotorcycleRAG.Admin/Utilities/ErrorPresenter.cs

---

## Phase 8: User Story 6 — Add websites for indexing (Priority: P2)

**Goal**: Admin can register websites; system scrapes/indexes; retrieval includes web citations.

**Independent Test**: Add a site, run scrape/index job, and query returns results attributed to that URL.

- [ ] T072 [US6] Create admin web sources controller in 1-Presentation/MotorcycleRAG.API/Controllers/WebSourcesAdminController.cs
- [ ] T073 [US6] Implement CRUD for web sources backed by SQL in 2-Application/MotorcycleRAG.Application/Services/WebSourceRegistryService.cs
- [ ] T074 [US6] Implement URL validation + uniqueness checks in 2-Application/MotorcycleRAG.Application/Services/WebSourceRegistryService.cs
- [ ] T075 [US6] Implement scrape/index job runner in 2-Application/MotorcycleRAG.Application/Services/WebScrapeOrchestrator.cs
- [ ] T076 [US6] Persist scrape/index outcomes per website in 4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/WebScrapeRunRepository.cs
- [ ] T077 [US6] Index scraped web content with attribution metadata in 4-Persistence/MotorcycleRAG.Persistence/Search/MotorcycleIndexingService.cs
- [ ] T078 [US6] Add deduplication strategy for near-duplicate web results in 2-Application/MotorcycleRAG.Application/Services/AgentOrchestrator.cs

- [ ] T113 [US6] Extend trust-policy behavior tests in 5-Test/tests/MotorcycleRAG.UnitTests/Agents/WebSearchAgentTests.cs
- [ ] T117 [P] [US6] Define trust policy model (allowlist + trust tier) in 3-Domain/MotorcycleRAG.Domain/Models/WebTrustModels.cs
- [ ] T118 [US6] Enforce allowlist + trust tier filtering in 2-Application/MotorcycleRAG.Application/Agents/WebSearchAgent.cs
- [ ] T119 [US6] Persist/serve trusted-domain configuration (AppConfig/SQL) in 4-Persistence/MotorcycleRAG.Persistence/Configuration/WebTrustPolicyStore.cs

---

## Phase 9: User Story 7 — Configure MCP tools in the MAUI admin application (Priority: P3)

**Goal**: Admin can manage MCP servers/tools and enable/disable without redeploy; changes are auditable.

**Independent Test**: Create tool config, enable it, verify availability in orchestration for new runs, and audit trail records change.

- [ ] T079 [US7] Add MCP config domain models in 3-Domain/MotorcycleRAG.Domain/Models/McpModels.cs
- [ ] T080 [US7] Implement MCP configuration store using Azure App Configuration in 4-Persistence/MotorcycleRAG.Persistence/Configuration/McpConfigurationStore.cs
- [ ] T081 [US7] Implement MCP config provider with refresh/sentinel behavior in 2-Application/MotorcycleRAG.Application/Services/McpConfigurationProvider.cs
- [ ] T082 [US7] Implement audit trail persistence for MCP changes in 4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/ToolConfigurationAuditRepository.cs
- [ ] T083 [US7] Create MCP admin endpoints in 1-Presentation/MotorcycleRAG.API/Controllers/McpAdminController.cs
- [ ] T084 [US7] Wire orchestration to consume enabled tools for new runs in 2-Application/MotorcycleRAG.Application/Services/AgentOrchestrator.cs
- [ ] T085 [US7] Add MAUI tool configuration screens in 1-Presentation/MotorcycleRAG.Admin/Pages/ToolsPage.xaml
- [ ] T086 [US7] Implement MAUI tool editor + validation in 1-Presentation/MotorcycleRAG.Admin/ViewModels/ToolsViewModel.cs

- [ ] T114 [P] [US7] Add unit tests for MCP config provider refresh/sentinel in 5-Test/tests/MotorcycleRAG.UnitTests/Configuration/McpConfigurationProviderTests.cs

---

## Phase 10: User Story 4 — Operate reliably under failures (Priority: P3)

**Goal**: Return best-effort partial answers when one or more sources are unavailable.

**Independent Test**: Simulate outages (Search/OpenAI/Web) and confirm partial results + clear limitation messaging.

- [ ] T087 [US4] Ensure Polly policies cover all external calls in 4-Persistence/MotorcycleRAG.Persistence/Resilience/ResilienceService.cs
- [ ] T088 [US4] Implement partial-results aggregation when agents fail in 2-Application/MotorcycleRAG.Application/Services/AgentOrchestrator.cs
- [ ] T089 [US4] Ensure user-facing limitation messaging in 2-Application/MotorcycleRAG.Application/Services/MotorcycleRAGService.cs
- [ ] T090 [US4] Track degraded-mode telemetry in 4-Persistence/MotorcycleRAG.Persistence/Telemetry/TelemetryService.cs

- [ ] T115 [US4] Extend resilience tests for partial-outage aggregation behavior in 5-Test/tests/MotorcycleRAG.UnitTests/Resilience/ResilienceServiceTests.cs

---

## Phase 11: User Story 5 — Keep user data secure (Priority: P3)

**Goal**: Protect secrets and user query data; comply with ASVS L2 expectations.

**Independent Test**: Verify secrets not in repo; logs contain correlation IDs but not raw sensitive query text; authZ enforced for admin actions.

- [ ] T091 [US5] Ensure secrets are loaded via env/AppConfig/KeyVault only in 1-Presentation/MotorcycleRAG.API/Program.cs
- [ ] T092 [US5] Implement log sanitization/redaction for query text in 2-Application/MotorcycleRAG.Application/Services/MotorcycleRAGService.cs
- [ ] T093 [US5] Enforce authorization on admin controllers in 1-Presentation/MotorcycleRAG.API/Controllers/DataPipelineController.cs (and new controllers WebSourcesAdminController.cs, McpAdminController.cs)
- [ ] T094 [US5] Add audit logging for admin actions in 2-Application/MotorcycleRAG.Application/Services/AuditService.cs
- [ ] T095 [US5] Map implemented controls to ASVS evidence in specs/001-system-spec/checklists/asvs-v5-level2.md

- [ ] T116 [US5] Extend telemetry/log redaction tests in 5-Test/tests/MotorcycleRAG.UnitTests/Telemetry/TelemetryServiceTests.cs
- [ ] T125 [US5] Verify /health includes dependency checks (Search/OpenAI/DocIntelligence) in 1-Presentation/MotorcycleRAG.API/Program.cs
- [ ] T126 [US5] Add integration test for /health contract in 5-Test/tests/MotorcycleRAG.IntegrationTests/Api/HealthIntegrationTests.cs
- [ ] T127 [US5] Define/validate telemetry event names + required properties (queryId, duration, degradedMode) in 4-Persistence/MotorcycleRAG.Persistence/Telemetry/TelemetryService.cs
- [ ] T128 [US5] Extend telemetry tests for event/property coverage in 5-Test/tests/MotorcycleRAG.UnitTests/Telemetry/TelemetryServiceTests.cs

---

## Phase 12: Polish & Cross-Cutting Concerns

**Purpose**: Documentation consistency, operational readiness, and final validation.

- [ ] T096 Align plan/spec/contracts terminology (queryId, metrics, citations) in specs/001-system-spec/spec.md
- [ ] T097 Validate OpenAPI contract consistency in specs/001-system-spec/contracts/openapi.yaml
- [ ] T098 Validate quickstart end-to-end steps in specs/001-system-spec/quickstart.md
- [ ] T099 Run `dotnet build` validation for MotorcycleRAG.sln (MotorcycleRAG.sln)
- [ ] T100 Run `dotnet test` validation for MotorcycleRAG.sln (MotorcycleRAG.sln)
- [ ] T101 Run `npm run build` validation for 1-Presentation/MotorcycleRag.WebUI/package.json
- [ ] T102 Document deployment config keys in 6-Docs/deployment.md

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup; blocks all user stories.
- **User Stories (Phase 3+)**: Depend on Foundational.
- **Polish (Final Phase)**: Depends on whichever stories are targeted for release.

### User Story Completion Order (Dependency Graph)

- Phase 2 → **US1 (P1)**
- Phase 2 → **US1a (P1)** (can start in parallel with US1 after auth foundations)
- Phase 2 → **US2 (P2)**
- Phase 2 → **US3 (P2)**
- **US2 + US3** → **US3a (P2)** (admin UI depends on API ingress endpoints)
- Phase 2 → **US6 (P2)**
- Phase 2 → **US7 (P3)** (depends on config store + admin auth)
- Phase 2 → **US4 (P3)**
- Phase 2 → **US5 (P3)** (cross-cutting)

## Parallel Execution Examples

### US1 (Parallel)

- T023 and T024 can run in parallel (same file but non-overlapping blocks; coordinate merges) in 3-Domain/MotorcycleRAG.Domain/Models/QueryModels.cs
- T025 and T026 can proceed in parallel in 2-Application/MotorcycleRAG.Application/Services (AgentOrchestrator vs MotorcycleRAGService)

### US3a (Parallel)

- T060 (API client) and T063 (upload UI) can run in parallel in 1-Presentation/MotorcycleRAG.Admin/
- T064 (PDF chunking) and T065 (CSV chunking) can run in parallel in 1-Presentation/MotorcycleRAG.Admin/Processing/

---

## Implementation Strategy

### MVP First

1. Phase 1 (Setup)
2. Phase 2 (Foundational)
3. Phase 3 (US1)
4. Validate US1 independently (manual test against `POST /api/motorcycles/query`)

### Incremental Delivery

- Add US1a (auth/profile/limits), then ingestion (US2), then manuals (US3), then MAUI admin (US3a), then web sources (US6), then MCP config (US7), then reliability (US4), then security hardening evidence (US5).
