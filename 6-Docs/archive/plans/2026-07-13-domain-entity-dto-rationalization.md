# Domain Entity and DTO Rationalization

**Status:** Archived
**Date:** 2026-07-13  
**Goal:** Apply the approved rule that property bags are DTOs and entities own identity plus domain invariants or legal state transitions, while preserving Clean Architecture boundaries and an atomic compile-safe migration.

## Approved decisions

- `ManualDocument` is the canonical manual aggregate; retire `MotorcycleManual`.
- `IngestionJob` remains a rich Domain aggregate. Application orchestrates I/O and Persistence retains compare-and-swap concurrency guarantees.
- Shared cross-layer data carriers move to `MotorcycleRAG.Contracts.Models`; `QueryPlanDto` is Application-local.
- The migration is atomic. Do not retain deprecated aliases, compatibility adapters, or duplicate types. Preserve externally visible serialized property names where their contracts remain supported.

## Acceptance criteria

1. Every type under `3-Domain/MotorcycleRAG.Domain/Entities/` is either a behavior-bearing Entity or has been moved to the appropriate DTO boundary; no property bag remains named or placed as an Entity.
2. `ManualDocument` is the only manual-document aggregate; `MotorcycleManual` and duplicate manual processing records are removed.
3. `IngestionJob`, `BikeModel`, `McpToolConfiguration`, `WebScrapeRun`, and `WebTrustPolicy` own their documented invariants/transitions and expose no public mutation that bypasses them.
4. Shared graph, indexing, manual-processing, search, and specification data carriers are suffixed `Dto`, live in `Contracts.Models`, and all Contracts/Application/Persistence callers compile against them.
5. `QueryPlanDto` is in `MotorcycleRAG.Application` and no longer occupies the Domain Entities folder.
6. The Domain scoped instructions and synchronized agent-role guidance enforce the classification rule; retained Domain behavior has focused unit tests and the solution-level coverage gate passes.
7. Canonical documentation and the plan index are updated before plan closeout.

## Closeout review (2026-07-15)

This plan remains **Review required** and is not eligible for archival.

Verified implementation and evidence:

- The former graph, indexing, manual-processing, search, specification, and query-plan property bags are located at their planned DTO boundaries; the retired Domain Entity files are absent.
- The supplied fresh Domain coverage artifact at `/tmp/mcr-domain-final-independent-20260715-081500/a837df20-f8db-4c1b-8883-7a86ede9a4c5/coverage.cobertura.xml` reports the retained Domain entities at 90.90%–100% line coverage. Domain tests passed 146/146; Persistence tests passed 1,444 with seven documented existing skips; completed package reviews were approved.
- The canonical architecture rules and catalog have been reviewed and updated where this migration changed current terminology.

Remaining acceptance-criterion gaps:

1. Criterion 3 is not complete. `IngestionJob` still exposes public setters, and `BikeModel`, `ManualDocument`, and `McpToolConfiguration` expose public `init` setters that let callers bypass their factories and lifecycle validation.
2. Criterion 6 is not complete. `DomainEntities_PostMigrationInventory_ContainsOnlyBehaviorBearingTypes` remains skipped in `ModelClassificationPolicyTests`; the required post-migration inventory gate is therefore not enforcing the rule against new property bags or publicly mutable Domain entities.
3. The supplied artifact is a Domain test coverage report that also includes referenced Core classes. It confirms the retained Domain entity coverage, but it is not evidence that the configured solution-level aggregation command has passed.

Resume implementation to remove the setter bypasses, enable and pass the inventory gate, and capture the configured solution-level coverage aggregation. A new closeout review can then verify every acceptance criterion and archive the plan.

## Work packages and dependency order

### 1. Define and enforce the classification policy

**Owner skills:** `dotnet-dev`, `code-review`, `app-docs-standard`  
**Depends on:** none  
**File scope:**

- `3-Domain/MotorcycleRAG.Domain/AGENTS.md`
- `6-Docs/rules/architecture-general.md`
- `6-Docs/system/architecture.md`
- `3-Domain/MotorcycleRAG.Contracts.Models/AGENTS.md`
- `5-Test/MotorcycleRAG.Domian.Tests/AGENTS.md`
- `5-Test/MotorcycleRAG.Contracts.Tests/AGENTS.md`

Add these enforceable rules:

- An Entity has stable identity and owns at least one invariant, state transition, or other domain behavior. A persistence key, public properties, default initializers, and declarative validation attributes alone are insufficient.
- A DTO is a data carrier. Shared DTOs belong in `Contracts.Models` and end in `Dto`; use-case-local DTOs belong in Application.
- Value objects are immutable, equality-by-value semantic concepts and may contain behavior; they are neither DTOs nor entities.
- Entity state must not have public setters that bypass invariants. One top-level type per file.
- Persistence rows that do not form a shared contract remain private to Persistence rather than leaking into Domain.

Synchronize the same role behavior in all six tool surfaces for `dotnet-dev`, `dal-dev`, `test-dev`, and `code-reviewer`:

- `.codex/agents/{dotnet-dev,dal-dev,test-dev,code-reviewer}.toml`
- `.cursor/agents/{dotnet-dev,dal-dev,test-dev,code-reviewer}.agent.md`
- `.github/agents/{dotnet-dev,dal-dev,test-dev,code-reviewer}.agent.md`
- `.opencode/agents/{dotnet-dev,dal-dev,test-dev,code-reviewer}.md`
- `.kilo/agents/{dotnet-dev,dal-dev,test-dev,code-reviewer}.md`
- `.claude/agents/{dotnet-dev,dal-dev,test-dev,code-reviewer}.md`

Add focused architecture/classification tests rather than a filename-only rule. The test must reject new Domain Entity types with no declared invariant/transition and DTO types outside their approved boundary, while allowing documented exceptions only by explicit architecture approval.

### 2. Migrate independent shared graph and indexing DTO families

**Owner skills:** `dotnet-dev`, `dal-dev`, `test-dev`  
**Depends on:** package 1  
**Parallel-safe scopes:** graph family and indexing family may run concurrently.

**Graph family file scope:**

- Move `3-Domain/MotorcycleRAG.Domain/Entities/GraphNode.cs` to `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/Graph/GraphNodeDto.cs`.
- Move `3-Domain/MotorcycleRAG.Domain/Entities/GraphEdge.cs` to `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/Graph/GraphEdgeDto.cs`.
- Update `3-Domain/MotorcycleRAG.Contracts/Repositories/IGraphRepository.cs`.
- Update `2-Application/MotorcycleRAG.Application/Services/Ingestion/GraphEntityIngestionService.cs`, `ManualBikeLinker.cs`, `MotorcycleCategoryClassifier.cs`, and `Services/QueryValidation/QuestionValidationService.cs`.
- Update `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/SqlGraphRepository.cs`.
- Update `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/GraphPathResult.cs`, `GraphTraversalResult.cs`, plus graph/application/persistence tests.

**Indexing family file scope:**

- Move `Entities/IndexedArtifact.cs` to `Contracts.Models/DTOs/Ingestion/IndexedArtifactDto.cs`.
- Move `Entities/IndexedChunk.cs` to `Contracts.Models/DTOs/Ingestion/IndexedChunkDto.cs`.
- Update `3-Domain/MotorcycleRAG.Contracts/Interfaces/IIndexedArtifactRepository.cs` and `IIndexedChunkRepository.cs`.
- Update `2-Application/MotorcycleRAG.Application/Services/Ingestion/ProcessorArtifactService.cs`, `ChunkReprocessService.cs`, and `IngestionJobService.cs`.
- Update `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/IndexedArtifactRepository.cs` and `IndexedChunkRepository.cs`.
- Update all affected Application and Persistence repository tests.

### 3. Consolidate the manual-document aggregate and processing DTOs

**Owner skills:** `dotnet-dev`, `dal-dev`, `test-dev`  
**Depends on:** package 1  
**Must be serialized with:** package 5 because `IngestionJob` owns the related lifecycle.

- Retire `3-Domain/MotorcycleRAG.Domain/Entities/MotorcycleManual.cs` and use `ManualDocument` as the sole manual aggregate.
- Refactor `Entities/ManualDocument.cs` so it owns manual identity and applicable lifecycle invariants. Keep only behavior-bearing aggregate state in Domain.
- Retire `Entities/ManualProcessingRun.cs` and `ManualProcessingStage.cs`; consolidate shared data into existing `Contracts.Models/DTOs/ManualIngestion/ManualRunDto.cs` and `ManualStageDto.cs`.
- Resolve `Entities/ManualPage.cs` against the canonical `ManualDocument` aggregate: either make it a behavior-bearing child entity with controlled creation, or move it to `Contracts.Models/DTOs/ManualIngestion/ManualPageDto.cs` if it remains a data carrier. The approved property-bag rule selects the DTO route unless package implementation identifies and tests a real page invariant.
- Update `3-Domain/MotorcycleRAG.Contracts/Interfaces/IManualDocumentRepository.cs` and `IManualIngestionService.cs`.
- Update `2-Application/MotorcycleRAG.Application/Services/Ingestion/ManualIngestionService.cs`, `ManualPageQueryService.cs`, and `ManualBikeLinker.cs`.
- Update `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/ManualDocumentRepository.cs`, related schema/migrations only if the atomic mapping cannot preserve the current schema, and all manual-ingestion API, integration, and repository tests.

### 4. Move search, specification, and use-case-plan property bags

**Owner skills:** `dotnet-dev`, `dal-dev`, `test-dev`  
**Depends on:** package 1  
**Parallel-safe scopes:** search/specification family and `QueryPlanDto` may run concurrently; search must serialize with package 2 only where shared `ProcessedData` or graph types overlap.

- Move `Entities/MotorcycleDocument.cs` to `Contracts.Models/DTOs/Search/MotorcycleDocumentDto.cs`; classify/move its `DocumentMetadata` companion consistently rather than retaining a mutable Domain value object.
- Update `3-Domain/MotorcycleRAG.Contracts/Interfaces/IMotorcycleIndexingService.cs`, `Contracts.Models/DTOs/ProcessedData.cs`, PDF/CSV processors, Azure Search adapters, and their tests.
- Split `Entities/MotorcycleSpecification.cs` into five one-type-per-file shared DTOs: `MotorcycleSpecificationDto`, `EngineSpecificationDto`, `PerformanceMetricsDto`, `SafetyFeaturesDto`, and `PricingInformationDto` under `Contracts.Models/DTOs/Specifications/`.
- Move `Entities/QueryPlan.cs` to `2-Application/MotorcycleRAG.Application/DTOs/QueryPlanDto.cs`; update any query-planner callers/tests. Do not place this use-case result in Domain or shared Contracts.Models unless an actual cross-boundary consumer is introduced.

### 5. Refactor retained entities to own their behavior

**Owner skills:** `dotnet-dev`, `test-dev`, `dal-dev`  
**Depends on:** package 1; packages 2 and 3 for `IngestionJob` contract callers  
**Serialized scope:** `IngestionJob` with package 3; the remaining three entities may run in parallel.

- `Entities/BikeModel.cs`: preserve canonical identity and make normalized-name/alias rules non-bypassable; update `IBikeModelRepository`, `SpecsIngestionService`, `QuestionValidationService`, and Domain tests.
- `Entities/McpToolConfiguration.cs`: retain enable/disable/configuration/connection-status transitions; add invariant validation and prevent public state bypass; update tool configuration services, repositories, and tests.
- `Entities/WebTrustPolicy.cs`: retain as a policy entity; add normalized host-pattern matching, blocked precedence, and tier semantics; update `IWebTrustPolicyStore`, configuration store, and tests.
- `Entities/WebScrapeRun.cs`: replace freely mutable string lifecycle state with a typed status and legal start/complete/fail transitions; update `IWebScrapeRunRepository`, `IWebScrapeOrchestrator`, `WebScrapeOrchestrator`, Persistence repository, API mappings, and tests.
- `Entities/IngestionJob.cs`: encapsulate construction and legal queued/processing/awaiting-metadata/terminal/deleting transitions. Move only I/O/orchestration to Application and retain Persistence compare-and-swap operations as concurrency enforcement. Update `IIngestionJobRepository`, `IIngestionJobService`, `IngestionJobService`, hosted ingestion services, artifact services, status mappers, repository SQL mapping, API tests, and integration tests.

### 6. Verification and plan closeout

**Owner skills:** `test-dev`, `code-review`, `app-docs-standard`; orchestrated by `conductor`  
**Depends on:** packages 2–5.

- Run focused Domain, Contracts, Application, Persistence, API, and integration tests for each package.
- Run the configured solution-level .NET coverage suite and `5-Test/scripts/aggregate_coverage.py`; retained Entity behaviors must meet the strict per-file/per-class gate. Do not add `CompilerGenerated` or ad-hoc coverage exclusions.
- Run formatting/build checks and verify all type/namespace renames with repository-wide search.
- Assign independent code review for each completed package and resolve findings before release.
- Assign the mandatory `docs-dev` plan-closeout task after implementation evidence is complete. It must update the canonical architecture/data-model documentation and `6-Docs/catalog.md` if ownership or public component boundaries changed, mark this plan `Completed`/then `Archived`, and update `6-Docs/plans/README.md` with the archive entry.

## Execution lanes

1. Complete package 1 first.
2. Start package 2 graph family, package 2 indexing family, and package 4 specification/QueryPlan scopes in parallel where their file scopes do not overlap.
3. Start package 3 and the `IngestionJob` portion of package 5 together only as one serialized manual/ingestion lane.
4. Run BikeModel, MCP configuration, WebTrustPolicy, and WebScrapeRun sub-lanes in parallel after package 1, except where their shared contracts overlap a current worker.
5. Each worker completion enters review immediately; rework stays in the same file scope. Package 6 follows only after all package reviews approve.

## Required skills

| Work | Skills |
| --- | --- |
| Domain, Contracts.Models, Application migration | `dotnet-dev` |
| SQL repository/schema mapping changes | `dal-dev` |
| Unit/integration/coverage tests | `test-dev` |
| Package review | `code-review` |
| Canonical docs and mandatory closeout | `app-docs-standard` |
| Scheduling, dependency tracking, review pipeline | `conductor` |

## Out of scope

- Changing deployed schemas, infrastructure, or Azure resources without separate approval.
- Compatibility aliases, fallback adapters, or duplicate legacy models.
- Altering API serialization shapes beyond what is required to preserve existing supported contracts.
