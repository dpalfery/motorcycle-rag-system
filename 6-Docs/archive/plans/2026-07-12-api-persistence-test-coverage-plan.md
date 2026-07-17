# API Persistence Test Coverage Expansion

**Status:** Archived
**Date:** 2026-07-12

> This incomplete draft had unresolved questions Q1 (scope) and Q2 (test project location). The user resolved both: scope is all `MotorcycleRAG.Persistence` source files (Q1 option c), and a new dedicated project `MotorcycleRAG.Persistence.Tests` at `5-Test/tests/MotorcycleRAG.Persistence.Tests/` (Q2 option b). The finalized plan supersedes this file. Do not use this file as implementation authority.

---

## 1. Problem / Motivation

_To be finalized once scope is confirmed (see §2a). Preliminary finding: the request's premise (47.9% baseline for MotorcycleRAG.API; a single "persistence class" inside that assembly; a new project at `5-Tests/MotorcycleRAG.API.Tests`) does not match the current repository state. See §3 for verified facts._

## 2. Approved decisions

_None yet — pending §2a._

## 2a. Open questions (decision ledger)

| Q# | Question | Options | Recommended | Depends on | Status |
|----|----------|---------|-------------|------------|--------|
| Q1 | Which class(es) constitute the "persistence class" in scope, and what is the corrected coverage baseline/target assembly? | (a) `PersistenceConfiguration.cs` in `MotorcycleRAG.API` — already 100% covered, no work needed. (b) `SqlConnectionFactory.cs` in `MotorcycleRAG.Persistence` — currently 74.6%, small bounded gap to >90%. (c) All `MotorcycleRAG.Persistence.Sql.Repositories.*` classes (the true low-coverage persistence layer, several classes at 0–15%) — large multi-class effort. (d) Other — specify exact file path(s). | (c) — matches the intent of "close persistence coverage gaps to >90%"; (a) is already done, (b) alone leaves the real gap (repositories) untouched. | — | OPEN |
| Q2 | Where should new/expanded tests live? | (a) Extend the existing consolidated `5-Test/tests/MotorcycleRAG.UnitTests` project (already references both `MotorcycleRAG.API` and `MotorcycleRAG.Persistence`, already has `Persistence/Sql/Repositories/*Tests.cs` folder convention). (b) Create a new isolated project at `5-Test/MotorcycleRAG.API.Tests` (or `5-Test/MotorcycleRAG.Persistence.Tests`) as the request's deliverable literally states. | (a) — matches existing repo convention (see `5-Test/tests/MotorcycleRAG.UnitTests/AGENTS.md`), avoids duplicate project scaffolding/package refs, and is where sibling repository tests (`SqlGraphRepositoryTests`, `AccessRequestRepositoryTests`, `IngestionJobRepositoryTests`) already live. | Q1 (scope determines which repositories need net-new test files) | OPEN |

## 3. Investigation findings

- **Coverage baseline contradiction:** The most recent local coverage report (`coveragereport/Summary.txt`, generated 2026-07-12 17:39, covering 7/9–7/12) shows `MotorcycleRAG.API` assembly line coverage at **66.3%**, not 47.9%. No occurrence of "47.9" exists anywhere in the repository. The 47.9% figure could not be corroborated.
- **`PersistenceConfiguration.cs` (`1-Presentation/MotorcycleRAG.API/Configuration/Services/PersistenceConfiguration.cs`)** is the only class in the `MotorcycleRAG.API` assembly literally named/scoped as "persistence." It is a static DI-registration class (single method `AddSqlPersistence`, 32 coverable lines, no branches) and is already at **100% line coverage** per `coveragereport/MotorcycleRAG.API_PersistenceConfiguration.html`.
- **Architectural boundary:** `1-Presentation/MotorcycleRAG.API/AGENTS.md` states "API may depend on Application and Core; do not bypass Application with direct Persistence access." This confirms the API assembly does not contain persistence *logic* — only the DI wiring class above. Actual persistence logic lives in the separate `4-Persistence/MotorcycleRAG.Persistence` project.
- **`MotorcycleRAG.Persistence` assembly** is at **51.4%** overall (per `coveragereport/Summary.txt`). Low-coverage classes most likely to be "the gap" the request intends:
  - `Sql.Repositories.AuditRepository` — 0%
  - `Sql.Repositories.ManualDocumentRepository` — 0%
  - `Sql.Repositories.UserManagementQueryRepository` — 2%
  - `Sql.Repositories.UserRepository` — 2.5%
  - `Sql.Repositories.IndexedChunkRepository` — 3.1%
  - `Sql.Repositories.IndexedArtifactRepository` — 3.4%
  - `Sql.Repositories.UserIdentityRepository` — 4.3%
  - `Sql.Repositories.ToolConfigurationAuditRepository` — 4.6%
  - `Sql.Repositories.PlanRepository` — 5.7%
  - `Sql.Repositories.UsageRepository` — 6%
  - `Sql.Repositories.BikeModelRepository` — 8.1%
  - `Sql.Repositories.WebScrapeRunRepository` — 8.8%
  - `Azure.AzureBlobStorageService` — 9.9%
  - `Sql.Repositories.ToolConfigurationRepository` — 10.1%
  - `Azure.Search.InMemorySearchShimChunkIndexingService` — 11.3%
  - `Sql.Repositories.BikeModelCategoryRepository` — 11.8%
  - `Azure.Search.AzureSearchHealthService` — 12.3%
  - `Sql.Repositories.WebSourceRepository` — 14.2%
  - `Azure.DocumentIntelligenceClientWrapper` — 14.8%
  - `Azure.Search.AzureSearchQueryService` — 14.9%
  - `Local.OpenAiCompatibleChatClient` — 16.4%
  - `Telemetry.TelemetryService` — 10%
  - Already above 90%: `Sql.Repositories.AccessRequestRepository` (99.7%), `Sql.Repositories.IngestionJobRepository` (90.1%), `Sql.Repositories.SqlGraphRepository` (96.9%), `Sql.Repositories.ParameterMergeExtensions` (100%), `Sql.ServiceCollectionExtensions` (100%).
  - `Sql.SqlConnectionFactory` is at **74.6%** and already has a fairly thorough test file (`5-Test/tests/MotorcycleRAG.UnitTests/Persistence/Sql/SqlConnectionFactoryTests.cs`, 9 test cases covering constructor validation, connection creation, command/parameter mapping, and open-connection retry via a `TestableSqlConnectionFactory` subclass). Remaining gap is likely the Polly retry-policy predicate/backoff branches and/or the embedded-credential warning-log branch.
- **Existing test project convention:** `5-Test/tests/MotorcycleRAG.UnitTests` (xUnit 2.9.3 + Moq 4.20.72 + FluentAssertions 8.9.0 + coverlet.collector) is a single consolidated unit-test project referencing `MotorcycleRAG.API`, `MotorcycleRAG.Application`, `MotorcycleRAG.Domain`, `MotorcycleRAG.Contracts`, `MotorcycleRAG.Contracts.Models`, `MotorcycleRAG.Persistence`, `MotorcycleRAG.AgentProvisioning`, and `MotorcycleRAG.Core`. Its folder layout mirrors source layout: `Persistence/Sql/SqlConnectionFactoryTests.cs`, `Persistence/Sql/Repositories/{SqlGraphRepositoryTests,AccessRequestRepositoryTests,IngestionJobRepositoryTests}.cs`, `Presentation/API/Configuration/Services/{AuthorizationPoliciesConfigurationTests,CorsServiceConfigurationTests,RateLimitingServiceConfigurationTests}.cs`. Its `AGENTS.md` scopes it to "pure 0-Base, Application, and Domain logic without database, HTTP, Azure, or filesystem dependencies. Prefer a small fake or mock for an interface implementation" — consistent with mocking `IDbConnection`/`ISqlConnectionFactory` rather than hitting a live SQL Server.
- **No existing `MotorcycleRAG.API.Tests` project** exists anywhere in the repo, and the repo's test root is `5-Test` (singular), not `5-Tests` (plural) as stated in the request's deliverable path.
- Other existing per-app test projects (`5-Test/MotorcycleRag.WebUI.BFF.Tests`, `5-Test/MotorcycleRAG.MobileApp.Tests`) exist only for apps that are *not* already covered by the consolidated `MotorcycleRAG.UnitTests` project (BFF and mobile app are separate deployables). This reinforces that per-project test projects are used only when there isn't already a consolidated project referencing that assembly — which is not the case for `MotorcycleRAG.API` or `MotorcycleRAG.Persistence`.

## 4. Task list

_Pending Q1/Q2 answers — will enumerate one task per repository/class once scope is confirmed, each with: current %, target %, uncovered method list, and mock/test-double strategy (Dapper repositories typically need `IDbConnection`/`IDbCommand` mocks per `.agents/skills/dal-dev/references/dapper-repository.md`)._

## 5. Sequencing / dependency graph

_Pending §4._

## 6. Residual decisions / risks

_Pending._

## 7. Out of scope

_Pending._

## 8. Required skills

_Pending — likely `dotnet-dev` / `dal-dev` (Dapper repository mocking patterns) at minimum._

## 9. Verification harness

_Pending — will include `dotnet test` for `5-Test/tests/MotorcycleRAG.UnitTests` (or new project, per Q2), coverage regeneration via ReportGenerator to confirm >90% on in-scope classes, and `code-reviewer` review._
