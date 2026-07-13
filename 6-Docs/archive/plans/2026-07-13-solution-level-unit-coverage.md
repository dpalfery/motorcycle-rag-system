# Solution-Level .NET Unit Coverage in CI

**Status:** Archived
**Date:** 2026-07-13
**Archived:** 2026-07-13
**Goal:** Produce a complete, correct .NET unit-coverage metric by running unit tests at the solution-filter level instead of per-project, and repair the broken coverage tooling and stale workflow paths that currently prevent that.

> **Closeout note:** All 8 tasks (T1–T8) completed and verified. `MotorcycleRAG.UnitTests.slnf` created at repo root; runner (`run_unit_coverage.py`) supports `suite["solution"]` and writes `coveragePaths` list; aggregator (`aggregate_coverage.py`) reads `coveragePaths` and merges multiple Cobertura files; `coverage-config.json` consolidated to `dotnet-unit`/`python-unit`/`admindesktop-unit` suites with unioned `sourceRoots`; both `pr-gate.yml` and `nightly.yml` use the consolidated suite list and corrected paths. Canonical documentation updated: `6-Docs/DevOps/overview.md` §4 describes solution-level `.slnf` coverage and multi-coverage aggregation; `6-Docs/archive/plans/2026-07-12-persistence-test-coverage.md` annotated with canonical-path reconciliation note.

---

## 1. Problem / Motivation

The user wants a **complete unit-coverage metric** and believes solution-level test execution (against the `.sln`) is the right way to achieve it. Investigation against live source shows the current state cannot deliver a complete or correct metric, for several independent reasons:

1. **The `dotnet-unit` suite is broken.** `5-Test/scripts/coverage-config.json` points `dotnet-unit` at `5-Test/tests/MotorcycleRAG.UnitTests/MotorcycleRAG.UnitTests.csproj`, which **does not exist** anywhere in the repo or in `MotorcycleRAG.sln`. The real .NET unit tests are split across ~8 per-layer test projects.
2. **Solution-level coverage would silently under-report.** `aggregate_coverage.py` parses exactly **one** `coveragePath` per suite (lines ~477–503), and `run_unit_coverage.py::run_dotnet_suite` sets it via `find_latest_file(...)`, returning a **single** file. A `dotnet test <solution> --collect:XPlat` run emits **one `coverage.cobertura.xml` per test project**; only the most-recent would be recorded. This is the central blocker.
3. **Path inconsistency between `5-Test/` and `5-Test/tests/`.** `MotorcycleRAG.sln` registers every test project directly under `5-Test\`. On disk `5-Test/tests/` holds only `MotorcycleRAG.IntegrationTests/`. Workflows and `coverage-config.json` inconsistently reference both, including non-existent paths (`5-Test/tests/MotorcycleRAG.EndToEndTests/`, `5-Test/tests/MotorcycleRAG.LoadTests/`, `5-Test/tests/MotorcycleRAG.Persistence.Tests/`).
4. **`MotorcycleRAG.sln` omits two test projects the pipelines run:** `5-Test/MotorcycleRAG.MobileApp.Tests/` (needs MAUI, macOS only) and `5-Test/MotorcycleRAG.LoadTests/`.
5. **PR-gate `build-output` artifact paths are stale** (pr-gate.yml lines 149–172) — they reference non-existent root locations (`MotorcycleRAG.API/bin/`, `MotorcycleRAG.IntegrationTests/bin/`), so the downstream `integration`/`e2e` jobs that `download-artifact` + `--no-build` target a non-existent layout.
6. **`nightly.yml` omits the shared dependency install** (`5-Test/scripts/requirements.txt`) that `pr-gate.yml` has, yet it invokes the same scripts.
7. **MAUI build trap:** a single `dotnet test MotorcycleRAG.sln` on the Linux runner cannot build `MotorcycleRAG.MobileApp.Tests`.

## 2. Approved decisions

- **D1 — Execution model:** Solution-filter on Linux + mobile split. .NET unit tests run via a **solution filter** (`.slnf`) on Linux for all non-mobile unit projects; `mobileapp-unit` remains a separate per-project run on macOS. Python/Node suites remain separate and are merged by the aggregator. (User-approved.)
- **D2 — Scope:** Workflows + coverage tooling + new `.slnf`. The runner and aggregator MUST change to handle multiple cobertura files per suite, otherwise the metric is wrong. (User-approved.)
- **D3 — Mechanism:** A `.slnf` solution filter is preferred over a bare `--filter`, because it excludes the MAUI/Integration/E2E/Load projects from the **build**, not just test execution — preventing Linux build failures.
- **D4 — Path canonicalization:** Canonical test-project location is `5-Test/<Project>/` (direct), matching `MotorcycleRAG.sln` and the on-disk majority. All `5-Test/tests/<Project>/` references in config and workflows are corrected to `5-Test/<Project>/`. No physical folder moves in this plan.
- **D5 — Suite consolidation:** The broken `dotnet-unit` suite is replaced by a single solution-level `dotnet-unit` suite (targeting the `.slnf`, with the union of all .NET layer `sourceRoots`). The now-redundant `domain-unit`, `bff-unit`, and `persistence-unit` suites are removed (their projects are covered by the solution-level run). `mobileapp-unit`, `python-unit`, `admindesktop-unit`, `webui-unit` remain.
- **D6 — Backward compatibility:** `suite-result.json` carries a new `coveragePaths` (list). Aggregator reads `coveragePaths` if present, else falls back to `[coveragePath]`, so existing single-project suites keep working.
- **D7 — `.slnf` placement:** `MotorcycleRAG.UnitTests.slnf` lives at repository root next to `MotorcycleRAG.sln` (a build-configuration file, consistent with the existing root-level solution; not a new source folder).

## 3. Investigation findings

- **`MotorcycleRAG.sln`** contains these test projects, all under `5-Test\`: IntegrationTests, EndToEndTests, WebUI.BFF.Tests, Core.Tests, API.Tests, Domian.Tests (sic), Application.Tests, Contracts.Tests, AgentProvisioning.Tests, Persistence.Tests. It does **not** contain MobileApp.Tests or LoadTests.
- **`coverlet.runsettings`** excludes test assemblies (`[MotorcycleRAG.*.Test*]*`) and lists production ExcludeByFile entries; it applies unchanged to a solution-level run.
- **`coverage-config.json`** `coverageExclusions.pathPrefixes` already includes `5-Test/`, so test-source coverage is excluded from the metric as intended.
- **`aggregate_coverage.py`** filters every coverage entry through the suite's `sourceRoots` via `normalize_suite_source_path`; a solution-level suite therefore needs the **union** of all .NET layer source roots or files outside a layer's roots are dropped.
- **`nightly.yml`** has no `pip install -r 5-Test/scripts/requirements.txt` step (pr-gate.yml does, line 120–121).
- The archived plan `2026-07-12-persistence-test-coverage.md` references `5-Test/tests/MotorcycleRAG.Persistence.Tests/`, which conflicts with the current on-disk + `.sln` location `5-Test/MotorcycleRAG.Persistence.Tests/`. This is a stale-doc discrepancy for the docs closeout to reconcile.

## 4. Task list

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| T1 | 1 | Solution filter | Create `MotorcycleRAG.UnitTests.slnf` at repo root referencing `MotorcycleRAG.sln` and including only the 8 Linux-buildable unit test projects: API.Tests, Application.Tests, Core.Tests, Contracts.Tests, Domian.Tests, AgentProvisioning.Tests, Persistence.Tests, WebUI.BFF.Tests (paths as in the `.sln`). Exclude IntegrationTests, EndToEndTests, MobileApp.Tests, LoadTests. | dotnet-dev |
| T2 | 1 | Coverage runner | Modify `5-Test/scripts/run_unit_coverage.py::run_dotnet_suite`: (a) target = `suite.get("solution") or suite["project"]`; (b) when `suite["solution"]` set and `suite["filter"]` present, append `--filter <filter>`; (c) collect **all** `coverage.cobertura.xml` under the suite results dir (`sorted(suite_dir.rglob(...))`) and write `coveragePaths` (list) into `suite-result.json`, keeping a single `coveragePath` for backward compat. | python-dev |
| T3 | 1 | Coverage aggregator | Modify `5-Test/scripts/aggregate_coverage.py` main loop (~lines 477–503): build `coverage_path_texts = suite_status.get("coveragePaths") or ([coveragePath] if coveragePath else [])`, then iterate and `parse_cobertura_file` for each existing path. No other aggregation logic changes. | python-dev |
| T4 | 2 | Suite config | Update `5-Test/scripts/coverage-config.json`: replace the `dotnet-unit` suite with a solution-level entry (`"solution": "MotorcycleRAG.UnitTests.slnf"`, `"filter": "Category!=Integration&Category!=AzureIntegration"`, `sourceRoots` = union of `0-Base/`, `1-Presentation/MotorcycleRAG.API/`, `1-Presentation/MotorcycleRag.WebUI.BFF/`, `2-Application/`, `3-Domain/`, `4-Persistence/`, `7-Deployment/AgentProvisioning/`); remove `domain-unit`, `bff-unit`, `persistence-unit`; fix any remaining `5-Test/tests/...` paths to `5-Test/...`. Depends on T1, T2. | python-dev |
| T5 | 3 | PR-gate workflow | Update `.github/workflows/pr-gate.yml`: (a) `build-test` `Run unified unit coverage` `--suite` list → `dotnet-unit python-unit admindesktop-unit`; (b) fix `Upload build output` artifact paths (lines ~149–172) to real locations, adding `5-Test/MotorcycleRAG.IntegrationTests/{bin,obj}` and `5-Test/MotorcycleRAG.EndToEndTests/{bin,obj}` and correcting production project paths under `1-Presentation/`; (c) `integration` job test path → `5-Test/MotorcycleRAG.IntegrationTests/...`; (d) `e2e` job test path + TestData dir → `5-Test/MotorcycleRAG.EndToEndTests/...`. Depends on T4. | github-devops |
| T6 | 3 | Nightly workflow | Update `.github/workflows/nightly.yml`: (a) add `Install shared test-script dependencies` step (`python -m pip install --requirement 5-Test/scripts/requirements.txt`) to `unit-coverage-linux` before running coverage; (b) `unit-coverage-linux` suite list → `dotnet-unit python-unit admindesktop-unit`; (c) fix `integration-tests`, `end-to-end-tests`, `azure-integration-tests`, `load-tests` (test path lines ~216, 263, 313, 364) and NBomber report path (line ~386) from `5-Test/tests/...` → `5-Test/...`. Depends on T4. | github-devops |
| T7 | 4 | Verification | Validate end-to-end: run `python3 5-Test/scripts/run_unit_coverage.py --suite dotnet-unit --results-dir /tmp/uc ...` locally, confirm `suite-result.json` lists multiple `coveragePaths` and the aggregator emits a merged cobertura + summary covering API/Application/Core/Contracts/Domain/AgentProvisioning/Persistence/BFF source files. Then trigger both workflows via `workflow_dispatch` and confirm unit-coverage artifacts contain merged coverage and the gate passes. | test-dev |
| T8 | 5 | Docs closeout | Per AGENTS.md plan-closeout: verify acceptance criteria against implementation evidence; update `6-Docs/DevOps/overview.md` §4 (CI/CD Flow) to describe the solution-level `.slnf` unit run and the multi-coverage aggregation; reconcile the stale `5-Test/tests/MotorcycleRAG.Persistence.Tests/` path references in the archived `2026-07-12-persistence-test-coverage.md` note and `6-Docs/rules/architecture-general.md` 5-Test Layer section; update plan index. | docs-dev |

## 5. Sequencing / dependency graph

```
T1 (.slnf) ─┐
            ├─► T4 (config) ─┬─► T5 (pr-gate.yml)  ─┐
T2 (runner) ─┤               └─► T6 (nightly.yml)   ├─► T7 (verify) ─► T8 (docs)
T3 (aggr.)  ──┘                                     │
   (T2,T3 independent of T1; T4 depends on T1+T2)  │
   T5,T6 depend on T4                               │
   T7 depends on T5+T6                              │
   T8 depends on T7                                 │
```

Order: T1, T2, T3 may proceed in parallel → T4 → (T5 ∥ T6) → T7 → T8.

## 6. Residual decisions / risks

- **Duplicate `5-Test/tests/MotorcycleRAG.IntegrationTests/` folder.** On disk `5-Test/tests/` contains a `MotorcycleRAG.IntegrationTests/` entry while the `.sln` and pipelines (after T5/T6) target `5-Test/MotorcycleRAG.IntegrationTests/`. Verify whether the `tests/` copy is a stale empty/agents folder or a real second project. Owner: implementer during T5; if real, escalate (physical-move decision, currently out of scope).
- **`Category!=Integration&Category!=AzureIntegration` filter assumption.** Confirm unit test projects do not also contain integration-category tests that should be excluded; adjust filter string if the trait taxonomy differs. Owner: test-dev during T7.
- **Codecov upload path.** `nightly.yml` `unit-tests` job uploads `CoverageReport/coverage-merged.cobertura.xml`; confirm the merged file is still produced at that path after T3. Owner: test-dev during T7.
- **`webui-unit` suite** exists in config but is not invoked by either workflow today; left unchanged. Decide later whether to wire it in.

## 7. Out of scope

- Physically moving/consolidating folders under `5-Test/tests/` — separate repo-hygiene migration; this plan only corrects references to canonical `5-Test/<Project>/` paths.
- Adding `MotorcycleRAG.MobileApp.Tests` or `MotorcycleRAG.LoadTests` to `MotorcycleRAG.sln` — would force MAUI on Linux solution builds; mobile stays per-project on macOS, load stays per-project in nightly.
- Renaming the misspelled `MotorcycleRAG.Domian.Tests` → `Domain.Tests` — wide-blast-radius rename; tracked separately.
- Changing the 85% file/class coverage threshold.
- Integration/E2E/Load/Azure-integration execution model (stays per-project `dotnet test <project>`; only paths are corrected).

## 8. Required skills

- `python-dev` — coverage runner + aggregator multi-file support (T2, T3), suite config (T4).
- `dotnet-dev` — `.slnf` authoring and solution/project structure verification (T1).
- `github-devops` — GitHub Actions workflow edits in pr-gate.yml and nightly.yml (T5, T6).
- `test-dev` — end-to-end coverage validation and CI verification (T7).
- `docs-dev` — plan closeout and canonical documentation updates (T8).
- `code-reviewer` — review of all code/config changes before merge.

## 9. Verification harness

- **Local coverage run (T7):** `python3 5-Test/scripts/run_unit_coverage.py --suite dotnet-unit --results-dir TestResults/UnitCoverage --configuration Release` must (a) build only the `.slnf` projects on Linux/macOS without MAUI, (b) produce a `suite-result.json` whose `coveragePaths` lists multiple files, and (c) yield an aggregate `coverage-summary.json`/`coverage-merged.cobertura.xml` whose `filesCount` spans API, Application, Core, Contracts, Domain, AgentProvisioning, Persistence, and BFF source files (not just one project).
- **Threshold gate:** `aggregate_coverage.py` exit code reflects the 85% policy over the complete union; no regression vs. the prior per-suite result for Persistence (≥85%).
- **CI (T7):** `workflow_dispatch` both `pr-gate.yml` and `nightly.yml`; confirm `unit-coverage-*-results` artifacts contain the merged report, the `integration`/`e2e`/`load` jobs hit their (corrected) project paths, and the gate summary passes.
- **Review gates:** `code-reviewer` review of T1–T6; optional read-only Azure validation not required (no Azure writes or resource reads in this change).
- **Docs gate (T8):** docs-dev verifies acceptance criteria and updates `6-Docs/DevOps/overview.md` §4 and the plan index before the plan may be archived.
