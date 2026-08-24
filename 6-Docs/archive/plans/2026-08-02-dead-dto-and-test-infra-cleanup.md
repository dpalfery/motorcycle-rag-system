# Dead DTO and test-infra cleanup

**Status:** Archived 2026-08-02
**Date:** 2026-08-02
**Goal:** Remove confirmed orphaned DTOs and duplicated test infrastructure, and document the two accepted SDK-blocked coverage limitations — without altering production services, coverage policy, or the live `WebScrapeRun`/`WebScrapeRunRepository` path.

---

## 1. Problem / Motivation

A bug-crusher sweep flagged "DTOs below 85 % coverage" and reported 46.51 % / 83.38 % numbers. Architect-v3 investigation confirmed bug-crusher's own `REPRODUCED: no` verdict: the named DTOs live in `MotorcycleRAG.Contracts.Models` and `5-Test/`, both **intentionally excluded** from coverage threshold enforcement by `5-Test/scripts/coverage-config.json:6-22` (`/MotorcycleRAG.Contracts.Models/` and `5-Test/` path prefixes). The coverage framing was therefore misleading; the actionable signal underneath was dead/orphaned code.

CodeGraph (AST-authoritative) confirmed the following have zero non-self references in production code:

- `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/WebSourceCrawlResult.cs` — superseded by the `WebScrapeRun` domain entity + `WebScrapeRunRow` row class. The SQL table `[dbo].[WebSourceCrawlResults]` is mapped to `WebScrapeRun`, not to the DTO.
- `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/Optimization/ConnectionPoolSettings.cs` and `ConnectionPoolStatistics.cs` — zero callers for the two connection-pool DTO types. The `Optimization` namespace itself is **not** unused: it also hosts the live BatchProcessing/VectorCompression DTOs (`BatchProcessingOptions`, `BatchProcessingResult`, `BatchProcessingError`, `BatchProcessingStatistics`, `CompressedVector`, `CompressionStatistics`) and is consumed by `IBatchProcessingService`/`IVectorCompressionService` and their implementations. Only the two connection-pool types are removed; the namespace and folder remain.
- `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/Specifications/EngineSpecificationDto.cs`, `MotorcycleSpecificationDto.cs`, and the sibling DTOs they compose (`PerformanceMetricsDto`, `SafetyFeaturesDto`, `PricingInformationDto`) — referenced only by `TestDataManager.cs` (E2E test infra) and self-composition. No ingestion/search boundary consumes them. Kyber-Weave returned no doc/spec/ADR naming them as a planned feature.
- `5-Test/MotorcycleRAG.EndToEndTests/TestDataManager.cs` — `SaveTestDataAsync` has zero callers; the whole class is dead E2E scaffolding.
- `5-Test/MotorcycleRAG.API.Tests/Persistence/Sql/Helpers/FakeDbConnection.cs` — duplicated by `5-Test/MotorcycleRAG.Persistence.Tests/Persistence/Sql/Repositories/RepositoryFakeInfrastructure.cs:20`. The 300 callers CodeGraph reports are all in Persistence.Tests files; test projects do not cross-reference. The API.Tests copy's "single shared implementation" comment is stale.

Two further coverage gaps are **accepted limitations, not defects**, and are documented rather than refactored:

- `4-Persistence/MotorcycleRAG.Persistence/Azure/FoundryAgentRunner.cs` calls concrete non-virtual Azure SDK methods on `ProjectConversationsClient`, `ProjectOpenAIClient`, and the responses client. The constructor already injects `IFoundryClientFactory`, but the factory return types are concrete SDK clients that Moq cannot intercept. The 27 existing tests cover constructor validation and the static helpers (`BuildAgentVersionMap`, `MapToAgentResponseStatus`).
- `4-Persistence/MotorcycleRAG.Persistence/Telemetry/TelemetryService.cs` consumes `TelemetryClient` (Application Insights SDK) with non-virtual methods. The internal ctor exposes an `Action<ITelemetry>?` observer for testability, but the `MetricTelemetry` pre-aggregation path is SDK-internal. The 99 existing tests cover the reachable surface.

## 2. Approved decisions

These are immutable for the life of this plan.

- **D1 — Remove the orphaned Specifications DTOs and `TestDataManager`.** Delete `EngineSpecificationDto.cs`, `MotorcycleSpecificationDto.cs`, `PerformanceMetricsDto.cs`, `SafetyFeaturesDto.cs`, `PricingInformationDto.cs` (the entire `Specifications/` folder), and `5-Test/MotorcycleRAG.EndToEndTests/TestDataManager.cs`. No planned-feature documentation exists. The local `TestScenario` class nested at the bottom of `TestDataManager.cs` dies with the file (it has no external callers).
- **D2 — Remove `WebSourceCrawlResult`.** Delete `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/WebSourceCrawlResult.cs`. The live data path is `WebScrapeRun` (entity) + `WebScrapeRunRepository` (maps `[dbo].[WebSourceCrawlResults]` rows to `WebScrapeRun`, never to the DTO).
- **D3 — Remove the Optimization connection-pool DTOs.** Delete `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/Optimization/ConnectionPoolSettings.cs` and `ConnectionPoolStatistics.cs`, and the `Optimization/` folder once empty.
- **D4 — Remove the API.Tests `FakeDbConnection` duplicate; document the FoundryAgentRunner and TelemetryService coverage limitations as accepted.** Do **not** refactor those production services, do **not** change coverage exclusions, do **not** alter `WebScrapeRun`/`WebScrapeRunRepository`. FoundryAgentRunner/TelemetryService abstraction work (e.g. an `IFoundryAgentOperations` wrapper) is explicitly out of scope; if desired it must be its own architect-v3 plan.

## 3. Investigation findings

- `5-Test/scripts/coverage-config.json:6-22` — coverage exclusions confirmed: `pathContains` includes `/MotorcycleRAG.Contracts.Models/`; `pathPrefixes` includes `5-Test/`. The reported coverage percentages for the named DTOs cannot be reproduced because the DTOs are outside measured scope. Removing them does **not** improve or change any coverage metric.
- CodeGraph blast-radius for `WebSourceCrawlResult` showed no production references; the similarly named SQL table is mapped via `WebScrapeRunRepository.Map` (`WebScrapeRunRepository.cs:181-190`) to `WebScrapeRun.Rehydrate`, never to the DTO.
- CodeGraph blast-radius for `ConnectionPoolSettings` / `ConnectionPoolStatistics` showed zero callers in any layer.
- CodeGraph blast-radius for `EngineSpecificationDto` / `MotorcycleSpecificationDto` showed only `TestDataManager.GetTestMotorcycleSpecificationsAsync` (`TestDataManager.cs:81-174`) and self-composition references.
- `TestDataManager.SaveTestDataAsync` (`TestDataManager.cs:317`) had zero callers in CodeGraph.
- CodeGraph reported `FakeDbConnection` (`5-Test/MotorcycleRAG.API.Tests/Persistence/Sql/Helpers/FakeDbConnection.cs:22`) with 300 callers — all in `5-Test/MotorcycleRAG.Persistence.Tests/` files, which use their own `MotorcycleRAG.UnitTests.Persistence.Sql.Repositories.FakeDbConnection` (`RepositoryFakeInfrastructure.cs:20`). Test projects do not cross-reference.
- Kyber-Weave `docs_explore` for "MotorcycleSpecification EngineSpecification ConnectionPool Optimization connection pool monitoring planned feature" returned no document above the relevance threshold across 72 considered — no spec, ADR, or governed doc claims these DTOs as a planned feature.
- `FoundryAgentRunner.cs:50,101,118,145` — concrete Azure SDK calls (`_conversationsClient.CreateProjectConversationAsync`, `_conversationsClient.DeleteConversationAsync`, `_openAIClient.GetProjectResponsesClientForAgent`, `responsesClient.CreateResponseAsync`) confirmed non-virtual.
- `TelemetryService.cs:43-54` — internal testability seam confirmed; `:127-129` `MetricTelemetry` SDK pre-aggregation use confirmed.

## 4. Test contract

Every implementation task in this plan is a pure deletion in `MotorcycleRAG.Contracts.Models` or under `5-Test/` (both excluded from coverage), or a documentation update. None has a meaningful behavior-asserting automated test to author first; the orchestrator does **not** sequence a `test-dev` RED pass before these tasks. Each task's contract is the manual/read-only validation named below. Per conductor-v3's no-test rule, silence is not acceptable; the manual gates are mandatory and named.

| Task # | Test project / file | Runner command | Behavior asserted (RED → GREEN) |
|--------|---------------------|----------------|---------------------------------|
| T1 | `no-test` — pure deletion | `dotnet build MotorcycleRAG.sln -c Release` ; `rg "\bWebSourceCrawlResult\b" --type cs` (must return 0) ; `5-Test/scripts/run-comprehensive-tests.sh` (macOS) or `.ps1` (Windows) | Manual validation: (a) whole-solution Release build 0 errors / 0 warnings on all 23 projects; (b) repository grep for the DTO type name `WebSourceCrawlResult` returns 0 hits using the **word-boundary** form `\bWebSourceCrawlResult\b` — the unbounded form is a known false positive because the live plural SQL table `[dbo].[WebSourceCrawlResults]` (referenced by `WebScrapeRunRepository` and its tests) contains the DTO name as a substring and must not be renamed; (c) `dotnet-unit` suite passes at prior baseline (no regression). |
| T2 | `no-test` — pure deletion | `dotnet build MotorcycleRAG.sln -c Release` ; `rg "\bConnectionPoolSettings\b|\bConnectionPoolStatistics\b" --type cs` (must return 0) ; `5-Test/scripts/run-comprehensive-tests.sh` | Manual validation: (a) whole-solution Release build 0 errors / 0 warnings; (b) repository grep for the two DTO **type names** `ConnectionPoolSettings` / `ConnectionPoolStatistics` returns 0 hits using the **word-boundary** form; the `MotorcycleRAG.Contracts.Models.DTOs.Optimization` namespace term is **not** part of the acceptance grep because that namespace is live — it also hosts the consumed BatchProcessing/VectorCompression DTOs, which are out of scope; (c) `dotnet-unit` suite passes at prior baseline. |
| T3 | `no-test` — pure deletion | `dotnet build MotorcycleRAG.sln -c Release` ; `rg "EngineSpecificationDto\|MotorcycleSpecificationDto\|PerformanceMetricsDto\|SafetyFeaturesDto\|PricingInformationDto\|TestDataManager\|MotorcycleRAG.Contracts.Models.DTOs.Specifications\|TestScenario" --type cs` (must return 0) ; `5-Test/scripts/run-comprehensive-tests.sh` | Manual validation: (a) whole-solution Release build 0 errors / 0 warnings; (b) repository grep for all five Specifications DTO names, `TestDataManager`, the `Specifications` namespace, and the local `TestScenario` class returns 0 hits; (c) `dotnet-unit` suite passes at prior baseline (the EndToEndTests project, if it still compiles, has the same passing set as before — the deletions only remove an uncalled class). |
| T4 | `no-test` — pure deletion (test project) | `dotnet build MotorcycleRAG.sln -c Release` ; `dotnet test --project 5-Test/MotorcycleRAG.API.Tests --filter "Category!=Integration&Category!=AzureIntegration"` ; `rg "FakeDbConnection" --type cs 5-Test/MotorcycleRAG.API.Tests` (must return 0) | Manual validation: (a) whole-solution Release build 0 errors / 0 warnings; (b) `MotorcycleRAG.API.Tests` non-integration subset passes at prior baseline; (c) grep for `FakeDbConnection` under `5-Test/MotorcycleRAG.API.Tests/` returns 0 hits (the `Helpers/` folder is removed if empty). The Persistence.Tests copy at `RepositoryFakeInfrastructure.cs:20` is untouched. |
| T5 | `no-test` — documentation only | docs reviewer against `6-Docs/documentation-standard.md` | Manual validation: canonical documentation updated per the repository documentation standard, recording the accepted coverage limitations for `FoundryAgentRunner` (non-virtual Azure SDK client methods; abstraction deferred) and `TelemetryService` (non-virtual `TelemetryClient`; `MetricTelemetry` pre-aggregation SDK-internal). docs-dev must use `mcp__kyber-weave__docs_explore` first to locate the owning canonical doc(s); the documentation standard governs frontmatter and `code-refs`. |
| T6 | `no-test` — plan closeout | docs reviewer against this plan's §5 acceptance criteria and `6-Docs/plans/README.md` | Manual validation: docs-dev verifies every T1–T5 acceptance criterion against implementation evidence, updates any affected canonical documentation, maintains the plan index, and archives the plan to `6-Docs/archive/plans/` with status `Archived`. Required by AGENTS.md "Plan and specification closeout". |

## 5. Task list

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| T1 | Cleanup | Contracts.Models | Delete `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/WebSourceCrawlResult.cs`. Acceptance: file removed; `rg "\bWebSourceCrawlResult\b" --type cs` returns 0 (word-boundary form; the live plural SQL table `[dbo].[WebSourceCrawlResults]` is a known false positive for the unbounded form and is out of scope); whole-solution Release build clean; `dotnet-unit` suite at prior baseline. Test-contract row T1. | dotnet-dev, code-reviewer |
| T2 | Cleanup | Contracts.Models | Delete `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/Optimization/ConnectionPoolSettings.cs` and `ConnectionPoolStatistics.cs`; remove the `Optimization/` folder if empty. Acceptance: both files removed; `rg "\bConnectionPoolSettings\b|\bConnectionPoolStatistics\b" --type cs` returns 0 (word-boundary type-name form; the `MotorcycleRAG.Contracts.Models.DTOs.Optimization` namespace term is **not** asserted — the namespace is live via the BatchProcessing/VectorCompression DTOs and is out of scope, so the folder is retained); whole-solution Release build clean; `dotnet-unit` suite at prior baseline. Test-contract row T2. | dotnet-dev, code-reviewer |
| T3 | Cleanup | Contracts.Models + EndToEndTests | Delete the entire `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/Specifications/` folder (`EngineSpecificationDto.cs`, `MotorcycleSpecificationDto.cs`, `PerformanceMetricsDto.cs`, `SafetyFeaturesDto.cs`, `PricingInformationDto.cs`) and `5-Test/MotorcycleRAG.EndToEndTests/TestDataManager.cs` (including its local `TestScenario` class). Acceptance: all six files removed; `rg "EngineSpecificationDto\|MotorcycleSpecificationDto\|PerformanceMetricsDto\|SafetyFeaturesDto\|PricingInformationDto\|TestDataManager\|MotorcycleRAG.Contracts.Models.DTOs.Specifications" --type cs` returns 0; whole-solution Release build clean; `dotnet-unit` suite at prior baseline. Test-contract row T3. | dotnet-dev, code-reviewer |
| T4 | Cleanup | API.Tests | Delete `5-Test/MotorcycleRAG.API.Tests/Persistence/Sql/Helpers/FakeDbConnection.cs`. If the `Helpers/` directory becomes empty, remove it. Acceptance: file removed; `rg "FakeDbConnection" --type cs 5-Test/MotorcycleRAG.API.Tests` returns 0; whole-solution Release build clean; `MotorcycleRAG.API.Tests` non-integration subset at prior baseline. The Persistence.Tests `FakeDbConnection` (`RepositoryFakeInfrastructure.cs:20`) and the `AccessRequestRepositoryTests.cs` nested helper, if any, are not in scope. Test-contract row T4. | dotnet-dev, code-reviewer |
| T5 | Docs | Canonical component docs | Record the accepted coverage limitations for `FoundryAgentRunner` (`4-Persistence/MotorcycleRAG.Persistence/Azure/FoundryAgentRunner.cs`) and `TelemetryService` (`4-Persistence/MotorcycleRAG.Persistence/Telemetry/TelemetryService.cs`) in the owning canonical documentation. Use `mcp__kyber-weave__docs_explore` first to locate the doc(s); follow `6-Docs/documentation-standard.md` for frontmatter and `code-refs`. Do not describe these as defects — they are SDK-testability constraints accepted by D4. Acceptance: docs reviewer confirms the standard is met and the limitations are attributed to non-virtual Azure SDK / Application Insights SDK methods, not to dead code. Test-contract row T5. | docs-dev |
| T6 | Closeout | Plan index + canonical docs | Plan closeout per AGENTS.md "Plan and specification closeout". Verify T1–T5 acceptance criteria against implementation evidence, update affected canonical documentation, maintain `6-Docs/plans/README.md`, and move this plan to `6-Docs/archive/plans/` with status `Archived` only after the verification gate (§10) passes. Test-contract row T6. | docs-dev |

## 6. Sequencing / dependency graph

- **T1, T2, T3, T4, T5** have disjoint file scopes (Contracts.Models/DTOs/WebSourceCrawlResult.cs, Contracts.Models/DTOs/Optimization/, Contracts.Models/DTOs/Specifications/ + EndToEndTests/TestDataManager.cs, API.Tests/.../Helpers/FakeDbConnection.cs, and canonical docs respectively). They may run in parallel.
- **T6 (closeout)** depends on T1–T5 all reaching ACCEPTED. It is the final task.
- Because every task is `no-test`, there is no RED → GREEN gate to sequence. The mandatory pre-merge gate for T1–T4 is the §10 verification harness (whole-solution Release build, grep assertions, `dotnet-unit` baseline); for T5 it is the docs-reviewer check; for T6 it is the closeout verification.
- If the orchestrator observes that the EndToEndTests project (touched by T3) becomes empty of source files after `TestDataManager.cs` is removed, T3 must additionally verify the project still builds (an empty xUnit project is valid) or flag it for a follow-up project-removal task — out of scope here.

## 7. Residual decisions / risks

- **Public-API surface change.** `MotorcycleRAG.Contracts.Models` is a published contract assembly. Although CodeGraph confirms zero internal consumers for the DTOs being removed, an external consumer (other repository, downstream service) could in principle reference them. Risk assessed as negligible because no governed doc, spec, or ADR mentions them and they have been unreferenced internally. Owner of any regression: the consuming team.
- **Stale comment cleanup not in scope.** `RepositoryFakeInfrastructure.cs:8-12` describes its `FakeDbConnection` as "Shared fake ADO.NET infrastructure for SQL repository unit tests" — accurate for Persistence.Tests. The stale "single shared implementation" comment lives on the API.Tests file being deleted in T4, so it dies with the file.
- **EndToEndTests project state after T3.** If `TestDataManager.cs` is the only source file in `5-Test/MotorcycleRAG.EndToEndTests/`, T3 leaves an empty project. This plan does not remove the project file or its `csproj` reference from `MotorcycleRAG.sln` (or the unit-test solution filter, if present); that is left to a follow-up if the orchestrator decides an empty project is undesirable. Risk: low — empty test projects build and run zero tests cleanly.
- **FoundryAgentRunner / TelemetryService abstraction.** The decision to defer is recorded in D4. If future work lifts their coverage via wrapper interfaces, that effort supersedes the documentation added in T5; T5's prose should be written so it does not over-commit (describe the *current* state, not a permanent prohibition).

## 8. Out of scope

- **Refactoring `FoundryAgentRunner` or `TelemetryService` for testability** (e.g. introducing `IFoundryAgentOperations` or wrapping `TelemetryClient`). D4 explicitly defers this. If pursued, it requires its own architect-v3 plan with real RED tests because it adds new contract surfaces and Persistence-layer wiring.
- **Changing coverage exclusions** in `5-Test/scripts/coverage-config.json` or `coverlet.runsettings`. The exclusions of `/MotorcycleRAG.Contracts.Models/` and `5-Test/` are intentional and governed by the archived `2026-07-13 Contracts.Models testing & coverage strategy` plan. This plan does not modify them.
- **Altering `WebScrapeRun`, `WebScrapeRunRepository`, the `[dbo].[WebSourceCrawlResults]` SQL table, or any live persistence path.** `WebSourceCrawlResult.cs` is the *DTO* being deleted; the *table* and the *entity* that maps to it are live and untouched.
- **Removing the `AccessRequestRepositoryTests.cs` nested `FakeDbConnection` helper** (visible at `AccessRequestRepositoryTests.cs:754` in CodeGraph). Bug-crusher did not flag it; it is private nested test scaffolding local to that test file and not part of the duplicate-of-record finding. Touched only if code-reviewer identifies it as collateral.
- **Removing the EndToEndTests project file or its solution entry.** See residual risk above.
- **Backfilling tests for the removed DTOs.** They have no behavior to test; they are being deleted.

## 9. Required skills

The orchestrator maps these to the specialist agent that performs each task; this list does not pre-assign agents.

- `dotnet-dev` — deletions, whole-solution Release build verification, grep acceptance, identifying any leftover `using` directives or compile breakage in T1–T4.
- `docs-dev` — T5 canonical-doc update (using Kyber-Weave for discovery and following the documentation standard); T6 plan closeout per AGENTS.md.
- `code-reviewer` — verification gate (§10) for T1–T4: confirms no orphaned references, no broken namespaces, build clean.

## 10. Verification harness

This plan is done only when **all** of the following pass:

1. **Test-contract validation (T1–T4).** Every `no-test` row's manual validation is satisfied:
   - `dotnet build MotorcycleRAG.sln -c Release` reports 0 errors and 0 warnings across all 23 projects.
   - Each task's `rg` acceptance grep returns 0 hits, using the **corrected word-boundary/type-name forms** recorded in §4: T1 `\bWebSourceCrawlResult\b`; T2 `\bConnectionPoolSettings\b|\bConnectionPoolStatistics\b`; T3 the Specifications DTO type names + `TestDataManager` + `TestScenario` (no word-boundary adjustment needed — the namespace string form matches nothing and returns 0); T4 `FakeDbConnection` scoped to `5-Test/MotorcycleRAG.API.Tests`. The originally drafted unbounded forms are known false positives: the T1 pattern matches the live plural SQL table `[dbo].[WebSourceCrawlResults]`, and the T2 namespace term matches the live BatchProcessing/VectorCompression DTOs in the `Optimization` namespace. Neither live artifact was renamed or deleted.
   - `5-Test/scripts/run-comprehensive-tests.sh` (macOS) or `.ps1` (Windows) completes with the `dotnet-unit` suite passing at the prior baseline (no regression, no newly-skipped tests).
2. **code-reviewer gate.** `code-reviewer` returns APPROVED for T1–T4 collectively (or per task if the orchestrator sequences separately). The review must verify: (a) the deleted symbols had no remaining references; (b) no `using` directives dangle; (c) no project file (`csproj`) still lists a deleted file; (d) the `Optimization/` and `Specifications/` folders and the API.Tests `Helpers/` folder are removed if empty.
3. **security-review gate.** Not anticipated to apply (pure deletions, no new code paths, no secret surface). If the orchestrator's policy requires it for any cross-cutting change, the reviewer should confirm the deletions do not weaken redaction (TelemetryService is not being modified) or auth surface.
4. **docs-reviewer gate (T5).** Canonical documentation for FoundryAgentRunner and TelemetryService records the accepted coverage limitations per the documentation standard.
5. **Plan closeout (T6).** Per AGENTS.md "Plan and specification closeout", docs-dev verifies T1–T5 evidence, updates canonical docs, maintains `6-Docs/plans/README.md`, and archives this plan to `6-Docs/archive/plans/2026-08-02-dead-dto-and-test-infra-cleanup.md` with status `Archived`. The plan may not be marked `Completed` or archived until the docs gate passes.
6. **Azure validation.** Not applicable. No production resource, schema, or runtime behavior changes — this is pure dead-code removal and documentation. No `azure-reader` task is required.

---

## 11. Closeout (docs-dev, 2026-08-02)

**Status:** Archived 2026-08-02 — every acceptance criterion in this plan's §5 and §10 verification harness was verified against implementation evidence on 2026-08-02. T1–T4 code-reviewer APPROVED; T5 docs-reviewer gate passed against `6-Docs/documentation-standard.md`; canonical documentation updated; plan index maintained; this plan is historical, not implementation authority.

### Evidence verified at closeout

**T1 — `WebSourceCrawlResult` DTO deleted.**
- `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/WebSourceCrawlResult.cs` removed (git status `D`).
- Corrected acceptance grep `rg "\bWebSourceCrawlResult\b" --type cs` → **0 hits**.
- Known false positive: the unbounded form `rg "WebSourceCrawlResult"` matches the live plural SQL table `[dbo].[WebSourceCrawlResults]` in `WebScrapeRunRepository.cs:41,81,118,136,160` and `WebScrapeRunRepositoryTests.cs:40,72`. That table is live and untouched; the acceptance grep is the word-boundary form.

**T2 — Optimization connection-pool DTOs deleted.**
- `ConnectionPoolSettings.cs` and `ConnectionPoolStatistics.cs` removed (git status `D`).
- Corrected acceptance grep `rg "\bConnectionPoolSettings\b|\bConnectionPoolStatistics\b" --type cs` → **0 hits**.
- The `Optimization/` folder was **retained** because it still hosts live BatchProcessing/VectorCompression DTOs (`BatchProcessingOptions`, `BatchProcessingResult`, `BatchProcessingError`, `BatchProcessingStatistics`, `CompressedVector`, `CompressionStatistics`) consumed by `IBatchProcessingService`/`IVectorCompressionService`. The namespace is live; only the two connection-pool types were deleted. The originally drafted namespace term `MotorcycleRAG.Contracts.Models.DTOs.Optimization` is a known false positive (13 files) and was removed from the acceptance check.

**T3 — Specifications DTOs + `TestDataManager` deleted.**
- Entire `3-Domain/MotorcycleRAG.Contracts.Models/DTOs/Specifications/` folder removed (all five DTO files: `EngineSpecificationDto`, `MotorcycleSpecificationDto`, `PerformanceMetricsDto`, `SafetyFeaturesDto`, `PricingInformationDto`).
- `5-Test/MotorcycleRAG.EndToEndTests/TestDataManager.cs` removed (git status `D`); local `TestScenario` class died with the file.
- Acceptance grep `rg "EngineSpecificationDto|MotorcycleSpecificationDto|PerformanceMetricsDto|SafetyFeaturesDto|PricingInformationDto|TestDataManager|MotorcycleRAG.Contracts.Models.DTOs.Specifications|TestScenario" --type cs` → **0 hits**.
- EndToEndTests project is not empty after the deletion (still contains `CompleteSystemTests.cs`, `UserJourneyTests.cs`, `EndToEndTestWebApplicationFactory.cs`, `FakeEndToEndAgentOrchestrator.cs`), so the §6/§7 empty-project contingency does not apply.

**T4 — API.Tests `FakeDbConnection` duplicate deleted.**
- `5-Test/MotorcycleRAG.API.Tests/Persistence/Sql/Helpers/FakeDbConnection.cs` removed (git status `D`); the now-empty `Helpers/` directory was removed.
- Acceptance grep `rg "FakeDbConnection" --type cs 5-Test/MotorcycleRAG.API.Tests` → **0 hits**.
- The Persistence.Tests copy at `RepositoryFakeInfrastructure.cs:20` is untouched; the only repository-wide `FakeDbConnection` csproj match is a `NoWarn` comment mentioning the name in `5-Test/MotorcycleRAG.Persistence.Tests/MotorcycleRAG.Persistence.Tests.csproj:10`, not a file reference.
- T4 acceptance test filter re-run at closeout: `dotnet test 5-Test/MotorcycleRAG.API.Tests --filter "Category!=Integration&Category!=AzureIntegration"` → **604 passed, 0 failed, 0 warnings**.

**T5 — Canonical documentation updated (docs-reviewer verified).**
- `6-Docs/rules/architecture-general.md` — added §"Accepted coverage limitations (SDK-blocked)" (lines 396–401) recording the `FoundryAgentRunner` and `TelemetryService` limitations as accepted SDK-blocked constraints, not defects; `code-refs` frontmatter lists `FoundryAgentRunner` and `TelemetryService`.
- `6-Docs/DevOps/overview.md` — §Phase 1 (line 134) records the accepted SDK-blocked limitations with a canonical-record link to the architecture rules.
- `6-Docs/catalog.md` — architecture-layers entry `last-reviewed` bumped to 2026-08-02.
- Facts cross-checked against source via CodeGraph: `FoundryAgentRunner` ctor injects `IFoundryClientFactory` (L28); concrete non-virtual SDK calls confirmed (`CreateProjectConversationAsync` L53, `DeleteConversationAsync` L109, `GetProjectResponsesClientForAgent` L135, `CreateResponseAsync` L145); static helpers `BuildAgentVersionMap` (L192) / `MapToAgentResponseStatus` (L171) exist; `TelemetryService` internal ctor with `Action<ITelemetry>?` observer (L43–46) and `MetricTelemetry` pre-aggregation (L127) confirmed; `FoundryAgentRunnerTests` and `TelemetryServiceTests` test files present.

### Verification gates (§10)

| Gate | Verdict |
| --- | --- |
| (1) Test-contract validation T1–T4 | ✅ Whole-solution Release build **0 errors / 0 warnings (23 projects)** (re-verified at closeout); corrected word-boundary/type-name greps all **0 hits**; comprehensive runner passed — unit **3730 passed / 0 failed / 0 skipped**, integration **117 passed / 11 skipped / 0 failed**, E2E **22 passed / 0 failed** |
| (2) code-reviewer gate | ✅ APPROVED for T1–T4 (no remaining references, no dangling `using`, no csproj still listing a deleted file, folders removed if empty — `Specifications/` and `Helpers/` removed; `Optimization/` correctly retained) |
| (3) security-review gate | ✅ Not applicable — pure deletions, no new code paths, no secret surface (TelemetryService not modified) |
| (4) docs-reviewer gate (T5) | ✅ PASS — canonical docs record the accepted limitations per the documentation standard, attributed to non-virtual Azure SDK / Application Insights SDK methods, not dead code |
| (5) Plan closeout (T6) | ✅ DONE (this section) — canonical docs updated, plan index maintained, plan moved to `6-Docs/archive/plans/` with status `Archived` |
| (6) Azure validation | ✅ Not applicable — no production resource, schema, or runtime behavior changes |

### Canonical guidance replacing this plan

- `6-Docs/rules/architecture-general.md` §Accepted coverage limitations (SDK-blocked) — the accepted `FoundryAgentRunner`/`TelemetryService` coverage limitations.
- `6-Docs/DevOps/overview.md` §Phase 1 — Accepted SDK-blocked limitations (coverage-policy context).
- `6-Docs/catalog.md` — architecture-layers entry reviewed 2026-08-02.

### Residual / out of scope (unchanged)

- FoundryAgentRunner/TelemetryService abstraction work (e.g. `IFoundryAgentOperations`) remains out of scope per D4; if pursued it requires its own architect-v3 plan.
- Coverage exclusions in `5-Test/scripts/coverage-config.json` / `coverlet.runsettings` unchanged.
- Live `WebScrapeRun`/`WebScrapeRunRepository`/`[dbo].[WebSourceCrawlResults]` path unchanged.
