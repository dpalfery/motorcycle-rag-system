# Domain Entity Setter Encapsulation and Inventory Gate

**Status:** Ready
**Date:** 2026-07-16
**Goal:** Encapsulate the four violating Domain entities behind constructors/`Rehydrate` factories and named behavior methods, convert their Dapper repositories to the `Row`-DTO + `Map` pattern, drop IngestionJob's dead C# properties and corresponding legacy DB columns, then enable the skipped `DomainEntities_PostMigrationInventory_ContainsOnlyBehaviorBearingTypes` gate.

---

## 1. Problem / Motivation

The architecture-guard test `DomainEntities_PostMigrationInventory_ContainsOnlyBehaviorBearingTypes` (in `5-Test/MotorcycleRAG.Contracts.Tests/Contracts/ModelClassificationPolicyTests.cs`, line 170) is deliberately skipped:

```
[Fact(Skip = "Enable after domain entity/DTO migration packages 2-5 complete by removing this skip.")]
```

The test scans every type in namespace `MotorcycleRAG.Domain.Entities` and fails a type when **either** clause trips:

1. `HasPublicPropertySetter` — any property whose `SetMethod.IsPublic == true`. Note: a `public init` accessor compiles to a public setter, so `init` is caught the same as `set`.
2. `!HasRecognizedDomainBehavior` — no instance method (non-static, non-abstract, non-special-name, with a body) named `Activate | Archive | Cancel | Complete | Disable | Enable | Fail | Start | Validate`, or starting with `Mark | Transition | Update`.

Because the test is skipped, nothing prevents a new anemic property-bag or a publicly-mutable entity from landing under `Domain.Entities`. The prior rationalization plan (`2026-07-13-domain-entity-dto-rationalization.md`, status `Review required`) verified the DTO relocation but left its acceptance criteria 3 (entity setter bypasses) and 6 (enable the inventory gate) open. This plan completes those two criteria.

Premise correction verified against live source: of the six files under `3-Domain/MotorcycleRAG.Domain/Entities/`, only **four** actually violate the rule. `WebTrustPolicy` and `WebScrapeRun` were already refactored in package 5 of the 2026-07-13 plan — both use a private constructor with `Create`/`Rehydrate` factories, get-only or `private set` properties, and recognized behavior methods (`MarkUpdated`; `Start`/`Complete`/`Fail`/`Cancel`). They are untouched by this plan.

## 2. Approved decisions

- **D1 — Scope is four entities.** Refactor `IngestionJob`, `BikeModel`, `ManualDocument`, and `McpToolConfiguration` only. `WebTrustPolicy` and `WebScrapeRun` are already compliant and are not modified.
- **D2 — BikeModel gains `UpdateAliases` instance behavior.** BikeModel currently has only static methods (`Create`, `NormalizeName`, `IsAlias`, `TitleCase`), so it fails the behavior clause even after setters are removed. Add an instance method `UpdateAliases(string? aliases)` (the `Update` prefix satisfies `HasRecognizedDomainBehavior`) that validates, de-dupes/normalizes the alias set, and stamps `UpdatedAtUtc`. This captures the alias-normalization invariant in the domain. BikeModel also keeps its existing `Create` factory and gains a `Rehydrate` factory; identity properties become get-only, `Aliases`/`UpdatedAtUtc` become `private set`. The `SpecsIngestionService` MERGE path constructs entities via `Create` (which already normalizes) and the repository reads `bikeModel.Aliases` — `private set` is safe because the repository never sets it. No read-modify-write change to the MERGE is required.
- **D3 — IngestionJob cleanup includes the database, delivered as an idempotent T-SQL script.** Remove the thirteen dead C# properties (see §3) **and** drop the corresponding `[dbo].[IngestionJobs]` columns via a new idempotent T-SQL script executed by the `SqlScriptExecutor` DbSetup workflow. **This codebase does not use FluentMigrator** (no package reference, no `[Migration]` classes); the established schema-evolution pattern is runtime `COL_LENGTH` probes and raw `.sql` scripts — see `IngestionJobRepository.HasSqlIdColumnAsync`. The script must backfill `[CreatedAtUtc]` from `[CreatedAt]`/`[StartTime]` where null (conditionally, via `IF COL_LENGTH` guards), drop the legacy columns, then simplify the `ORDER BY COALESCE([CreatedAtUtc], [CreatedAt], [StartTime])` fragments to `[CreatedAtUtc]`. The script is forward-only (columns are dead) and safe regardless of which columns physically exist thanks to the `IF COL_LENGTH('dbo.IngestionJobs', 'ColumnName') IS NOT NULL` guards.
- **D4 — Materialization is Dapper, not EF Core.** Every affected repository currently auto-materializes rows directly into the entity via `connection.QueryFirstOrDefaultAsync<EntityType>`, which relies on the public/init setters. The mandated fix is the proven `WebScrapeRunRepository` template: a private nested `*Row` DTO with `init` properties, `QueryAsync<*Row>`, and a private `Map(row) → Entity.Rehydrate(...)` method. No `DbContext`, `OnModelCreating`, `HasField`, or `UsePropertyAccessMode` is involved.
- **D5 — New focused plan, cross-referenced.** This plan is the implementation authority for the setter + gate work. The 2026-07-13 plan remains `Review required` and is referenced; completing this plan satisfies its criteria 3 and 6, enabling its later closeout.

## 3. Investigation findings

### Violator inventory (verified against live source)

| Entity | File | Setter violation | Behavior present? |
|---|---|---|---|
| `IngestionJob` | `3-Domain/MotorcycleRAG.Domain/Entities/IngestionJob.cs` | `public set` on ~20 properties (Id, IngestionJobId, CreatedAtUtc, CreatedBySubject, InputType, InputRef, ComputeProvider, DocIngestionRunId, ManualDocumentId, SourceFileName, coverage fields, ErrorsJson, ErrorMessage) plus `init => _field` on lifecycle fields | Yes — `StartProcessing`, `PauseForMetadata`, `ResumeFromMetadata`, `UpdateStage`, `Complete`, `Fail`, `Cancel`, `MarkDeleting`, `QueueForRetry`, `RecordFailure`, `RollbackDeletion` |
| `BikeModel` | `3-Domain/MotorcycleRAG.Domain/Entities/BikeModel.cs` | all identity/audit props `public init` (Id, Make, Model, Year, Aliases, CreatedAtUtc, UpdatedAtUtc, CreatedByUserId, UploadRef) | **No** — only static `Create`/`NormalizeName`/`IsAlias`/`TitleCase` |
| `ManualDocument` | `3-Domain/MotorcycleRAG.Domain/Entities/ManualDocument.cs` | `public init` on identity/immutable props (DocumentId, SourceFileName, CanonicalBlobContainer, CanonicalBlobPath, CanonicalBlobUri, SourceContentHash, DocumentType, Make, Model, Year, UploadedAtUtc) | Yes — `MarkCanonicalized`, `MarkProcessed`, `MarkFailed`, `BeginProcessing`, `SetStage` |
| `McpToolConfiguration` | `3-Domain/MotorcycleRAG.Domain/Entities/McpToolConfiguration.cs` | `public init` on identity (Id, ToolId, Name, Description, ServerUrl, ToolType, Version, IsSystemTool, Priority, TimeoutMs, RetryOnFailure, MaxRetries, CreatedAt) + `init => _field` on transition fields (IsEnabled, ConfigurationJson, DisabledReason, LastTestedAt, LastConnectionStatus, UpdatedAt) | Yes — `Enable`, `Disable`, `UpdateConfiguration`, `UpdateConnectionStatus` |

### IngestionJob dead properties (D3)

These thirteen properties are **never** read by the canonical `IngestionJobStatusMapper.Map`, **never** set by `IngestionJobService.StartJobAsync`, and **absent** from the repository `SELECT`/`INSERT`/`UPDATE` column lists — they are always default values:

`JobId`, `JobType`, `SourceFilePath`, `UserId`, `UserEmail`, `StartTime`, `EndTime`, `CreatedAt`, `UpdatedAt`, `TotalRecordsProcessed`, `RecordsIndexed`, `RecordsFailed`, `RecordsWithWarnings`.

`ComputeProvider` and `DocIngestionRunId` carry misleading "vestigial" comments but are actively set, persisted, and read by the mapper — they **stay**. `ErrorsJson`/`ErrorMessage` are written by `RecordFailure` — they stay. `SourceFileName` is actively persisted — it stays.

### IngestionJob.Id is a DB-assigned IDENTITY long

`Id` is `public long Id { get; set; }` — a SQL Server `BIGINT IDENTITY` column. `IngestionJobRepository.CreateAsync` (line 174) mutates `job.Id` post-INSERT: `job.Id = await connection.QuerySingleAsync<long>(...)`. The repository returns the same mutated instance. After encapsulation, `Create` produces an entity with `Id = 0`; `CreateAsync` inserts it, reads back the IDENTITY, and returns a `Rehydrate`d copy with the assigned `Id`. Every caller of `CreateAsync` must reassign the result.

**Implementation note — `Rehydrate` signature width:** IngestionJob's `Rehydrate` will have ~20+ parameters (all persisted fields including `long Id`). The established `WebScrapeRun`/`WebTrustPolicy` pattern uses individual named parameters. The implementer may use a parameter record/object if the team prefers, but the signature must accept the DB-assigned `long Id`.

### Established materialization pattern (D4) — the template

`WebScrapeRunRepository` (`4-Persistence/MotorcycleRAG.Persistence/Sql/Repositories/WebScrapeRunRepository.cs`) is the canonical reference:
- **Domain side:** private constructor + static `Create` + static `Rehydrate` (validates full persisted state) + get-only/`private set` properties + behavior methods.
- **Repository side:** a private nested `*Row` class (Dapper-friendly `init` properties), queries materialize into `*Row`, then a private `Map(*Row row) => Entity.Rehydrate(...)` builds the entity.

The `IndexedArtifactRepository.MapToDto(dynamic)` and `WebScrapeRunRepository.Map(WebScrapeRunRow)` helpers are existing precedents.

### Schema management in this codebase

This codebase has **no FluentMigrator** (verified: `MotorcycleRAG.DbSetup.csproj` has no package reference; no `[Migration]` classes exist anywhere). Schema evolution uses:
- **Pulumi** (`7-Deployment/infrastructure/`) for Azure resource provisioning.
- **`SqlScriptExecutor`** (`7-Deployment/DbSetup/MotorcycleRAG.DbSetup/SqlScriptExecutor.cs`) — executes raw `.sql` files split by `GO` statements, 5-minute per-batch timeout.
- **Runtime `COL_LENGTH` probes** — `IngestionJobRepository.HasSqlIdColumnAsync` already uses `SELECT CASE WHEN COL_LENGTH('dbo.IngestionJobs', 'Id') IS NULL THEN 0 ELSE 1 END` for forward-compatible column detection.

The D3 column-drop script follows this established idempotent pattern.

### Write-site inventory (where each entity's properties are currently set)

- **IngestionJob** — `IngestionJobRepository` auto-materializes (`QueryFirstOrDefaultAsync<IngestionJob>`, `QueryAsync<IngestionJob>`) and mutates `job.Id` after INSERT; `IngestionJobService.StartJobAsync` uses `new IngestionJob { … }`; `IngestionJobStatusMapper.Map`, `CoverageCalculator`, `GraphIngestionChannel`, `ProcessorArtifactService` read properties; many tests construct via `new IngestionJob { … }`.
- **BikeModel** — `BikeModelRepository` auto-materializes (`QueryFirstOrDefaultAsync<BikeModel>`, `QueryAsync<BikeModel>`); `SpecsIngestionService` uses `Create`; `QuestionValidationService`, `ManualBikeLinker` read; tests construct via both `Create` and `new BikeModel { … }`.
- **ManualDocument** — `ManualDocumentRepository` auto-materializes (including the multi-map `QueryAsync<ManualDocument, ManualRunDto, …>` in `GetRecentManualOperationsAsync`); `ManualIngestionService.RegisterManualAsync` constructs via a `Map`/initializer then calls `MarkCanonicalized`/`BeginProcessing`; tests construct directly.
- **McpToolConfiguration** — `ToolConfigurationRepository` auto-materializes; `ToolConfigurationService.CloneConfiguration` builds copies via `new McpToolConfiguration { … }` (used by `CreateToolAsync`/`UpdateToolAsync`); `McpConfigurationStore` is in-memory and already uses `Create`; behavior methods (`Enable`/`Disable`/`UpdateConfiguration`/`UpdateConnectionStatus`) are already invoked correctly.

### JSON deserialization

None of the four entities is deserialized into directly. `McpToolConfiguration` is **serialized** for audit logs but never deserialized into the entity; `IngestionJob.MetadataJson` is a string parsed as JSON, not the entity. **No `[JsonConstructor]` is required.**

### Entity cross-references

No entity references another by type (`IngestionJob.ManualDocumentId` is a `Guid?`). The four lanes are file-scope independent.

## 4. Task list

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| 1 | IngestionJob | Azure (read-only) | Schema audit of `[dbo].[IngestionJobs]`: confirm which of the thirteen legacy columns physically exist and whether any rows have `CreatedAtUtc` null but `[CreatedAt]`/`[StartTime]` populated. Output is the authoritative drop-list + backfill confirmation that informs task 4. With `IF COL_LENGTH` guards the script is safe regardless, but the audit improves confidence and documents the deployed state. | `azure-reader` |
| 2 | IngestionJob | Domain | Refactor `IngestionJob.cs`: private constructor; add `Create` (creation use case — minimal parameters, `Id = 0`) and `Rehydrate` (full-state persistence hydration — all ~20 persisted fields including `long Id`); convert identity/lifecycle properties to get-only or `private set`; remove the thirteen dead properties (D3). Preserve all behavior methods and all retained properties verbatim. Coverage fields (`TotalPages`, `PagesCapturedViewableCount`, etc.) become `private set` and continue to be set by `UpdateStage`. | `dotnet-dev` |
| 3 | IngestionJob | Persistence | Convert `IngestionJobRepository` to the Row+Map pattern: private nested `IngestionJobRow`, `Map(row) → IngestionJob.Rehydrate(…)`. Replace every `QueryAsync<IngestionJob>`/`QueryFirstOrDefaultAsync<IngestionJob>`. Change `CreateAsync` to return a `Rehydrate`d copy carrying the assigned `Id` instead of mutating `job.Id`; verify all callers reassign the result (`IngestionJobService.StartJobAsync` and every test caller). The `HasSqlIdColumnAsync` dual-path SELECT logic can be simplified since the Row DTO replaces direct entity materialization. | `dal-dev` |
| 4 | IngestionJob | Persistence (DDL) | New idempotent T-SQL script (D3) under `7-Deployment/DbSetup/sql/`: backfill `[CreatedAtUtc]` from `[CreatedAt]`/`[StartTime]` where null (conditional via `IF COL_LENGTH`), drop the legacy columns confirmed by task 1 (each guarded by `IF COL_LENGTH(...) IS NOT NULL`), and simplify the `ORDER BY COALESCE([CreatedAtUtc], [CreatedAt], [StartTime])` fragments in `GetLatestByInputRefAsync`/`GetLatestByInputAsync`/`GetLatestByInputRefsAsync`/`GetRecentAsync` to `[CreatedAtUtc]` in the C# repository. Script is forward-only (columns are dead); document the drop-list in a header comment. | `dal-dev` |
| 5 | IngestionJob | Application + Tests | Update `IngestionJobService.StartJobAsync` to construct via `Create`. Update all tests in `MotorcycleRAG.Application.Tests/Pipeline/*`, `MotorcycleRAG.Domian.Tests/IngestionAndManualModelTests.cs`, `MotorcycleRAG.IntegrationTests/*`, and `MotorcycleRAG.Persistence.Tests/…/IngestionJobRepositoryTests.cs` to use `Create`/`Rehydrate` (or test-local builders) instead of `new IngestionJob { … }`. Remove references to the deleted properties. Verify every `CreateAsync` caller reassigns the returned instance. | `dotnet-dev`, `test-dev` |
| 6 | BikeModel | Domain + Persistence + App | Refactor `BikeModel.cs`: private ctor, keep `Create`, add `Rehydrate`, add `UpdateAliases(string?)` (D2 — validates, de-dupes/normalizes, stamps `UpdatedAtUtc`), identity get-only, `Aliases`/`UpdatedAtUtc` `private set`. Convert `BikeModelRepository` to Row+Map (`BikeModelRow` + `Map(row) => BikeModel.Rehydrate(...)`); replace `QueryFirstOrDefaultAsync<BikeModel>` and `QueryAsync<BikeModel>`. The MERGE upsert reads `bikeModel.Aliases` only — `private set` is safe; no change to the MERGE SQL. Update `SpecsIngestionService`, `QuestionValidationService` (reads only), and tests. | `dotnet-dev`, `dal-dev`, `test-dev` |
| 7 | ManualDocument | Domain + Persistence + App | Refactor `ManualDocument.cs`: private ctor, add `Create` + `Rehydrate`, identity get-only (already `private set` on lifecycle fields — keep). Convert `ManualDocumentRepository` to Row+Map **including** the multi-map `GetRecentManualOperationsAsync` (`QueryAsync<ManualDocumentRow, ManualRunDto, …>` then `Map`). Update `ManualIngestionService.RegisterManualAsync` to construct via the factory. Update tests. | `dotnet-dev`, `dal-dev`, `test-dev` |
| 8 | McpToolConfiguration | Domain + Persistence + App | Refactor `McpToolConfiguration.cs`: private ctor, keep `Create`, add `Rehydrate`, identity get-only, transition fields `private set` (backing fields already exist). Convert `ToolConfigurationRepository` to Row+Map. Replace `ToolConfigurationService.CloneConfiguration`'s `new McpToolConfiguration { … }` with `Rehydrate`. Update tests. | `dotnet-dev`, `dal-dev`, `test-dev` |
| 9 | **Gating** | Contracts.Tests | Remove the `Skip` attribute from `DomainEntities_PostMigrationInventory_ContainsOnlyBehaviorBearingTypes` and fix any test fallout. This is the **only** task that touches `ModelClassificationPolicyTests.cs`. | `test-dev` |
| 10 | Review | All | Independent code review (Clean Architecture, encapsulation correctness, no public setters introduced) and security review (DDL script safety, no PII/secrets in logs). | `code-review`, `security-review` |
| 11 | Closeout | Docs | Verify acceptance criteria against implementation evidence; update `6-Docs/catalog.md`, the Domain entity documentation, and synchronized agent-role guidance if behavior changed; update the plan index; then archive. | `app-docs-standard` (`docs-dev`) |

## 5. Sequencing / dependency graph

- **Parallel, disjoint file scopes (no prereqs):** BikeModel (#6), ManualDocument (#7), McpToolConfiguration (#8). These may run concurrently with each other and with the IngestionJob lane.
- **IngestionJob lane, serialized internally:** #1 → #2 → #3 → #4 → #5. Task 1 (schema audit) informs task 4 (DDL script). Task 2 (entity) gates task 3 (repo) and task 5 (app/tests). Task 4 (DDL) may run in parallel with #3 once #1 completes, but the `ORDER BY` simplification in the C# repository (#3/#4) depends on the script being applied.
- **IngestionJob lane vs. the other three:** concurrent — no shared files.
- **Gating task:** #9 (`Skip` removal) runs **only after** #2, #3, #5, #6, #7, #8 each pass their own test suites. It is the single task that edits the shared test file and the single action that flips the gate on.
- **Reviews + closeout:** #10 after #9; #11 after #10.

## 6. Residual decisions / risks

- **Highest risk — Dapper materialization.** Removing setters breaks every `QueryAsync<EntityType>`/`QueryFirstOrDefaultAsync<EntityType>` call site. Mitigation: the proven `WebScrapeRunRepository` Row+Map pattern, applied uniformly. Every read path in each repository must be enumerated; the multi-map `GetRecentManualOperationsAsync` in `ManualDocumentRepository` is an easy one to miss.
- **DDL script (task 4) — second-highest risk.** The exact set of legacy columns that physically exist is unknown until task 1's audit. The `IF COL_LENGTH` guards make the script safe regardless, but the `CreatedAtUtc` backfill must execute before the `[CreatedAt]`/`[StartTime]` columns are dropped, or old-row ordering silently degrades. The script orders operations: backfill first, drop second.
- **`CreateAsync` `Id` write-back.** `IngestionJobRepository.CreateAsync` currently mutates `job.Id` post-INSERT (line 174/181) and returns the same instance. The new contract returns a `Rehydrate`d copy; every caller must reassign the result (verify `IngestionJobService.StartJobAsync` and every test caller). `Id` is `long` (DB IDENTITY), not Guid.
- **Test churn.** Every `new <Entity> { … }` in the test suites breaks and must move to `Create`/`Rehydrate` or a test-local builder. The blast-radius summaries enumerate the affected test files.
- **Forward-only DDL.** The D3 script is forward-only (the 13 columns are dead). A companion rollback script is not required — there is no business reason to restore unused columns. If reversibility is later required, re-adding nullable columns is straightforward.
- **Owner/condition for residual items:** task 1 (`azure-reader`) resolves the column-existence and backfill open question; the implementer of task 4 writes the idempotent guards; `code-review`/`security-review` (task 10) sign off before closeout.

## 7. Out of scope

- Any change to `WebTrustPolicy` or `WebScrapeRun` (already compliant).
- Any change to the test's classification logic (`HasPublicPropertySetter`, `HasRecognizedDomainBehavior`, ADR approval mechanism). Only the `Skip` attribute is removed.
- Removing `ComputeProvider`/`DocIngestionRunId`/`ErrorsJson`/`ErrorMessage`/`SourceFileName` or any other **actively-used** IngestionJob field. Only the thirteen dead properties (§3) are removed.
- Dropping columns from tables other than `[dbo].[IngestionJobs]`.
- Introducing FluentMigrator or any new migration framework. The D3 script is a raw idempotent T-SQL file executed by the existing `SqlScriptExecutor`.
- Changes to deployed Azure resources, infrastructure, or deployment pipelines beyond the single T-SQL script.
- Reopening the broader DTO-relocation work of the 2026-07-13 plan (its other criteria are already verified).
- Compatibility aliases, fallback adapters, or duplicate legacy models (forbidden by repository policy).

## 8. Required skills

- `dotnet-dev` — Domain entity refactors and Application service/mapper updates.
- `dal-dev` — Dapper repository Row+Map conversions and the idempotent T-SQL DDL script.
- `test-dev` — unit, integration, and test-builder updates; the gating `Skip` removal.
- `azure-reader` — read-only schema audit of `[dbo].[IngestionJobs]`.
- `code-review` — architecture and encapsulation review.
- `security-review` — DDL script safety and logging review.
- `app-docs-standard` (`docs-dev`) — mandatory plan closeout, documentation, and plan-index update.

## 9. Verification harness

- **Per-entity unit tests:** Domain behavior tests under `5-Test/MotorcycleRAG.Domian.Tests/` for each of the four entities must pass, including new coverage for `BikeModel.UpdateAliases` and each entity's `Rehydrate` validation/rejection paths. No coverage exclusions or `CompilerGenerated` attributes added.
- **Repository tests:** `5-Test/MotorcycleRAG.Persistence.Tests/Persistence/Sql/Repositories/{IngestionJob,BikeModel,ManualDocument}RepositoryTests.cs` and `…/Configuration/McpConfigurationStoreTests.cs` must pass with the Row+Map conversions.
- **Application/API tests:** `IngestionJobServiceDualModeTests`, `CoverageCalculatorTests`, `IngestionJobMetadataTests`, `IngestionJobStatusMapperTests`, `ManualIngestionServiceTests`, `McpToolManagerTests`, `McpAdminControllerTests` must pass.
- **Integration tests:** `ManualIngestionApiIntegrationTests`, `IngestionJobStatusIntegrationTests`, and any test that constructs entities must pass against the real database with the DDL script applied.
- **The gate itself:** `DomainEntities_PostMigrationInventory_ContainsOnlyBehaviorBearingTypes` runs unskipped and passes (zero violations) — this is the primary acceptance signal.
- **Solution-level coverage:** the configured `.NET` coverage suite (`MotorcycleRAG.UnitTests.slnf` + `aggregate_coverage.py`) must continue to meet the per-file/per-class 85% gate for the affected Domain and Persistence files.
- **Build/format:** `dotnet build` and the repo formatting check must be clean.
- **Reviews:** `code-review` approve (Clean Architecture preserved, no public setters, encapsulation correct) and `security-review` approve (DDL script safe and idempotent, no secrets/PII in logs) before `docs-dev` closeout.
- **Closeout:** `docs-dev` verifies every acceptance criterion against evidence, updates `6-Docs/catalog.md` and Domain entity docs, synchronizes the six agent-role surfaces if behavior changed, and updates `6-Docs/plans/README.md` before archiving.
