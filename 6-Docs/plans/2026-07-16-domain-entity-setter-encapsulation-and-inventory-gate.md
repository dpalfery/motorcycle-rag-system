# Domain Entity Setter Encapsulation and Inventory Gate

**Status:** Draft
**Date:** 2026-07-16
**Goal:** Encapsulate the four violating Domain entities behind constructors/`Rehydrate` factories and named behavior methods, convert their Dapper repositories to the `Row`-DTO + `Map` pattern, then enable the skipped `DomainEntities_PostMigrationInventory_ContainsOnlyBehaviorBearingTypes` gate.

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
- **D2 — BikeModel gains `UpdateAliases` instance behavior.** BikeModel currently has only static methods, so it fails the behavior clause even after setters are removed. Add an instance method `UpdateAliases(string? aliases)` (the `Update` prefix satisfies `HasRecognizedDomainBehavior`) that validates, de-dupes/normalizes the alias set, and stamps `UpdatedAtUtc`. This captures the alias-consolidation invariant currently buried in the SQL `MERGE`. BikeModel also keeps its existing `Create` factory and gains a `Rehydrate` factory; identity properties become get-only, `Aliases`/`UpdatedAtUtc` become `private set`.
- **D3 — IngestionJob cleanup includes the database.** Remove the thirteen dead C# properties (see §3) **and** drop the corresponding `[dbo].[IngestionJobs]` columns via a new FluentMigrator migration. The migration must backfill `[CreatedAtUtc]` from `[CreatedAt]`/`[StartTime]` where null, drop the legacy columns, then simplify the `ORDER BY COALESCE([CreatedAtUtc], [CreatedAt], [StartTime])` fragments to `[CreatedAtUtc]`. The deployed-schema-approval gate (repository root rule + 2026-07-13 out-of-scope clause) is satisfied by this plan's approval.
- **D4 — Materialization is Dapper, not EF Core.** Every affected repository currently auto-materializes rows directly into the entity via `connection.QueryFirstOrDefaultAsync<EntityType>`, which relies on the public/init setters. The mandated fix is the proven `WebScrapeRunRepository` template: a private nested `*Row` DTO with `init` properties, `QueryAsync<*Row>`, and a private `Map(row) → Entity.Rehydrate(...)` method. No `DbContext`, `OnModelCreating`, `HasField`, or `UsePropertyAccessMode` is involved.
- **D5 — New focused plan, cross-referenced.** This plan is the implementation authority for the setter + gate work. The 2026-07-13 plan remains `Review required` and is referenced; completing this plan satisfies its criteria 3 and 6, enabling its later closeout.

## 3. Investigation findings

### Violator inventory (verified against live source)

| Entity | File | Setter violation | Behavior present? |
|---|---|---|---|
| `IngestionJob` | `3-Domain/MotorcycleRAG.Domain/Entities/IngestionJob.cs` | many `public set` (Id, IngestionJobId, CreatedAtUtc, CreatedBySubject, InputType, InputRef, ComputeProvider, …) plus `init` on lifecycle fields | Yes — `Complete`, `Fail`, `Cancel`, `UpdateStage`, `MarkDeleting`, `QueueForRetry`, `RecordFailure`, `StartProcessing`, `PauseForMetadata`, `ResumeFromMetadata` |
| `BikeModel` | `3-Domain/MotorcycleRAG.Domain/Entities/BikeModel.cs` | all identity/audit props `public init` | **No** — only static `Create`/`NormalizeName`/`IsAlias` |
| `ManualDocument` | `3-Domain/MotorcycleRAG.Domain/Entities/ManualDocument.cs` | `public init` on identity/immutable props | Yes — `MarkCanonicalized`, `MarkProcessed`, `MarkFailed`, `BeginProcessing`, `SetStage` |
| `McpToolConfiguration` | `3-Domain/MotorcycleRAG.Domain/Entities/McpToolConfiguration.cs` | `public init` on identity + `init => _field` on transition fields | Yes — `Enable`, `Disable`, `UpdateConfiguration`, `UpdateConnectionStatus` |

### IngestionJob dead properties (D3)

These thirteen properties are **never** read by the canonical `IngestionJobStatusMapper.Map`, **never** set by `IngestionJobService.StartJobAsync`, and **absent** from the repository `SELECT`/`INSERT`/`UPDATE` column lists — they are always default values:

`JobId`, `JobType`, `SourceFilePath`, `UserId`, `UserEmail`, `StartTime`, `EndTime`, `CreatedAt`, `UpdatedAt`, `TotalRecordsProcessed`, `RecordsIndexed`, `RecordsFailed`, `RecordsWithWarnings`.

`ComputeProvider` and `DocIngestionRunId` carry misleading "vestigial" comments but are actively set, persisted, and read by the mapper — they **stay**. `ErrorsJson`/`ErrorMessage` are written by `RecordFailure` — they stay.

### Established materialization pattern (D4) — the template

`WebScrapeRunRepository` is the canonical reference. Domain side: private constructor + static `Create` + static `Rehydrate` (validates full persisted state) + get-only/`private set` properties + behavior methods. Repository side: a private nested `*Row` class (Dapper-friendly `init` properties), queries materialize into `*Row`, then a private `Map(*Row row) => Entity.Rehydrate(...)` builds the entity. The `IndexedArtifactRepository.MapToDto(dynamic)` and `WebScrapeRunRepository.Map(WebScrapeRunRow)` helpers are existing precedents.

### Write-site inventory (where each entity's properties are currently set)

- **IngestionJob** — `IngestionJobRepository` auto-materializes (`QueryFirstOrDefaultAsync<IngestionJob>` etc.) and mutates `job.Id` after INSERT; `IngestionJobService.StartJobAsync` uses `new IngestionJob { … }`; `IngestionJobStatusMapper.Map`, `CoverageCalculator`, `GraphIngestionChannel`, `ProcessorArtifactService` read properties; many tests construct via `new IngestionJob { … }`.
- **BikeModel** — `BikeModelRepository` auto-materializes; `SpecsIngestionService` uses `Create`; `QuestionValidationService`, `ManualBikeLinker` read; tests construct via both `Create` and `new BikeModel { … }`.
- **ManualDocument** — `ManualDocumentRepository` auto-materializes (including the multi-map `QueryAsync<ManualDocument, ManualRunDto, …>` in `GetRecentManualOperationsAsync`); `ManualIngestionService.RegisterManualAsync` constructs via a `Map`/initializer then calls `MarkCanonicalized`/`BeginProcessing`; tests construct directly.
- **McpToolConfiguration** — `ToolConfigurationRepository` auto-materializes; `ToolConfigurationService.CloneConfiguration` builds copies via `new McpToolConfiguration { … }` (used by `CreateToolAsync`/`UpdateToolAsync`); `McpConfigurationStore` is in-memory and already uses `Create`; behavior methods (`Enable`/`Disable`/`UpdateConfiguration`/`UpdateConnectionStatus`) are already invoked correctly.

### JSON deserialization

None of the four entities is deserialized into directly. `McpToolConfiguration` is **serialized** for audit logs but never deserialized into the entity; `IngestionJob.MetadataJson` is a string parsed as JSON, not the entity. **No `[JsonConstructor]` is required.**

### Entity cross-references

No entity references another by type (`IngestionJob.ManualDocumentId` is a `Guid?`). The four lanes are file-scope independent.

## 4. Task list

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| 1 | IngestionJob | Azure (read-only) | Schema audit of `[dbo].[IngestionJobs]`: confirm which of the thirteen legacy columns physically exist and whether any rows have `CreatedAtUtc` null but `[CreatedAt]`/`[StartTime]` populated. Output is the authoritative drop-list + backfill decision that gates task 4. | `azure-reader` |
| 2 | IngestionJob | Domain | Refactor `IngestionJob.cs`: private constructor; add `Create` (creation use case) and `Rehydrate` (full-state persistence hydration); convert identity/lifecycle properties to get-only or `private set`; remove the thirteen dead properties (D3). Preserve all behavior methods and all retained properties verbatim. | `dotnet-dev` |
| 3 | IngestionJob | Persistence | Convert `IngestionJobRepository` to the Row+Map pattern: private nested `IngestionJobRow`, `Map(row) → IngestionJob.Rehydrate(…)`. Replace every `QueryAsync<IngestionJob>`/`QueryFirstOrDefaultAsync<IngestionJob>`. Change `CreateAsync` to return a `Rehydrate`d copy carrying the assigned `Id` instead of mutating `job.Id`; verify all callers reassign the result. | `dal-dev` |
| 4 | IngestionJob | Persistence | New FluentMigrator migration (D3): backfill `[CreatedAtUtc]` from `[CreatedAt]`/`[StartTime]`, drop the legacy columns confirmed by task 1, and simplify the `ORDER BY COALESCE([CreatedAtUtc], [CreatedAt], [StartTime])` fragments in `GetLatestByInputRefAsync`/`GetLatestByInputAsync`/`GetLatestByInputRefsAsync`/`GetRecentAsync` to `[CreatedAtUtc]`. Reversible `Down` migration required. | `dal-dev` |
| 5 | IngestionJob | Application + Tests | Update `IngestionJobService.StartJobAsync` to construct via `Create`. Update all tests in `MotorcycleRAG.Application.Tests/Pipeline/*`, `MotorcycleRAG.Domian.Tests/IngestionAndManualModelTests.cs`, `MotorcycleRAG.IntegrationTests/*`, and `MotorcycleRAG.Persistence.Tests/…/IngestionJobRepositoryTests.cs` to use `Create`/`Rehydrate` (or test-local builders) instead of `new IngestionJob { … }`. Remove references to the deleted properties. | `dotnet-dev`, `test-dev` |
| 6 | BikeModel | Domain + Persistence + App | Refactor `BikeModel.cs`: private ctor, keep `Create`, add `Rehydrate`, add `UpdateAliases(string?)` (D2), identity get-only, `Aliases`/`UpdatedAtUtc` `private set`. Convert `BikeModelRepository` to Row+Map; route the `MERGE` alias-update through `UpdateAliases` rather than SQL-only mutation. Update `SpecsIngestionService`, `QuestionValidationService` (reads only), and tests. | `dotnet-dev`, `dal-dev`, `test-dev` |
| 7 | ManualDocument | Domain + Persistence + App | Refactor `ManualDocument.cs`: private ctor, add `Create` + `Rehydrate`, identity get-only, lifecycle `private set`. Convert `ManualDocumentRepository` to Row+Map **including** the multi-map `GetRecentManualOperationsAsync`. Update `ManualIngestionService.RegisterManualAsync` to construct via the factory. Update tests. | `dotnet-dev`, `dal-dev`, `test-dev` |
| 8 | McpToolConfiguration | Domain + Persistence + App | Refactor `McpToolConfiguration.cs`: private ctor, keep `Create`, add `Rehydrate`, identity get-only, transition fields `private set` (backing fields already exist). Convert `ToolConfigurationRepository` to Row+Map. Replace `ToolConfigurationService.CloneConfiguration`'s `new McpToolConfiguration { … }` with `Rehydrate`. Update tests. | `dotnet-dev`, `dal-dev`, `test-dev` |
| 9 | **Gating** | Contracts.Tests | Remove the `Skip` attribute from `DomainEntities_PostMigrationInventory_ContainsOnlyBehaviorBearingTypes` and fix any test fallout. This is the **only** task that touches `ModelClassificationPolicyTests.cs`. | `test-dev` |
| 10 | Review | All | Independent code review (Clean Architecture, encapsulation correctness, no public setters introduced) and security review (migration safety, no PII/secrets in logs). | `code-review`, `security-review` |
| 11 | Closeout | Docs | Verify acceptance criteria against implementation evidence; update `6-Docs/catalog.md`, the Domain entity documentation, and synchronized agent-role guidance if behavior changed; update the plan index; then archive. | `app-docs-standard` (`docs-dev`) |

## 5. Sequencing / dependency graph

- **Parallel, disjoint file scopes (no prereqs):** BikeModel (#6), ManualDocument (#7), McpToolConfiguration (#8). These may run concurrently with each other and with the IngestionJob lane.
- **IngestionJob lane, serialized internally:** #1 → #2 → #3 → #4 → #5. Task 1 (schema audit) gates task 4 (migration). Task 2 (entity) gates task 3 (repo) and task 5 (app/tests).
- **IngestionJob lane vs. the other three:** concurrent — no shared files.
- **Gating task:** #9 (`Skip` removal) runs **only after** #2, #3, #5, #6, #7, #8 each pass their own test suites. It is the single task that edits the shared test file and the single action that flips the gate on.
- **Reviews + closeout:** #10 after #9; #11 after #10.

## 6. Residual decisions / risks

- **Highest risk — Dapper materialization.** Removing setters breaks every `QueryAsync<EntityType>`/`QueryFirstOrDefaultAsync<EntityType>` call site. Mitigation: the proven `WebScrapeRunRepository` Row+Map pattern, applied uniformly. Every read path in each repository must be enumerated; the multi-map `GetRecentManualOperationsAsync` in `ManualDocumentRepository` is an easy one to miss.
- **Schema migration (task 4) — second-highest risk.** The exact set of legacy columns that physically exist is unknown until task 1's audit; some of the thirteen C# properties may never have had corresponding columns. The migration must be conditional on existence and must backfill `CreatedAtUtc` before dropping `[CreatedAt]`/`[StartTime]`, or old-row ordering silently degrades. A reversible `Down` migration is required.
- **`CreateAsync` `Id` write-back.** `IngestionJobRepository.CreateAsync` currently mutates `job.Id` post-INSERT. The new contract returns a `Rehydrate`d copy; every caller must reassign the result (verify `IngestionJobService.StartJobAsync` line ~`job = await _repository.CreateAsync(job, ct)` and every test caller).
- **Test churn.** Every `new <Entity> { … }` in the test suites breaks and must move to `Create`/`Rehydrate` or a test-local builder. The blast-radius summaries enumerate the affected test files.
- **Owner/condition for residual items:** task 1 (`azure-reader`) resolves the column-existence and backfill open question; the implementer of task 4 resolves migration reversibility; `code-review`/`security-review` (task 10) sign off before closeout.

## 7. Out of scope

- Any change to `WebTrustPolicy` or `WebScrapeRun` (already compliant).
- Any change to the test's classification logic (`HasPublicPropertySetter`, `HasRecognizedDomainBehavior`, ADR approval mechanism). Only the `Skip` attribute is removed.
- Removing `ComputeProvider`/`DocIngestionRunId`/`ErrorsJson`/`ErrorMessage` or any other **actively-used** IngestionJob field. Only the thirteen dead properties (§3) are removed.
- Dropping columns from tables other than `[dbo].[IngestionJobs]`.
- Changes to deployed Azure resources, infrastructure, or deployment pipelines beyond the single FluentMigrator migration.
- Reopening the broader DTO-relocation work of the 2026-07-13 plan (its other criteria are already verified).
- Compatibility aliases, fallback adapters, or duplicate legacy models (forbidden by repository policy).

## 8. Required skills

- `dotnet-dev` — Domain entity refactors and Application service/mapper updates.
- `dal-dev` — Dapper repository Row+Map conversions and the FluentMigrator migration.
- `test-dev` — unit, integration, and test-builder updates; the gating `Skip` removal.
- `azure-reader` — read-only schema audit of `[dbo].[IngestionJobs]`.
- `code-review` — architecture and encapsulation review.
- `security-review` — migration safety and logging review.
- `app-docs-standard` (`docs-dev`) — mandatory plan closeout, documentation, and plan-index update.

## 9. Verification harness

- **Per-entity unit tests:** Domain behavior tests under `5-Test/MotorcycleRAG.Domian.Tests/` for each of the four entities must pass, including new coverage for `BikeModel.UpdateAliases` and each entity's `Rehydrate` validation/rejection paths. No coverage exclusions or `CompilerGenerated` attributes added.
- **Repository tests:** `5-Test/MotorcycleRAG.Persistence.Tests/Persistence/Sql/Repositories/{IngestionJob,BikeModel,ManualDocument}RepositoryTests.cs` and `…/Configuration/McpConfigurationStoreTests.cs` must pass with the Row+Map conversions.
- **Application/API tests:** `IngestionJobServiceDualModeTests`, `CoverageCalculatorTests`, `IngestionJobMetadataTests`, `IngestionJobStatusMapperTests`, `ManualIngestionServiceTests`, `McpToolManagerTests`, `McpAdminControllerTests` must pass.
- **Integration tests:** `ManualIngestionApiIntegrationTests`, `IngestionJobStatusIntegrationTests`, and any test that constructs entities must pass against the real database with the migration applied.
- **The gate itself:** `DomainEntities_PostMigrationInventory_ContainsOnlyBehaviorBearingTypes` runs unskipped and passes (zero violations) — this is the primary acceptance signal.
- **Solution-level coverage:** the configured `.NET` coverage suite (`MotorcycleRAG.UnitTests.slnf` + `aggregate_coverage.py`) must continue to meet the per-file/per-class 85% gate for the affected Domain and Persistence files.
- **Build/format:** `dotnet build` and the repo formatting check must be clean.
- **Reviews:** `code-review` approve (Clean Architecture preserved, no public setters, encapsulation correct) and `security-review` approve (migration reversible and safe, no secrets/PII in logs) before `docs-dev` closeout.
- **Closeout:** `docs-dev` verifies every acceptance criterion against evidence, updates `6-Docs/catalog.md` and Domain entity docs, synchronizes the six agent-role surfaces if behavior changed, and updates `6-Docs/plans/README.md` before archiving.
