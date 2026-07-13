# MotorcycleRAG.Persistence Unit Test Coverage 46.92% to 90%+

**Status:** Draft
**Date:** 2026-07-12
**Goal:** Increase `MotorcycleRAG.Persistence` unit test coverage from 46.92% to over 90% by creating a new dedicated test project `5-Test/MotorcycleRAG.API.Tests`.

---

> **Plan-conflict resolution note (2026-07-12):** Two independent investigations of this request produced two draft plans — this file, and `6-Docs/plans/2026-07-12-api-persistence-test-coverage-plan.md` (now `Superseded`, pointing here). They disagreed on scope (full 42-file Persistence inventory vs. the 15 `Sql/Repositories/` classes only) and on the new project's name/location (`MotorcycleRAG.Persistence.Tests` under `5-Test/tests/` vs. `MotorcycleRAG.API.Tests` under `5-Test/`). The user resolved the conflict: **adopt this plan's comprehensive 42-file scope and phased structure**, but **rename/relocate the target project to `5-Test/MotorcycleRAG.API.Tests`** (the literal path originally requested), not `5-Test/tests/MotorcycleRAG.Persistence.Tests/`. Every reference to the project name/path below has been updated accordingly. See §6 for the resulting naming-mismatch risk this creates (a project that tests only `MotorcycleRAG.Persistence` classes, named `...API.Tests`, living directly under `5-Test/` rather than alongside its closest siblings `MotorcycleRAG.UnitTests`/`MotorcycleRAG.IntegrationTests` under `5-Test/tests/`).

## 1. Problem / Motivation

The `MotorcycleRAG.Persistence` project (42 source files across 13 subfolders) currently has 46.92% line coverage as measured by the `dotnet-unit` coverage suite (`5-Test/scripts/run_unit_coverage.py` / `coverage-config.json`). This is the coverage figure the original request's "47.9%" baseline was almost certainly referring to (the request mislabeled the assembly as `MotorcycleRAG.API`, which is actually at 66.3%; `MotorcycleRAG.API`'s own persistence-DI-wiring class, `PersistenceConfiguration.cs`, is already at 100% coverage and required no work). The coverage policy in `coverage-config.json` requires 90% file-line and 90% class-line coverage. Only 7 of ~42 source files have dedicated unit tests today:

- `Sql/SqlConnectionFactory.cs` (tested in `MotorcycleRAG.UnitTests`)
- `Sql/Repositories/AccessRequestRepository.cs`, `IngestionJobRepository.cs`, `SqlGraphRepository.cs` (tested in `MotorcycleRAG.UnitTests`)
- `Azure/ExternalIdentityProvisioningService.cs` (tested in `MotorcycleRAG.UnitTests`)
- `Configuration/McpConfigurationStore.cs`, `WebTrustPolicyStore.cs` (tested in `MotorcycleRAG.UnitTests`)
- `Telemetry/BudgetMonitorService.cs` (tested in `MotorcycleRAG.UnitTests`)

The remaining ~35 source files have no unit tests. A new dedicated test project provides a single home for Persistence tests, proper IVT access to internal members, and a focused coverage suite.

---

## 2. Approved decisions

**D1 — Leave existing Persistence tests in `MotorcycleRAG.UnitTests`.** Existing tests in `5-Test/tests/MotorcycleRAG.UnitTests/Persistence/` remain in place. The new `5-Test/MotorcycleRAG.API.Tests` project focuses exclusively on uncovered files. Rationale: additive and lower-risk; the coverage aggregation merges suites using max line-hits per file (`aggregate_coverage.py` line 193), so both suites contribute; avoids namespace changes and potential breakage. Existing tests can be migrated in a separate cleanup task if desired.

**D2 — New project at `5-Test/MotorcycleRAG.API.Tests/`.** Per explicit user direction, this deviates from the sibling layout used by the other consolidated backend suites (`5-Test/tests/MotorcycleRAG.UnitTests/`, `5-Test/tests/MotorcycleRAG.IntegrationTests/`) and instead sits directly under `5-Test/`, matching the convention used for `5-Test/MotorcycleRag.WebUI.BFF.Tests/` and `5-Test/MotorcycleRAG.MobileApp.Tests/` (though those are separate-deployable app suites, not a shared-backend suite). Folder structure inside the project still mirrors the Persistence source layout (`Sql/Repositories/`, `Azure/Search/`, etc.). See §6 for the resulting naming/location risk.

**D3 — New `persistence-unit` coverage suite, corrected to the final project path.** `coverage-config.json` already contains a `persistence-unit` suite entry from the earlier (superseded) plan iteration, pointing at `5-Test/tests/MotorcycleRAG.Persistence.Tests/MotorcycleRAG.Persistence.Tests.csproj`. This must be corrected as part of P0 to point at `5-Test/MotorcycleRAG.API.Tests/MotorcycleRAG.API.Tests.csproj` with `testRoots: ["5-Test/MotorcycleRAG.API.Tests/"]`. The suite's logical `name` stays `persistence-unit` (it describes what is measured, not the project name) with `sourceRoots: ["4-Persistence/"]`. The existing `dotnet-unit` suite retains `4-Persistence/` in its sourceRoots (it still runs the existing 7 tested files' tests). Both suites' cobertura XMLs are merged by the aggregator using max per-line coverage.

**D4 — Extract shared FakeDbConnection infrastructure.** The existing `FakeDbConnection`/`FakeDbCommand`/`FakeDbParameter`/`FakeDbParameterCollection` classes are duplicated as `private sealed class` in 3 existing test files. The new project extracts these into a single shared `internal` file under `Infrastructure/`.

**D5 — IVT for the new project.** Add `<InternalsVisibleTo Include="MotorcycleRAG.API.Tests" />` to `MotorcycleRAG.Persistence.csproj`. The current csproj only declares IVT for `MotorcycleRAG.UnitTests` (line 42). (Note: the assembly name is `MotorcycleRAG.API.Tests` per D2 even though the project only tests `MotorcycleRAG.Persistence` — the IVT entry must match the assembly name exactly, not the tested project's name.)

---

## 2a. Open questions (decision ledger)

_None outstanding. Scope (all 42 files) and project location (`5-Test/MotorcycleRAG.API.Tests`) were resolved by the user; see D1–D5 above and the plan-conflict resolution note at the top of this file._

---

## 3. Investigation findings

### Coverage baseline correction

- The original request's "47.9% for MotorcycleRAG.API" does not match live data for the `MotorcycleRAG.API` assembly (66.3% per the merged `coveragereport/Summary.txt`, and 100% for its only persistence-named class, `PersistenceConfiguration.cs`). It much more closely matches `MotorcycleRAG.Persistence`'s 46.92% figure as measured by the `dotnet-unit` suite's per-sourceRoot filtering in `run_unit_coverage.py`/`aggregate_coverage.py` — this plan treats that as the corrected baseline and target assembly.

### Coverage infrastructure

- `coverage-config.json` defines a `dotnet-unit` suite that runs `MotorcycleRAG.UnitTests.csproj` against sourceRoots including `4-Persistence/`.
- `run_unit_coverage.py` runs `dotnet test <single-project>` per suite. Each suite has exactly one `project` field; multiple projects require multiple suite entries.
- `aggregate_coverage.py` parses each suite's cobertura XML, filters by the suite's `sourceRoots`, and merges file metrics using **max line hits** across suites. This means two suites covering the same source file complement each other.
- `coverlet.runsettings` excludes test assemblies (`[MotorcycleRAG.*.Test*]*`) and generated/migration files from coverage.
- The 90% threshold is enforced per-file and per-class, not just aggregate. Every in-scope source file must individually meet 90%.
- **A `persistence-unit` suite entry already exists in `coverage-config.json`** (added by the earlier, superseded plan iteration) pointing at the old project name/path. It must be corrected in P0 — see D3.

### Existing test patterns

- **SQL repositories (Dapper):** Use `FakeDbConnection` (extends `DbConnection`) with enqueued `DbDataReader` results. The `ISqlConnectionFactory` is mocked to return the fake connection. Tests verify: constructor null-guard validation, happy-path data mapping (using `[Theory]`/`[InlineData]` for enum state matrices), no-rows returns null, error wrapping, and parameter assignment. See `AccessRequestRepositoryTests.cs` (927 lines) as the reference implementation.
- **SqlConnectionFactory:** Uses a `TestableSqlConnectionFactory` subclass that overrides `protected virtual OpenSqlConnectionAsync` to avoid real database connections. Uses `NullLogger<>` and `Options.Create(SqlOptions)`.
- **Azure services:** Use `Moq` for `ILogger<T>`, `IOptions<T>`, `IResilienceService`, `ICorrelationService`, `ISearchClientFactory`. See `ExternalIdentityProvisioningServiceTests.cs`.
- **Package versions:** xUnit 2.9.3, Moq 4.20.72, FluentAssertions 8.9.0, coverlet.collector 10.0.0, Microsoft.NET.Test.Sdk 18.4.0, Moq.Contrib.HttpClient 1.4.0.

### FakeDbConnection duplication

The `FakeDbConnection`, `FakeDbCommand`, `FakeDbParameter`, and `FakeDbParameterCollection` classes are copy-pasted as `private sealed class` in:
- `AccessRequestRepositoryTests.cs` (line 754+)
- `IngestionJobRepositoryTests.cs` (line 874+)
- `SqlGraphRepositoryTests.cs` (line 852+)

No shared infrastructure file exists today. The new project will create a single `internal` copy.

### Solution file

`MotorcycleRAG.sln` (repo root) includes both `MotorcycleRAG.UnitTests` (line 32) and `MotorcycleRAG.IntegrationTests` (line 34). The new project must be added via `dotnet sln add`.

### IntegrationTests overlap

`5-Test/tests/MotorcycleRAG.IntegrationTests/Persistence/` contains only `DeepInfraEmbeddingTests.cs` (tests `AzureFoundryClientWrapper` via real HTTP, skipped by default). No SQL repository integration tests exist. No overlap with planned unit tests.

### Source file inventory (complete)

| Subfolder | Files | Tested | Untested |
|-----------|-------|--------|----------|
| `Sql/Repositories/` | 19 | 3 | 16 |
| `Sql/` (non-repo) | 2 code + 1 sql | 1 | 1 (ServiceCollectionExtensions) |
| `Azure/` (root) | 10 | 1 | 9 |
| `Azure/Search/` | 10 | 0 | 8 code + 2 interfaces |
| `Azure/Blob/` | 1 | 0 | 1 |
| `Configuration/` | 2 | 2 | 0 |
| `DataProcessing/` | 3 | 0 | 3 |
| `ExternalServices/` | 2 | 0 | 2 |
| `HealthChecks/` | 5 | 0 | 5 |
| `Local/` | 1 | 0 | 1 |
| `Notifications/` | 1 | 0 | 1 |
| `Resilience/` | 2 | 0 | 2 |
| `Search/` | 1 | 0 | 1 |
| `Telemetry/` | 3 | 1 | 2 |

---

## 4. Task list

### High-level task matrix

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| P0 | Prerequisites | Project setup | Create csproj, IVT, solution, coverage config (incl. correcting the pre-existing `persistence-unit` entry), shared infra, AGENTS.md | dotnet-dev, test-dev |
| P1 | SQL Repositories | Sql/Repositories/ | Unit tests for 16 untested Dapper repositories | test-dev |
| P2 | Azure Root | Azure/ | Unit tests for 9 untested Azure service/wrapper files | test-dev, dotnet-dev |
| P3 | Azure Search | Azure/Search/ | Unit tests for 8 untested search service files | test-dev, dotnet-dev |
| P4 | Azure Blob | Azure/Blob/ | Unit tests for BlobManualPageAssetStore | test-dev |
| P5 | DataProcessing | DataProcessing/ | Unit tests for 3 processor files | test-dev |
| P6 | ExternalServices | ExternalServices/ | Unit tests for 2 pipeline service files | test-dev |
| P7 | HealthChecks | HealthChecks/ | Unit tests for 5 health check files | test-dev |
| P8 | Local | Local/ | Unit tests for OpenAiCompatibleChatClient | test-dev |
| P9 | Notifications | Notifications/ | Unit tests for ApproverNotificationService | test-dev |
| P10 | Resilience | Resilience/ | Unit tests for CorrelationService and ResilienceService | test-dev |
| P11 | Search | Search/ | Unit tests for MotorcycleIndexingService | test-dev |
| P12 | Telemetry | Telemetry/ | Unit tests for IngestionTelemetryService and TelemetryService | test-dev |
| P13 | Sql DI | Sql/ | Unit tests for ServiceCollectionExtensions | test-dev |

Recommended execution order: **P1 (SQL Repositories) first**, since it has the most existing reference material (`AccessRequestRepositoryTests.cs` etc.) to validate the shared `FakeDbConnection` infrastructure from P0 before it's relied on elsewhere; then expand into P2–P13 in any order (all are mutually independent — see §5).

---

### P0 — Prerequisites

**Objective:** Stand up the new test project with all wiring before any test code is written.

**Files to create:**
- `5-Test/MotorcycleRAG.API.Tests/MotorcycleRAG.API.Tests.csproj`
- `5-Test/MotorcycleRAG.API.Tests/AGENTS.md`
- `5-Test/MotorcycleRAG.API.Tests/Infrastructure/FakeDbConnection.cs` (shared `internal` fakes: `FakeDbConnection`, `FakeDbCommand`, `FakeDbParameter`, `FakeDbParameterCollection`, and a `FakeDbDataReader`/`CreateReader` helper if needed)
- `5-Test/MotorcycleRAG.API.Tests/Infrastructure/TestHelpers.cs` (shared `TestableSqlConnectionFactory` subclass, reader-builder helpers)

**Files to modify:**
- `4-Persistence/MotorcycleRAG.Persistence/MotorcycleRAG.Persistence.csproj` — add `<InternalsVisibleTo Include="MotorcycleRAG.API.Tests" />` alongside the existing `MotorcycleRAG.UnitTests` entry (line 42)
- `MotorcycleRAG.sln` — add the new project via `dotnet sln add`
- `5-Test/scripts/coverage-config.json` — **correct** the existing `persistence-unit` suite entry (added by the superseded plan iteration) from `project: "5-Test/tests/MotorcycleRAG.Persistence.Tests/MotorcycleRAG.Persistence.Tests.csproj"` / `testRoots: ["5-Test/tests/MotorcycleRAG.Persistence.Tests/"]` to the corrected values below (D3)

**csproj details:** Copy the PropertyGroup and package references from `MotorcycleRAG.UnitTests.csproj` (net10.0, IsPackable=false, IsTestProject=true, OPENAI001 suppression). Include: xunit 2.9.3, xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.4.0, Moq 4.20.72, Moq.Contrib.HttpClient 1.4.0, FluentAssertions 8.9.0, coverlet.collector 10.0.0, Microsoft.Extensions.Logging.Abstractions 10.0.7, Microsoft.Extensions.Options 10.0.7, Microsoft.AspNetCore.TestHost 10.0.7, Microsoft.ApplicationInsights 3.1.1. Add a single `ProjectReference` to `4-Persistence/MotorcycleRAG.Persistence/MotorcycleRAG.Persistence.csproj` (no reference to `MotorcycleRAG.API` is needed under this plan's scope — see §6 naming risk). Add global usings: `Xunit`, `Moq`, `FluentAssertions`.

**Corrected `coverage-config.json` suite entry:**
```json
{
  "name": "persistence-unit",
  "kind": "dotnet",
  "project": "5-Test/MotorcycleRAG.API.Tests/MotorcycleRAG.API.Tests.csproj",
  "resultsSubdirectory": "persistence-unit",
  "supportedPlatforms": ["linux", "darwin", "win32"],
  "requiredInCi": true,
  "sourceRoots": ["4-Persistence/"],
  "testRoots": ["5-Test/MotorcycleRAG.API.Tests/"]
}
```

**Acceptance criteria:**
- `dotnet build 5-Test/MotorcycleRAG.API.Tests/MotorcycleRAG.API.Tests.csproj` succeeds with zero warnings (TreatWarningsAsErrors is inherited from Persistence reference chain).
- `dotnet test 5-Test/MotorcycleRAG.API.Tests/MotorcycleRAG.API.Tests.csproj` runs with zero tests and exits successfully.
- `python3 5-Test/scripts/run_unit_coverage.py --suite persistence-unit` runs without error against the corrected project path (produces empty cobertura, no failures).
- The new project appears in `MotorcycleRAG.sln`.
- `coverage-config.json`'s `persistence-unit` entry no longer references `MotorcycleRAG.Persistence.Tests` anywhere.

**Dependencies:** None. This is the first task; every other task (P1–P13) depends on it.

---

### P1 — SQL Repositories (16 untested files)

**Objective:** Achieve 90%+ coverage on all untested Dapper repositories in `Sql/Repositories/`.

**Source files (all under `4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/`):**

| Source file | Test file (under `.../MotorcycleRAG.API.Tests/Sql/Repositories/`) | Mock dependencies | Est. tests |
|-------------|--------------------------------------------------------------|-------------------|------------|
| `AuditRepository.cs` | `AuditRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<AuditRepository>` | 6-8 |
| `BikeModelCategoryRepository.cs` | `BikeModelCategoryRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 6-8 |
| `BikeModelRepository.cs` | `BikeModelRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 6-8 |
| `IndexedArtifactRepository.cs` | `IndexedArtifactRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 8-10 |
| `IndexedChunkRepository.cs` | `IndexedChunkRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 8-10 |
| `ManualDocumentRepository.cs` | `ManualDocumentRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 8-10 |
| `ParameterMergeExtensions.cs` | `ParameterMergeExtensionsTests.cs` | None (static extension methods, pure logic) | 5-7 |
| `PlanRepository.cs` | `PlanRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 8-10 |
| `ToolConfigurationAuditRepository.cs` | `ToolConfigurationAuditRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 6-8 |
| `ToolConfigurationRepository.cs` | `ToolConfigurationRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 8-10 |
| `UsageRepository.cs` | `UsageRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 6-8 |
| `UserIdentityRepository.cs` | `UserIdentityRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 6-8 |
| `UserManagementQueryRepository.cs` | `UserManagementQueryRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 8-10 |
| `UserRepository.cs` | `UserRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 8-10 |
| `WebScrapeRunRepository.cs` | `WebScrapeRunRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 6-8 |
| `WebSourceRepository.cs` | `WebSourceRepositoryTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 6-8 |

**Test pattern:** Follow `AccessRequestRepositoryTests.cs` as the reference. For each repository:
1. Constructor null-guard tests (null connectionFactory, null logger)
2. Input validation tests (blank/null arguments throw `ArgumentException`)
3. Happy-path data mapping (enqueue fake reader with populated row, verify returned DTO/entity mapping). Use `[Theory]` with `[InlineData]` for enum state matrices where applicable.
4. No-rows returns null/default
5. Connection failure wraps in expected exception type
6. Verify SQL parameter assignment via `connection.ExecutedCommands`

**Shared infrastructure:** Use the `FakeDbConnection` from `Infrastructure/FakeDbConnection.cs` (created in P0). Mock `ISqlConnectionFactory` to return the fake connection.

**Estimated total:** ~120-140 tests

**Dependencies:** P0 (shared infrastructure must exist).

**File/symbol scope for concurrency:** Each test file is independent (different source file, different test file). All 16 can be written in parallel by different agents, provided P0 is complete. No two agents touch the same file.

---

### P2 — Azure Root Services (9 untested files)

**Objective:** Achieve 90%+ coverage on untested Azure service and wrapper files in `Azure/`.

**Source files (all under `4-Persistence/MotorcycleRAG.Persistence/Azure/`):**

| Source file | Test file (under `.../MotorcycleRAG.API.Tests/Azure/`) | Mock dependencies | Est. tests | Difficulty |
|-------------|---------------------------------------------------|-------------------|------------|------------|
| `AzureFoundryClientWrapper.cs` | `AzureFoundryClientWrapperTests.cs` | `IHttpClientFactory` (via `Moq.Contrib.HttpClient`), `IResilienceService`, `ICorrelationService`, `IConfiguration`, `IOptions<AzureFoundryOptions>` | 10-12 | Moderate |
| `AzureSearchClientWrapper.cs` | `AzureSearchClientWrapperTests.cs` | Inspect deps at implementation time; likely `ISearchClientFactory` or `SearchClient` | 6-8 | Moderate |
| `AzureBlobStorageService.cs` | `AzureBlobStorageServiceTests.cs` | `BlobServiceClient` or `IBlobServiceClientFactory` (check if wrapper exists) | 6-8 | Hard |
| `BlobServiceClientFactory.cs` | `BlobServiceClientFactoryTests.cs` | `IOptions<AzureFoundryOptions>`, `TokenCredential` | 4-6 | Moderate |
| `DisabledDocumentIntelligenceClient.cs` | `DisabledDocumentIntelligenceClientTests.cs` | None (likely a no-op stub implementing an interface) | 3-5 | Easy |
| `DocumentIntelligenceClientWrapper.cs` | `DocumentIntelligenceClientWrapperTests.cs` | `DocumentIntelligenceClient` (Azure SDK), `ILogger<>`, `IOptions<>` | 6-8 | Hard |
| `FoundryAgentRunner.cs` | `FoundryAgentRunnerTests.cs` | `AIAgentClient`/`AgentsClient` (Azure.AI.Projects.Agents), `ILogger<>` | 6-8 | Hard |
| `HttpResilienceDelegatingHandler.cs` | `HttpResilienceDelegatingHandlerTests.cs` | `HttpMessageHandler` mock (via `Moq.Contrib.HttpClient` or `HttpClient` with fake handler) | 5-7 | Moderate |
| `ServiceCollectionExtensions.cs` | `AzureServiceCollectionExtensionsTests.cs` | `IServiceCollection` (real `ServiceCollection` instance) | 3-4 | Easy |

**Test pattern for HttpClient-based services:** Use `Moq.Contrib.HttpClient` to create a mock `HttpMessageHandler`, build an `HttpClient` from it, and mock `IHttpClientFactory.CreateClient()` to return it. Set up expected HTTP responses and verify request properties (URI, headers, body).

**Test pattern for DI extensions:** Create a real `ServiceCollection`, call the extension method, and verify expected service registrations via `serviceDescriptor.ServiceType` and `ServiceLifetime`.

**Estimated total:** ~50-66 tests

**Dependencies:** P0. For `AzureSearchClientWrapper`, inspect whether it depends on `ISearchClientFactory` (defined in `Azure/Search/`) — if so, mock that interface directly.

**File/symbol scope for concurrency:** Each test file is independent. All 9 can run in parallel after P0.

---

### P3 — Azure Search (8 untested code files)

**Objective:** Achieve 90%+ coverage on untested search service files in `Azure/Search/`.

**Source files (all under `4-Persistence/MotorcycleRAG.Persistence/Azure/Search/`):**

| Source file | Test file (under `.../MotorcycleRAG.API.Tests/Azure/Search/`) | Mock dependencies | Est. tests | Difficulty |
|-------------|--------------------------------------------------------|-------------------|------------|------------|
| `AzureSearchDocumentService.cs` | `AzureSearchDocumentServiceTests.cs` | `ISearchClientFactory`, `ILogger<>`, `IResilienceService`, `ICorrelationService` | 8-10 | Easy |
| `AzureSearchHealthService.cs` | `AzureSearchHealthServiceTests.cs` | `ISearchClientFactory`, `ILogger<>` | 5-7 | Easy |
| `AzureSearchQueryService.cs` | `AzureSearchQueryServiceTests.cs` | `ISearchClientFactory`, `ILogger<>`, `IResilienceService`, `ICorrelationService` | 10-12 | Easy |
| `ChunkIndexingService.cs` | `ChunkIndexingServiceTests.cs` | `ISearchClientFactory`, `ILogger<>`, `IResilienceService` | 8-10 | Easy |
| `InMemorySearchShimChunkIndexingService.cs` | `InMemorySearchShimChunkIndexingServiceTests.cs` | None (in-memory shim, pure logic) | 5-7 | Easy |
| `SearchClientFactory.cs` | `SearchClientFactoryTests.cs` | `IOptions<AzureFoundryOptions>`, `SearchIndexClient` (Azure SDK) | 6-8 | Moderate |
| `SearchCredential.cs` | `SearchCredentialTests.cs` | None (static factory, verify it returns non-null credential) | 2-3 | Easy |
| `SearchIndexResiliencePipelineProvider.cs` | `SearchIndexResiliencePipelineProviderTests.cs` | `ISearchIndexResiliencePipeline` (or Polly pipeline verification) | 4-6 | Moderate |

**Files not requiring tests:** `ISearchClientFactory.cs`, `ISearchIndexResiliencePipeline.cs` (interfaces, no implementation).

**Test pattern:** Most services accept `ISearchClientFactory` — mock it to return a `Mock<SearchClient>`. Note: `SearchClient` is an Azure SDK class; verify its methods are virtual before mocking. If not virtual, test through the factory's interface contract and verify the service calls the expected factory methods.

**For `SearchClientFactory`:** It takes `SearchIndexClient` as a constructor parameter. `SearchIndexClient.GetIndexAsync` is virtual in the Azure SDK — mock it to test `IndexExistsAsync` (returns true on success, false on 404 `RequestFailedException`). The `GetClient`/`GetIndexName`/`GetDefaultClient` methods are pure logic (endpoint + naming convention) — test directly.

**Estimated total:** ~48-63 tests

**Dependencies:** P0. No dependency on P2 (different files).

**File/symbol scope for concurrency:** Each test file is independent. All 8 can run in parallel after P0.

---

### P4 — Azure Blob (1 untested file)

| Source file | Test file | Mock dependencies | Est. tests | Difficulty |
|-------------|-----------|-------------------|------------|------------|
| `Azure/Blob/BlobManualPageAssetStore.cs` | `MotorcycleRAG.API.Tests/Azure/Blob/BlobManualPageAssetStoreTests.cs` | `BlobServiceClient` or `BlobContainerClient` (Azure SDK), `ILogger<>`, `IOptions<>` | 5-7 | Moderate |

**Dependencies:** P0. Independent of P2/P3.

---

### P5 — DataProcessing (3 untested files)

| Source file | Test file | Mock dependencies | Est. tests | Difficulty |
|-------------|-----------|-------------------|------------|------------|
| `DataProcessing/DisabledPdfProcessor.cs` | `MotorcycleRAG.API.Tests/DataProcessing/DisabledPdfProcessorTests.cs` | None (likely a disabled stub) | 3-4 | Easy |
| `DataProcessing/MotorcycleCSVProcessor.cs` | `MotorcycleRAG.API.Tests/DataProcessing/MotorcycleCSVProcessorTests.cs` | `ILogger<>`, stream/string input | 6-8 | Easy |
| `DataProcessing/MotorcyclePDFProcessor.cs` | `MotorcycleRAG.API.Tests/DataProcessing/MotorcyclePDFProcessorTests.cs` | `ILogger<>`, `IOptions<>`, possibly PDF library | 6-8 | Moderate |

**Dependencies:** P0. Independent of other tasks.

---

### P6 — ExternalServices (2 untested files)

| Source file | Test file | Mock dependencies | Est. tests | Difficulty |
|-------------|-----------|-------------------|------------|------------|
| `ExternalServices/FabricPipelineService.cs` | `MotorcycleRAG.API.Tests/ExternalServices/FabricPipelineServiceTests.cs` | `IAzureFoundryClient`, `ISearchClientFactory` or `IChunkIndexingService`, `ILogger<>`, `IResilienceService` | 8-10 | Moderate |
| `ExternalServices/LocalPipelineService.cs` | `MotorcycleRAG.API.Tests/ExternalServices/LocalPipelineServiceTests.cs` | `IEmbeddingService`, `ISearchClientFactory`, `ILogger<>` | 8-10 | Moderate |

**Dependencies:** P0. Independent of other tasks.

---

### P7 — HealthChecks (5 untested files)

| Source file | Test file | Mock dependencies | Est. tests | Difficulty |
|-------------|-----------|-------------------|------------|------------|
| `HealthChecks/AzureFoundryHealthCheck.cs` | `MotorcycleRAG.API.Tests/HealthChecks/AzureFoundryHealthCheckTests.cs` | `IAzureFoundryClient` or `IHttpClientFactory`, `ILogger<>` | 4-5 | Easy |
| `HealthChecks/AzureOpenAIHealthCheck.cs` | `MotorcycleRAG.API.Tests/HealthChecks/AzureOpenAIHealthCheckTests.cs` | `IAzureFoundryClient`, `ILogger<>` | 4-5 | Easy |
| `HealthChecks/AzureSearchHealthCheck.cs` | `MotorcycleRAG.API.Tests/HealthChecks/AzureSearchHealthCheckTests.cs` | `ISearchClientFactory`, `ILogger<>` | 4-5 | Easy |
| `HealthChecks/DocumentIntelligenceHealthCheck.cs` | `MotorcycleRAG.API.Tests/HealthChecks/DocumentIntelligenceHealthCheckTests.cs` | `IDocumentIntelligenceClient` or wrapper, `ILogger<>` | 4-5 | Easy |
| `HealthChecks/SqlDatabaseHealthCheck.cs` | `MotorcycleRAG.API.Tests/HealthChecks/SqlDatabaseHealthCheckTests.cs` | `ISqlConnectionFactory`, `ILogger<>` | 4-5 | Easy |

**Test pattern:** Each implements `IHealthCheck.CheckHealthAsync`. Mock dependencies to return healthy/unhealthy states. Verify `HealthCheckResult` status (`Healthy`, `Degraded`, `Unhealthy`) and description text. Use a mock `HealthCheckContext` if needed.

**Estimated total:** ~20-25 tests

**Dependencies:** P0. Independent of other tasks.

---

### P8 — Local (1 untested file)

| Source file | Test file | Mock dependencies | Est. tests | Difficulty |
|-------------|-----------|-------------------|------------|------------|
| `Local/OpenAiCompatibleChatClient.cs` | `MotorcycleRAG.API.Tests/Local/OpenAiCompatibleChatClientTests.cs` | `IHttpClientFactory` (via `Moq.Contrib.HttpClient`), `ILogger<>`, `IOptions<>` | 6-8 | Moderate |

**Dependencies:** P0. Independent of other tasks.

---

### P9 — Notifications (1 untested file)

| Source file | Test file | Mock dependencies | Est. tests | Difficulty |
|-------------|-----------|-------------------|------------|------------|
| `Notifications/ApproverNotificationService.cs` | `MotorcycleRAG.API.Tests/Notifications/ApproverNotificationServiceTests.cs` | `IEmailService` or notification abstraction, `ILogger<>`, `IOptions<>` | 5-7 | Easy |

**Dependencies:** P0. Independent of other tasks.

---

### P10 — Resilience (2 untested files)

| Source file | Test file | Mock dependencies | Est. tests | Difficulty |
|-------------|-----------|-------------------|------------|------------|
| `Resilience/CorrelationService.cs` | `MotorcycleRAG.API.Tests/Resilience/CorrelationServiceTests.cs` | None (likely `AsyncLocal<string>` based, pure logic) | 5-7 | Easy |
| `Resilience/ResilienceService.cs` | `MotorcycleRAG.API.Tests/Resilience/ResilienceServiceTests.cs` | `ILogger<>`, Polly pipeline (verify retry/fallback behavior with mock delegates) | 8-10 | Moderate |

**Test pattern for ResilienceService:** Verify that `ExecuteAsync` calls the primary delegate, applies retry on failure (with mock delegate that fails then succeeds), and invokes the fallback delegate when all retries are exhausted.

**Dependencies:** P0. Independent of other tasks. Note: `ResilienceService` and `CorrelationService` are dependencies of many other services (P2, P3, P6), but since they are mocked in those tests, there is no test-level dependency. However, completing P10 early provides confidence in the mocking setup.

---

### P11 — Search (1 untested file)

| Source file | Test file | Mock dependencies | Est. tests | Difficulty |
|-------------|-----------|-------------------|------------|------------|
| `Search/MotorcycleIndexingService.cs` | `MotorcycleRAG.API.Tests/Search/MotorcycleIndexingServiceTests.cs` | `ISearchClientFactory` or `IChunkIndexingService`, `IAzureFoundryClient`, `ILogger<>`, `IResilienceService` | 6-8 | Moderate |

**Dependencies:** P0. Independent of other tasks.

---

### P12 — Telemetry (2 untested files)

| Source file | Test file | Mock dependencies | Est. tests | Difficulty |
|-------------|-----------|-------------------|------------|------------|
| `Telemetry/IngestionTelemetryService.cs` | `MotorcycleRAG.API.Tests/Telemetry/IngestionTelemetryServiceTests.cs` | `TelemetryClient` (ApplicationInsights), `ILogger<>` | 5-7 | Moderate |
| `Telemetry/TelemetryService.cs` | `MotorcycleRAG.API.Tests/Telemetry/TelemetryServiceTests.cs` | `TelemetryClient`, `ILogger<>` | 5-7 | Moderate |

**Test pattern:** `TelemetryClient` methods are virtual in the ApplicationInsights SDK — mock with Moq. Verify that telemetry events are tracked with the expected properties and metrics.

**Dependencies:** P0. Independent of other tasks.

---

### P13 — Sql DI Extension (1 untested file)

| Source file | Test file | Mock dependencies | Est. tests | Difficulty |
|-------------|-----------|-------------------|------------|------------|
| `Sql/ServiceCollectionExtensions.cs` | `MotorcycleRAG.API.Tests/Sql/SqlServiceCollectionExtensionsTests.cs` | `IServiceCollection` (real `ServiceCollection` instance) | 3-4 | Easy |

**Test pattern:** Create a real `ServiceCollection`, call `AddSqlPersistence` (or equivalent), verify expected service registrations.

**Dependencies:** P0. Independent of other tasks.

---

## 5. Sequencing / dependency graph

```
P0 (Prerequisites, incl. coverage-config.json correction)
 |
 +---> P1 (SQL Repos, 16 files — can subdivide into 4 parallel groups of 4)
 +---> P2 (Azure Root, 9 files — all parallel)
 +---> P3 (Azure Search, 8 files — all parallel)
 +---> P4 (Azure Blob, 1 file)
 +---> P5 (DataProcessing, 3 files — all parallel)
 +---> P6 (ExternalServices, 2 files — parallel)
 +---> P7 (HealthChecks, 5 files — all parallel)
 +---> P8 (Local, 1 file)
 +---> P9 (Notifications, 1 file)
 +---> P10 (Resilience, 2 files — parallel)
 +---> P11 (Search, 1 file)
 +---> P12 (Telemetry, 2 files — parallel)
 +---> P13 (Sql DI, 1 file)
```

**Rules:**
- P0 is strictly sequential and blocks all other tasks — including the `coverage-config.json` correction (D3), since `run_unit_coverage.py --suite persistence-unit` will fail/point at a nonexistent project until it's fixed.
- P1-P13 are fully parallelizable after P0 completes. No task in P1-P13 depends on another P1-P13 task, because each tests a different source file and uses mocked dependencies — every source file across all 13 groups is disjoint (verified against the source inventory in §3).
- P1 is the largest task (~120-140 tests across 16 files). It can be subdivided into 4 concurrent sub-groups of 4 repositories each to maximize throughput, with no file-level conflicts.
- Recommended order: **P1 first** (validates the shared `FakeDbConnection` infrastructure from P0 against the most reference-rich pattern before other groups rely on it), then P10 (Resilience) and P7 (HealthChecks) next — they are simple and exercise the `Moq`-based mocking patterns that P2/P3/P6 depend on conceptually — then the remainder (P2–P6, P8–P9, P11–P13) in any order/parallel.

---

## 6. Residual decisions / risks

### Residual decisions

**RD1 — Move existing Persistence tests from UnitTests?** D1 recommends leaving them. If the team later wants a single home for all Persistence tests, a separate cleanup task can migrate the 7 existing test files from `MotorcycleRAG.UnitTests/Persistence/` to `MotorcycleRAG.API.Tests/`, update namespaces, remove `4-Persistence/` from the `dotnet-unit` suite's sourceRoots, and remove the IVT entry for `MotorcycleRAG.UnitTests` from the Persistence csproj. Owner: user decision post-implementation.

**RD2 — Coverage exclusion for DI extension methods.** `ServiceCollectionExtensions.cs` files (Sql, Azure) have low testable logic (service registration). If 90% coverage is infeasible for these files, the team may need to add per-file exclusions to `coverlet.runsettings` (`ExcludeByFile`) or refactor the registration logic. Owner: test-dev agent during P13/P2 — measure actual coverage first.

**RD3 — Project naming/location mismatch (new, from the Q3 resolution).** `5-Test/MotorcycleRAG.API.Tests` tests exclusively `MotorcycleRAG.Persistence` classes and has zero `MotorcycleRAG.API` `ProjectReference`. It also sits directly under `5-Test/` rather than under `5-Test/tests/` alongside its closest siblings (`MotorcycleRAG.UnitTests`, `MotorcycleRAG.IntegrationTests`, `MotorcycleRAG.EndToEndTests`, `MotorcycleRAG.LoadTests`). Both properties will read as mislabeled/misplaced to future contributors browsing `5-Test/`. This was an explicit, twice-confirmed user decision — not blocking — but is recorded here so a future rename/relocation (if ever desired) has documented rationale. If a future plan adds actual `MotorcycleRAG.API` coverage work, it can land in this same project without a further rename.

### Risks

**R1 — Azure SDK client mocking (HIGH).** Several Azure SDK classes (`SearchClient`, `SearchIndexClient`, `BlobServiceClient`, `BlobContainerClient`, `DocumentIntelligenceClient`) may have non-virtual methods or sealed classes that Moq cannot mock. **Mitigation:** Check each SDK class's API before writing tests. If a class is un-mockable, either (a) test through the injected factory interface (`ISearchClientFactory`), (b) use a wrapper/subclass pattern if the class has protected virtual members, or (c) flag the file as requiring a production-code refactor to introduce an abstraction (escalate to user before refactoring).

**R2 — FoundryAgentRunner (HIGH).** `FoundryAgentRunner` wraps `Azure.AI.Projects.Agents` which is a preview SDK (`Azure.AI.Projects.Agents` 2.0.0). The SDK surface may be unstable or un-mockable. **Mitigation:** Inspect the class's constructor dependencies. If it creates SDK clients internally, coverage may be limited without a production-code refactor. Flag as a risk and target partial coverage with a note.

**R3 — Coverage threshold is per-file, not aggregate (MEDIUM).** Even if aggregate coverage exceeds 90%, individual files below 90% will fail the policy (`aggregate_coverage.py` checks per-file and per-class). Every source file must independently reach 90%. **Mitigation:** Run `python3 5-Test/scripts/run_unit_coverage.py --suite persistence-unit` after each task group and check the weakest-files report.

**R4 — `TreatWarningsAsErrors` inherited (LOW).** The Persistence project has `TreatWarningsAsErrors=true`. The test project must compile cleanly. **Mitigation:** Ensure all test code follows the same analyzer rules. The test csproj should not enable `TreatWarningsAsErrors` unless required (the existing UnitTests csproj does not set it).

**R5 — FakeDbConnection fidelity (MEDIUM).** The shared `FakeDbConnection` must support all Dapper patterns used across 16 repositories (query single, query multiple, execute, execute scalar, multi-result, output parameters). The existing inline copies may have slight variations. **Mitigation:** Consolidate the most complete implementation from `AccessRequestRepositoryTests.cs` (927 lines, the most comprehensive). If a repository needs a new fake behavior, extend the shared class rather than re-duplicating.

**R6 — Stale `coverage-config.json` entry if P0 skips the correction (MEDIUM, new).** The `persistence-unit` suite entry currently on disk points at the old (superseded) project name/path. If P0 doesn't explicitly correct it (D3), coverage tooling will silently fail to find the project. **Mitigation:** P0's acceptance criteria explicitly require confirming no remaining reference to `MotorcycleRAG.Persistence.Tests` in `coverage-config.json`.

---

## 7. Out of scope

- **Moving existing Persistence tests** from `MotorcycleRAG.UnitTests/Persistence/` to the new project (see RD1).
- **Production-code refactoring** to improve testability (e.g., extracting Azure SDK clients behind new interfaces). If a source file is un-testable without refactoring, escalate to the user; do not refactor as part of this plan.
- **Integration tests** against real Azure services or a real SQL database. Those belong in `MotorcycleRAG.IntegrationTests`.
- **Performance/load testing.** Belongs in `5-Test/tests/MotorcycleRAG.LoadTests/`.
- **Test coverage for other layers** (Domain, Application, API). This plan is scoped to `MotorcycleRAG.Persistence` only — despite the new project's name (see RD3).
- **Modifying the existing `dotnet-unit` suite** or removing `4-Persistence/` from its sourceRoots (D1/D3 leave it in place).
- **Renaming/relocating the project** to better reflect its actual contents (see RD3) — deferred to the user.

---

## 8. Required skills

| Skill | Used by tasks | Reason |
|-------|--------------|--------|
| `test-dev` | P0, P1-P13 | xUnit test authoring, FluentAssertions assertions, Moq setup, FakeDbConnection infrastructure |
| `dotnet-dev` | P0, P2, P3 | .NET project setup, csproj configuration, Azure SDK integration patterns, dependency injection |
| `code-review` | Post-implementation | Review test correctness, pattern adherence, and coverage verification |
| `docs-dev` | Post-implementation | Plan closeout: update `6-Docs/catalog.md`, Persistence documentation, and archive the plan |

---

## 9. Verification harness

### Per-task verification

After each task group (P1-P13), the implementing agent SHALL:

1. Run `dotnet test 5-Test/MotorcycleRAG.API.Tests/MotorcycleRAG.API.Tests.csproj --logger trx` — all tests must pass with zero failures.
2. Run `dotnet build 5-Test/MotorcycleRAG.API.Tests/MotorcycleRAG.API.Tests.csproj -warnaserror` — must succeed with zero warnings.

### Coverage verification

After all task groups are complete:

1. Run `python3 5-Test/scripts/run_unit_coverage.py --suite persistence-unit` — the `persistence-unit` suite must pass with 90%+ file-line and class-line coverage for all in-scope `4-Persistence/` files.
2. Run `python3 5-Test/scripts/run_unit_coverage.py` (all suites) — verify no regressions in the `dotnet-unit` suite and that the merged coverage report shows 90%+ for Persistence.
3. Inspect `TestResults/UnitCoverage/CoverageReport/coverage-summary.md` — zero threshold failures for `4-Persistence/` files.

### Code review

- `code-reviewer` agent SHALL review all test code for correctness, pattern adherence (matching `AccessRequestRepositoryTests.cs` conventions), and absence of anti-patterns (no `Thread.Sleep`, no shared mutable state, no skipped tests without justification).
- `security-review` is not required (test-only changes, no production code modifications, no secrets).

### Plan closeout

After implementation verification is complete, a `docs-dev` agent SHALL:
1. Verify all acceptance criteria against implementation evidence.
2. Update `6-Docs/catalog.md` to include the new test project.
3. Update `6-Docs/MotorcycleRAG.Persistence/` documentation if test patterns or coverage tooling changed.
4. Update `6-Docs/plans/README.md` and archive this plan under `6-Docs/archive/plans/` with status `Archived`.

### Final acceptance criteria

- [ ] `MotorcycleRAG.API.Tests` project builds and runs with zero warnings and zero failures.
- [ ] `persistence-unit` coverage suite reports 90%+ file-line and class-line coverage for all in-scope `4-Persistence/` source files.
- [ ] No regressions in `dotnet-unit` or any other existing coverage suite.
- [ ] `MotorcycleRAG.sln` includes the new project.
- [ ] `MotorcycleRAG.Persistence.csproj` declares IVT for `MotorcycleRAG.API.Tests`.
- [ ] `coverage-config.json`'s `persistence-unit` suite entry points at `5-Test/MotorcycleRAG.API.Tests/` (no remaining reference to the superseded `MotorcycleRAG.Persistence.Tests` name/path).
- [ ] Code review approved by `code-reviewer`.
- [ ] Plan closeout completed by `docs-dev`.
