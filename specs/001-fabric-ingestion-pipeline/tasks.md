# Tasks: Fabric Ingestion Pipeline

**Input**: Design documents from `/specs/001-fabric-ingestion-pipeline/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests are REQUIRED by the constitution. Include test tasks for each user story.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure. Capture baselines and options.

- [X] T000 Instruct user to provision Microsoft Fabric environment according to `specs/001-fabric-ingestion-pipeline/quickstart.md`
- [X] T001 Capture baseline endpoints + DTO risks in code comments near legacy routes in `1-Presentation/MotorcycleRAG.API/Controllers/DataPipelineProcessingController.cs`
- [X] T002 Add feature configuration options placeholders (limits + toggles) in `2-Application/MotorcycleRAG.Application/Pipeline/PipelineConfiguration.cs`
- [X] T003 [P] Add policy name constants for this feature in `1-Presentation/MotorcycleRAG.API/Configuration/AuthorizationPolicyNames.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 Define `mcr-api-manuals-view` authorization policy in `1-Presentation/MotorcycleRAG.API/Program.cs`
- [X] T005 [P] Define request DTO `IngestionJobStartRequest` using opaque upload reference in `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/IngestionJobStartRequest.cs`
- [X] T006 [P] Define response DTO `IngestionJobStatusResponse` in `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/IngestionJobStatusResponse.cs`
- [X] T007 [P] Define abstraction for manual page asset storage `IManualPageAssetStore` in `3-Domain/MotorcycleRAG.Contracts/Interfaces/IManualPageAssetStore.cs`
- [X] T008 [P] Define ingestion job persistence contract `IIngestionJobRepository` in `3-Domain/MotorcycleRAG.Contracts/Interfaces/IIngestionJobRepository.cs`
- [X] T009 [P] Define SQL Graph abstraction `IGraphRepository` in `3-Domain/MotorcycleRAG.Contracts/Repositories/IGraphRepository.cs`
- [X] T010 Create SQL Migration for `GraphNodes` and `GraphEdges` tables in `4-Persistence/MotorcycleRAG.Persistence/Sql/Migrations/GraphTablesMigration.sql`

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Ingest Service Manuals (Priority: P1) 🎯 MVP

**Goal**: As a content administrator, I can ingest a motorcycle service manual so that the system extracts structured, searchable knowledge while preserving document hierarchy and source references. The system produces a searchable manual, records relations in SQL Graph, and executes via Microsoft Fabric REST API.

**Independent Test**: Ingest a known manual and confirm that key procedures and torque specs are searchable and always link back to the correct source reference. Verify via API polling that Fabric Job executes and updates coverage metrics.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T011 [P] [US1] Integration test for job status in `5-Test/tests/MotorcycleRAG.IntegrationTests/Pipeline/IngestionJobStatusIntegrationTests.cs`
- [X] T012 [P] [US1] Unit tests for coverage calculation edge cases in `5-Test/tests/MotorcycleRAG.UnitTests/Pipeline/CoverageCalculatorTests.cs`

### Implementation for User Story 1

- [X] T013 [P] [US1] Implement `IngestionJob` entity in `3-Domain/MotorcycleRAG.Domain/Entities/IngestionJob.cs`
- [X] T014 [P] [US1] Implement `ManualDocument`, `ManualPageAsset`, `ManualSection` entities in `3-Domain/MotorcycleRAG.Domain/Entities/ManualEntities.cs`
- [X] T015 [P] [US1] Implement `GraphNode`, `GraphEdge` entities in `3-Domain/MotorcycleRAG.Domain/Entities/GraphEntities.cs`
- [X] T016 [US1] Implement SQL persistence for ingestion jobs in `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/IngestionJobRepository.cs`
- [X] T017 [US1] Implement SQL Graph Repository with Dapper in `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/SqlGraphRepository.cs`
- [X] T018 [US1] Implement Azure Blob-backed page asset store in `4-Persistence/MotorcycleRAG.Persistence/Azure/Blob/BlobManualPageAssetStore.cs`
- [X] T019 [US1] Implement Fabric REST API Service `FabricPipelineService` in `4-Persistence/MotorcycleRAG.Persistence/ExternalServices/FabricPipelineService.cs`
- [X] T020 [US1] Implement Ingestion Job Service in `2-Application/MotorcycleRAG.Application/Pipeline/IngestionJobService.cs`
- [X] T021 [US1] Implement durable job resumption (skip processed chunks/pages) in `2-Application/MotorcycleRAG.Application/Pipeline/IngestionJobService.cs`
- [X] T022 [US1] Implement file validation (type, size limits, basic structure) in `2-Application/MotorcycleRAG.Application/Pipeline/Validators/IngestionJobValidator.cs`
- [X] T023 [US1] Implement lower-cost fallback behavior for metered external services in `2-Application/MotorcycleRAG.Application/Pipeline/Extractors/MeteredServiceFallbackPolicy.cs`
- [X] T024 [US1] Implement chunking and Graph Entity extraction `StartFabricIngestionCommand` in `2-Application/MotorcycleRAG.Application/Pipeline/Commands/StartFabricIngestionCommand.cs`
- [X] T025 [US1] Add `POST /api/ingestion/jobs/upload` using MultipartReader in `1-Presentation/MotorcycleRAG.API/Controllers/IngestionJobsController.cs`
- [X] T026 [US1] Add `POST /api/ingestion/jobs` trigger endpoint in `1-Presentation/MotorcycleRAG.API/Controllers/IngestionJobsController.cs`
- [X] T027 [US1] Add `GET /api/ingestion/jobs/{jobId}` status endpoint in `1-Presentation/MotorcycleRAG.API/Controllers/IngestionJobsController.cs`
- [X] T028 [P] [US1] Add Ingestion ViewModel in `1-Presentation/MotorcycleRAG.Admin/ViewModels/IngestionViewModel.cs`
- [X] T029 [US1] Add Ingestion View in `1-Presentation/MotorcycleRAG.Admin/Views/IngestionPage.xaml`

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently

---

## Phase 4: User Story 2 - Ingest Bike Specifications Dataset (Priority: P2)

**Goal**: As a content administrator, I can ingest a structured motorcycle specification dataset so that bike models are created/updated consistently (including common alias/format variations).

**Independent Test**: Ingest a sample CSV dataset with known duplicates/aliases and verify that canonical bike models are created and duplicates are handled deterministically.

### Tests for User Story 2

- [X] T030 [P] [US2] Unit tests for model normalization and aliases in `5-Test/tests/MotorcycleRAG.UnitTests/Pipeline/BikeModelNormalizationTests.cs`
- [X] T031 [P] [US2] Integration test for specs ingestion upsert behavior in `5-Test/tests/MotorcycleRAG.IntegrationTests/Pipeline/SpecsIngestionIntegrationTests.cs`

### Implementation for User Story 2

- [X] T032 [P] [US2] Create `BikeModel` entity in `3-Domain/MotorcycleRAG.Domain/Entities/BikeModel.cs`
- [X] T033 [P] [US2] Implement `BikeModel` persistence in `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/BikeModelRepository.cs`
- [X] T034 [US2] Implement CSV specs ingestion orchestrator in `2-Application/MotorcycleRAG.Application/Pipeline/SpecsIngestionService.cs`
- [X] T035 [US2] Update `IngestionViewModel.cs` and `IngestionPage.xaml` to support CSV uploads, including client-side pre-validation of CSV structure in `1-Presentation/MotorcycleRAG.Admin/`

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently

---

## Phase 5: User Story 3 - Link Manuals + Specs for Cross-Linked Retrieval (Priority: P3)

**Goal**: As a user of the RAG system, I can ask a maintenance question (e.g., torque spec or tool requirement) and get an answer that uses the ingested manual knowledge, is connected to the correct bike model, and always includes source references. Users can view retrieved manual pages securely.

**Independent Test**: Run a benchmark set of questions tied to known sources and verify that answers point to the correct manual sections and the correct bike model attributes. Retrieve specific manual pages successfully using an authorized account.

### Tests for User Story 3

- [X] T036 [P] [US3] Integration test for manual page retrieval entitlement in `5-Test/tests/MotorcycleRAG.IntegrationTests/Api/ManualPageRetrievalAuthorizationTests.cs`

### Implementation for User Story 3

- [X] T037 [P] [US3] Add `GET /api/manuals/{manualId}/pages/{pageNumber}` endpoint in `1-Presentation/MotorcycleRAG.API/Controllers/ManualsController.cs`
- [X] T038 [US3] Implement manual page access checks securely proxying blob storage bytes in `2-Application/MotorcycleRAG.Application/Pipeline/ManualPageQueryService.cs`
- [X] T039 [US3] Implement manual-to-bike linkage logic during ingestion in `2-Application/MotorcycleRAG.Application/Pipeline/ManualBikeLinker.cs`

**Checkpoint**: All user stories should now be independently functional

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [X] T040 [P] Add timeouts + retry/backoff around Fabric and Azure Blob external calls in `4-Persistence/MotorcycleRAG.Persistence/Resilience/ResilienceService.cs`
- [X] T041 Ensure rate limiting applies to new controllers in `1-Presentation/MotorcycleRAG.API/Program.cs`
- [X] T042 Add budget visibility metrics and custom telemetry events in `4-Persistence/MotorcycleRAG.Persistence/Telemetry/TelemetryService.cs`
- [X] T043 [P] Implement operational budget safeguards (throttling and alerts for metered services) in `4-Persistence/MotorcycleRAG.Persistence/Telemetry/BudgetMonitorService.cs`
- [X] T044 [P] Implement audit-friendly logging (who/what/when/outcome) for all ingestion actions without exposing sensitive content in `2-Application/MotorcycleRAG.Application/Pipeline/Audit/IngestionAuditLogger.cs`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
  - User stories can proceed in parallel or sequentially in priority order (P1 → P2 → P3)
- **Polish (Final Phase)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2)
- **User Story 2 (P2)**: Can start after Foundational (Phase 2)
- **User Story 3 (P3)**: Depends on US1 (for manual pages) and US2 (for bike models)

### Within Each User Story

- Tests MUST be written and FAIL before implementation
- Entities before persistence repositories
- Repositories before services
- Services before endpoints (API) and Views (MAUI)

### Parallel Opportunities

- All Setup tasks marked `[P]` can run in parallel
- All Foundational tasks marked `[P]` can run in parallel (within Phase 2)
- Once Foundational phase completes, `[US1]` and `[US2]` can start in parallel
- All tests for a user story marked `[P]` can run in parallel
- Entities within a story marked `[P]` can run in parallel

---

## Parallel Example: User Story 1

```bash
# Launch all models and basic repositories for User Story 1 together:
Task: "Implement IngestionJob entity in 3-Domain/MotorcycleRAG.Domain/Entities/IngestionJob.cs"
Task: "Implement ManualDocument, ManualPageAsset, ManualSection entities in 3-Domain/MotorcycleRAG.Domain/Entities/ManualEntities.cs"
Task: "Implement GraphNode, GraphEdge entities in 3-Domain/MotorcycleRAG.Domain/Entities/GraphEntities.cs"

# Launch tests in parallel:
Task: "Integration test for job status in 5-Test/tests/MotorcycleRAG.IntegrationTests/Pipeline/IngestionJobStatusIntegrationTests.cs"
Task: "Unit tests for coverage calculation edge cases in 5-Test/tests/MotorcycleRAG.UnitTests/Pipeline/CoverageCalculatorTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Test User Story 1 independently using the MAUI Admin App and Fabric UI.
5. Deploy/demo if ready

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy/Demo (MVP!)
3. Add User Story 2 → Test independently
4. Add User Story 3 → Test independently
5. Each story adds value without breaking previous stories