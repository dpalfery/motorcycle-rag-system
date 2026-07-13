# Contracts.Models Unit-Testing & Coverage Strategy

**Status:** Archived (decisions D1–D3 approved, implemented, and code-reviewed; documentation synced)
**Date:** 2026-07-13
**Goal:** Decide a defensible, low-waste strategy for unit-testing and coverage-measuring `3-Domain/MotorcycleRAG.Contracts.Models/`, and resolve the fate of `5-Test/MotorcycleRAG.Contracts.Tests/Contracts/Models/DTOs/RecordCoverageTests.cs`.

---

## 1. Problem / Motivation

The repo owner questions the value of unit-testing `Contracts.Models`, which the scoped `AGENTS.md` defines as *"shared, data-only DTOs that cross boundaries… free of interfaces, implementations, framework dependencies, and business invariants."* A coverage run measured XPlat line-rate **0.0787 (7.9%, 131/1664 lines)** for a slice spanning Core + Domain + Contracts + Contracts.Models, and `RecordCoverageTests.cs` looks like it exists to pad the number.

**Root cause (verified against live config, not speculation):** the padding is *policy-induced*, not a testing-philosophy choice.

- `5-Test/scripts/coverage-config.json` sets `thresholds.fileLinePercent = 85.0` and `classLinePercent = 85.0`.
- `5-Test/scripts/aggregate_coverage.py` returns exit code 1 if **any single in-scope file OR class** is below 85% — i.e. the gate is **per-file and per-class**, not aggregate.
- The `dotnet-unit` suite's `sourceRoots` include `3-Domain/`, so `Contracts.Models` is fully in-scope, and `coverageExclusions` (`/Migrations/`, `.Designer.cs`, `.generated.cs`, `obj/`, `bin/`, `5-Test/` prefixes) do **not** exclude it.
- `coverlet.runsettings` `ExcludeByAttribute` is `Obsolete,GeneratedCode` only. Auto-property getters/setters are `[CompilerGenerated]`, which is **not excluded** — so every `{ get; set; }` / `{ get; init; }` accessor is a coverable line. A pure DTO with no test has ~0% line coverage and fails the per-file gate.
- A repo-rooted `5-Test/scripts/auto_generate_coverage_tests.py` exists that auto-writes tests for the weakest-covered files via the Codex CLI — direct evidence the system is already compensating for this structural pressure.

**Consequence:** the per-file 85% gate, applied to a data-only layer whose accessors count as coverable, *forces* every DTO to be instantiated and exercised just to keep CI green. That is the source of `RecordCoverageTests.cs`. The fix must therefore happen at the coverage-policy level, not merely by deleting tests.

---

## 2. Recommended decisions (pending owner approval — architectural)

Per repo `AGENTS.md` ("Ask before an architectural decision; present the trade-offs"), **none of these are enacted by this plan.** They are presented for owner sign-off. Each is reversible and config/doc-only unless noted.

- **D1 (recommended):** Stop gating `Contracts.Models` line coverage at the project level. Add the project to the coverage exclusion set so the 85% per-file gate no longer applies to pure DTOs. Preferred mechanism: extend `coverage-config.json` `coverageExclusions.pathContains` with `"/MotorcycleRAG.Contracts.Models/"` **and** add `<Exclude>[MotorcycleRAG.Contracts.Models]*</Exclude>` to `coverlet.runsettings`, so both the coverlet collector and the aggregator agree. (Trade-offs and alternatives in §5.)
- **D2 (recommended):** Keep `Contracts.Models` in the build/test/compile pipeline and keep **targeted behavior tests** for the type-B surface only (§3). Correctness of behavior stays asserted via `dotnet test`; only the *coverage number* is no longer a gate input for this project.
- **D3 (recommended):** Trim — do not wholesale delete — `RecordCoverageTests.cs`: keep the assertions that exercise real behavior (factory methods, computed properties), delete the property-setter/getter round-trip assertions, and add the one genuine behavior gap (`WebSourceValidationResult`). Rename the file to reflect behavior, not coverage.

If D1 is rejected, D3 alone is insufficient: deleting/trimming property tests will re-trigger per-file gate failures on the now-uncovered pure DTOs. The options in §5 cover that contingency.

---

## 3. Investigation findings

### 3.1 Type classification — `3-Domain/MotorcycleRAG.Contracts.Models/`

~110 source `.cs` files (excl. `bin/`/`obj/`); several files declare multiple types (e.g. `DTOs/McpToolDtos.cs` = 4 classes, `DTOs/DetailedPipelineMetrics.cs` = 7 types, `DTOs/ChunkIndexingResult.cs` = 2 records, `DTOs/Citation.cs` = class + enum).

**Type-A (pure data carriers): the overwhelming majority (~100+ types).** Auto-properties (`{ get; set; }`/`{ get; init; }`), positional records, constant/initializer defaults (`= string.Empty`, `= new()`, `= DateTime.UtcNow`, `= 1024*1024*1024`, `= Environment.ProcessorCount`), and data/serialization attributes. No logic. Includes request/response DTOs, enums, and DTOs that *carry* semantically-derived values whose computation lives in the populating service (e.g. `IngestionJobStatusResponse.IsComplete`/`FillRate` are plain `init` props — the DTO itself computes nothing).

**Type-B (meaningful behavior) — the full set (7 types):**

| # | File | Type | Behavior |
|---|------|------|----------|
| 1 | `DTOs/ModelValidationResult.cs` | `ModelValidationResult` | Private ctor; static factories `Success()`, `Failure(IEnumerable<string>)`, `Failure(string)`; computed `ErrorMessage => IsValid ? "Validation passed" : string.Join("; ", Errors)`; null-coalescing in `Failure`. |
| 2 | `Serialization/MotorcycleCategoryJsonConverter.cs` | `MotorcycleCategoryJsonConverter : JsonConverter<MotorcycleCategory>` | `Read` (case-insensitive parse, throws `JsonException` on invalid → enforces 4-value invariant), `Write` (canonical lowercase wire string). **Already well-tested.** |
| 3 | `DTOs/Optimization/BatchProcessingStatistics.cs` | `BatchProcessingStatistics` | Computed `AverageThroughputPerSecond` (division + zero-guard), `SuccessRate` (ratio + zero-guard). |
| 4 | `DTOs/BatchPipelineResult.cs` | `BatchPipelineResult` | Computed `IsCompleted => EndTime.HasValue`, `HasErrors => Failed > 0 \|\| ProcessedWithErrors > 0`. |
| 5 | `DTOs/DetailedPipelineMetrics.cs` | `ExecutionMetrics` (nested) | Computed `SuccessRate => TotalExecutions > 0 ? SuccessfulExecutions/Total*100 : 0`. |
| 6 | `DTOs/PipelineRunStatusResult.cs` | `PipelineRunStatusResult` | Static factory `FromStatus(string)`. |
| 7 | `DTOs/Web/WebSourceValidationResult.cs` | `WebSourceValidationResult` | Computed `QualityMultiplier => Math.Max(0.5f, QualityScore)`; private `GetTierMultiplier(WebTrustTier)` switch; static factories `Approved(...)` (with `ArgumentNullException.ThrowIfNull` guard), `Rejected(...)`, `ApprovedWithError(...)`. **Not tested today — genuine gap.** |

Note: `DTOs/IngestionJobConfiguration.cs` is itself logic-free (Type-A) but is the binding site for `[JsonConverter(typeof(MotorcycleCategoryJsonConverter))]`; its wire contract is already covered by `Contracts/IngestionJobConfigurationCategorySerializationTests.cs`.

### 3.2 Verdict on `RecordCoverageTests.cs` — **trim, do not delete**

It is **partially valuable**, despite a name that implies pure padding. Per-member:

- `ModelValidationResult_FactoryMethods_Work` — **KEEP.** Tests `Success`/both `Failure` overloads, `ErrorMessage` branches, null-input handling. Real behavior.
- `BatchProcessingStatistics_Properties_Work` — **KEEP (behavior), drop nothing here.** Tests the two computed properties incl. the zero-denominator branch. Real behavior.
- `BatchPipelineResult_Properties_Work` — **KEEP.** Tests `IsCompleted`/`HasErrors` computed properties and non-null collection defaults. Real behavior.
- `DetailedPipelineMetrics_Properties_Work` — **PARTIAL.** The `ExecutionMetrics.SuccessRate` assertion is real behavior; the `Assert.NotNull(...)` checks on collection defaults are weak contract-invariant checks (marginally useful). Keep the `SuccessRate` assertions; the `NotNull` lines are optional.
- `PipelineRunStatusResult_Properties_Work` — **TRIM.** The `FromStatus(...)` call is real; the four property-set/get round-trips are pure padding.
- `AgentRunStatus_Constructor_SetsProperties` — **TRIM/DELETE.** A positional record ctor; the assertions just re-state the constructor signature. `Assert.Same` on the list adds near-zero value.

Net: ~3.5 of 6 facts are real behavior; ~2.5 are padding. And the richest untested type (`WebSourceValidationResult`) is not touched at all — confirming the file optimizes for line coverage, not correctness.

### 3.3 Repo testing/coverage policy (verified)

- **Enforced gate:** yes. PR gate `build-test` runs `run_unit_coverage.py` (suites `dotnet-unit`/`python-unit`/`admindesktop-unit`); nightly `unit-tests` aggregates at 90%. Failures are per-file/per-class and fail the job. See `6-Docs/DevOps/overview.md` §4.1 (`build-test`).
- **Exclusion mechanism exists:** `coverlet.runsettings` (`ExcludeByFile`, `ExcludeByAttribute`, `Exclude`) and `coverage-config.json` `coverageExclusions` (`pathContains`, `pathPrefixes`). Both must be updated together for an exclusion to take effect in the gate.
- **DTO-layer testing expectations in docs:** `6-Docs/rules/architecture-general.md` §5-Test Layer lists per-layer strategies but says nothing mandating per-DTO coverage. The scoped `5-Test/MotorcycleRAG.Contracts.Tests/AGENTS.md` states the project's purpose as *"Test DTO serialization, model validation, and contract invariants"* — **not** "exercise every property." Property-padding tests therefore deviate from the stated purpose.

### 3.4 Consumer / transitive coverage

A repo-wide scan shows `using MotorcycleRAG.Contracts.Models.DTOs;` across many Application, API, and Persistence services (e.g. `CoverageCalculator`, `ModelValidationService`, `AccessRequestService`, `AuditRepository`, `AzureSearchDocumentService`, etc.). DTOs are pervasively **constructed and consumed** transitively by `Application.Tests`, `API.Tests`, `Persistence.Tests`, and `IntegrationTests`. However:

- **Serialization round-trip coverage is narrow** — concentrated on the single `MotorcycleCategoryJsonConverter` (already tested). Other DTOs are used by value, not round-tripped through `JsonSerializer` in tests.
- Transitive construction covers *some* accessor lines incidentally, but **not uniformly enough to satisfy a per-file 85% gate** on every DTO. So consumer tests validate *correctness* of the DTOs that are actually used, but cannot be relied upon to keep the per-file gate green. This is why option 4 (rely on consumer tests) cannot stand alone.

---

## 4. Options evaluated (with trade-offs)

**Option 1 — Exclude `Contracts.Models` from the coverage metric; delete pure-padding tests.**
- *Pro:* removes the structural pressure at its source; no more test-to-pass-the-gate busywork; matches the AGENTS.md "data-only" definition; config-only, reversible, no DTO source edits.
- *Con:* also removes the coverage signal for the 7 type-B types (their correctness is still asserted by `dotnet test`, just not gated); sets a precedent that other "dumb" layers could ask to be excluded; requires a doc note so future contributors understand *why* it's excluded.
- *How:* `coverage-config.json` → add `"/MotorcycleRAG.Contracts.Models/"` to `coverageExclusions.pathContains`; `coverlet.runsettings` → add `<Exclude>[MotorcycleRAG.Contracts.Models]*</Exclude>`. (Assembly-glob exclude is preferred over `ExcludeByFile` because it is stable under folder moves.)

**Option 2 — Keep targeted tests ONLY on type-B behavior; remove property-only assertions.**
- *Pro:* tests stay meaningful; the genuine gap (`WebSourceValidationResult`) gets filled.
- *Con:* **alone, insufficient** — without Option 1, trimming property tests re-breaks the per-file gate on ~100 pure DTOs. Must be paired with Option 1 (or Option 4 at scale) to avoid regression.
- *How:* edit `RecordCoverageTests.cs` per §3.2; add `WebSourceValidationResultTests` (factories + `QualityMultiplier` + `GetTierMultiplier` tiers + null guard). Keep `IngestionJobConfigurationCategorySerializationTests.cs` as-is.

**Option 3 — Replace direct unit tests with serialization/contract snapshot tests for DTOs that cross serialization boundaries.**
- *Pro:* catches wire-format regressions where they actually bite (renamed JSON keys, converter wiring).
- *Con:* **largely N/A here.** Serialization risk is concentrated on the one converter, which is already tested. Broad snapshot tests over plain DTOs would be the same low-value busywork in a different costume, and snapshots are brittle (the repo's own `auto_generate_coverage_tests.py` prompt explicitly bans snapshot/golden tests).
- *Verdict:* do **not** adopt broadly. Keep only the existing `IngestionJobConfiguration` serialization test; add snapshot/round-trip tests **only** if a future DTO gains a custom converter or non-default `JsonSerializer` wiring.

**Option 4 — Rely on consumer tests (Application/Integration) for transitive coverage; add no direct DTO tests.**
- *Pro:* zero DTO-specific test maintenance; correctness is validated where DTOs are actually used.
- *Con:* consumer coverage is incidental and non-uniform — it will **not** keep the per-file gate green (§3.4), and DTOs used only at the edges may have zero coverage. Also offers no protection for the type-B behavior not exercised by consumers (e.g. `WebSourceValidationResult` factories).
- *Verdict:* acceptable as a *complement* (and the de-facto status quo), not as a standalone strategy.

---

## 5. Recommended strategy

**Adopt D1 + D2 + D3 = Options 1 + 2 combined** (Option 4 already happens transitively; Option 3 stays limited to the one existing converter test).

Rationale tied to repo constraints:
- Clean Architecture treats `Contracts.Models` as the outermost contract surface (data crossing boundaries). The `AGENTS.md` explicitly forbids business invariants here, so there is almost nothing *to* unit-test — which is exactly why a per-file line-coverage gate is the wrong instrument for this layer.
- Excluding it from the *metric* (D1) does not weaken correctness: behavior tests (D2/D3) still run under `dotnet test` and assert the 7 type-B members; consumer tests still exercise DTO construction/use.
- This is the only combination that removes the padding incentive *and* preserves real behavior coverage *and* keeps CI green without per-DTO busywork.

**Contingency if D1 is rejected:** keep `Contracts.Models` in-scope but (a) adopt D3 (trim to behavior, fill `WebSourceValidationResult`), and (b) accept that the remaining property-padding is the cost of the per-file gate — OR lower the per-file threshold for this one project (not supported by current config without code changes; would need a per-source-root threshold feature in `aggregate_coverage.py`, which is out of scope).

---

## 6. Task list (implementation — requires owner approval of D1–D3 and an implementation-capable agent)

This plan does **not** perform any source/config/doc changes. If approved, the work is:

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| 1 | Confirm | Contracts.Models | Run an exhaustive grep sweep for any type-B members missed here (expression-bodied props, `static` factories, `: JsonConverter`, `throw new` in props/ctors). Confirm the 7-type list. | dotnet-dev / test-dev |
| 2 | Config | Coverage tooling | Apply D1: add `"/MotorcycleRAG.Contracts.Models/"` to `coverage-config.json` `coverageExclusions.pathContains` **and** `<Exclude>[MotorcycleRAG.Contracts.Models]*</Exclude>` to `coverlet.runsettings`. | github-devops / test-dev |
| 3 | Test | Contracts.Tests | Apply D3: trim `RecordCoverageTests.cs` per §3.2 (keep factory + computed-property assertions; delete property round-trips); add `WebSourceValidationResultTests`. Optionally rename file/class to behavior-oriented names. | test-dev |
| 4 | Verify | CI | Run `dotnet test --project 5-Test/MotorcycleRAG.Contracts.Tests` and a local `run_unit_coverage.py --suite dotnet-unit` to confirm Contracts.Models no longer appears in threshold failures and the gate stays green. | github-devops / test-dev |
| 5 | Docs | 6-Docs | Update `6-Docs/DevOps/overview.md` §4.1 to document the Contracts.Models coverage exclusion + rationale; align `5-Test/MotorcycleRAG.Contracts.Tests/AGENTS.md` ("serialization, model validation, contract invariants") and `3-Domain/MotorcycleRAG.Contracts.Models/AGENTS.md` if wording implies per-DTO coverage. | docs-dev (app-docs-standard) |

Sequencing: 1 → (2 ∥ 3) → 4 → 5. Task 5 is the plan-closeout/documentation-sync step required by repo governance.

---

## 7. Residual decisions / risks

- **D1 approval is the gate.** Everything else depends on it. Owner must approve excluding a project from the coverage metric (architectural decision per AGENTS.md). *Owner: repo owner.*
- **Exclusion granularity.** The recommended assembly-glob exclude also drops the 7 type-B types from the metric. If the owner wants type-B behavior *gated*, the alternative is per-file `[ExcludeFromCodeCoverage]` on ~100 pure DTOs (noisy, edits every DTO, not recommended) or a future `aggregate_coverage.py` per-source-root threshold (out of scope). *Owner: repo owner.*
- **Precedent risk.** Excluding one layer may invite requests to exclude others (e.g. `Contracts` interfaces). Mitigation: document a narrow, principled criterion — "data-only assemblies with no business logic are excluded; behavior-bearing types within them are still unit-tested." *Owner: architecture maintainers.*
- **`auto_generate_coverage_tests.py`.** With Contracts.Models excluded, this tool will no longer target DTOs. No change needed, but worth noting it currently contributes to the padding pattern. *Owner: dev-experience maintainers.*
- **`coveragereport/` artifact.** A `coveragereport/` HTML folder exists at repo root (seen in the consumer-coverage scan); the 7.9% figure appears to originate there. Not in scope, but confirm it is gitignored and not a stale committed report.

## 8. Out of scope

- Changing the global 85%/90% thresholds or the per-file/per-class gate model itself.
- Adding per-source-root threshold support to `aggregate_coverage.py`.
- Touching the Python/Admin-Desktop/Mobile/WebUI coverage suites.
- Refactoring any DTO source (e.g. moving behavior out of `Contracts.Models`) — that is an architecture question separate from the testing strategy.
- The unrelated `2026-07-13-ioc-di-clean-architecture-remediation.md` plan.

## 9. Required skills

`dotnet-dev`, `test-dev` (xUnit/FluentAssertions), `github-devops` (coverlet/`coverage-config.json`/CI), `app-docs-standard` (doc sync). Mapping these to agents is the orchestrator's job.

## 10. Verification harness

- `dotnet test --project 5-Test/MotorcycleRAG.Contracts.Tests` passes; the trimmed/added behavior tests are green (incl. new `WebSourceValidationResult` cases).
- `python3 5-Test/scripts/run_unit_coverage.py --suite dotnet-unit ...` + `aggregate_coverage.py` shows **zero** `Contracts.Models` files in `thresholdFailures` and the overall gate is PASS.
- `code-reviewer` reviews the test trim + config change; `security-review` not strictly required (no secrets/auth surface), but the config change touches CI gating so a reviewer should confirm no gate is silently weakened for other projects.
- Doc sync (task 5) verified by `docs-dev` against the documentation standard before the plan is archived.

## 11. Closeout notes (archived 2026-07-13)

All three decisions (D1 config, D2 test trim, D3 new `WebSourceValidationResultTests`) were implemented and approved via code review. Canonical documentation updated: `6-Docs/DevOps/overview.md` §4.1 (Coverage exclusions), `5-Test/MotorcycleRAG.Contracts.Tests/AGENTS.md`, and `3-Domain/MotorcycleRAG.Contracts.Models/AGENTS.md`.

**Verification evidence (archival record):**

- `dotnet build -warnaserror` → 0 warnings, 0 errors.
- `dotnet test --project 5-Test/MotorcycleRAG.Contracts.Tests` → 28/28 passed, including the new `WebSourceValidationResult` cases.
- Full pipeline `run_unit_coverage.py --suite dotnet-unit` → all 8 test projects green; 0 `Contracts.Models` class nodes in the merged Cobertura; the exclusion fragment provably cannot match `MotorcycleRAG.Contracts/`. The sibling interfaces-only `MotorcycleRAG.Contracts` and all other assemblies remain fully measured.
- Mechanism applied exactly: `<Exclude>[MotorcycleRAG.Contracts.Models]*</Exclude>` added to `coverlet.runsettings` (existing excludes untouched); `"/MotorcycleRAG.Contracts.Models/"` appended to `coverage-config.json` `coverageExclusions.pathContains` (threshold 85.0 unchanged).
- `RecordCoverageTests.cs` trimmed to real-behavior assertions (CA1861 + CS8600 fixed at root).

**Out-of-scope findings (recorded for separate handling — NOT actioned by this plan):**

1. **Pre-existing coverage debt (79 files).** The PR/nightly coverage gate is currently red independent of this plan: 79 files in API/Application/Core/Domain/Persistence/BFF/AgentProvisioning fall below the 85% per-file/per-class threshold. None are under `Contracts.Models`; the change is proven additive-only. This debt predates and is unrelated to this plan.
2. **`WebSourceValidationResult.ApprovedWithError(string error)` ignores its `error` parameter.** Source-side bug in `3-Domain/MotorcycleRAG.Contracts.Models/` discovered while writing D3 tests. Recorded here for a future fix; not in this plan's scope.

**Environment caveat.** The working tree is mid-refactor under a separate, in-flight plan ([`2026-07-13-ioc-di-clean-architecture-remediation.md`](../../plans/2026-07-13-ioc-di-clean-architecture-remediation.md)). That unrelated work confounds a fresh end-to-end run; the verifications above were confirmed at the code-path and test level and via additive-only proof against the merged Cobertura.
