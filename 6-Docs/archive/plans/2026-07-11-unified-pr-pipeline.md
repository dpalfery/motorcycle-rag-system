# Unified PR pipeline — consolidate CodeQL, tests, Snyk, and docs into one orchestrated gate

**Status:** Archived
**Date:** 2026-07-11
**Goal:** Replace the four uncoordinated PR-triggered workflows with a single concurrency-controlled, path-filtered, security-gated pipeline that preserves full test depth while eliminating redundant runs, redundant solution rebuilds, and duplicate security scans.

---

## 1. Problem / Motivation

Four workflows currently fire on pull requests with **no coordination, no concurrency control, and no path filtering**:

| Workflow | PR trigger | What it does on a PR |
| --- | --- | --- |
| `codeql.yml` | `pull_request` → main, develop | CodeQL C# analysis (full solution build + `security-extended`) |
| `comprehensive-testing.yml` | `pull_request` → main only | Unit (Linux + macOS), integration, E2E, load, performance, SkillForge, **Snyk SCA+SAST** |
| `docs.yml` | `pull_request` (path-filtered) | markdownlint, validate-docs, lychee, gitleaks |
| `claude.yml` | `@claude` mention only | Claude AI assistant (on-demand, unchanged by this plan) |

**Root-cause chain (verified against live source):**

1. **Duplicate parallel security scanning on every PR.** `codeql.yml` runs full CodeQL; `comprehensive-testing.yml` runs Snyk SCA+SAST (`snyk-sca-sast` job) **plus** uploads 3 SkillForge SARIF files via `github/codeql-action/upload-sarif`. All four run in parallel with zero ordering. The user perceives "multiple CodeQL" because the `codeql-action` shows up across multiple workflows in the Actions UI.
2. **No concurrency control** on `codeql.yml`, `comprehensive-testing.yml`, or `docs.yml`. Only `deploy.yml` has a `concurrency` block. Rapid pushes to a PR spawn overlapping, redundant runs (Action minutes × N).
3. **No path filters on `codeql.yml` or `comprehensive-testing.yml`.** A docs-only or config-only PR triggers a full CodeQL C# build and the entire test matrix — including a **macOS** runner for mobile unit tests (the most expensive runner type). PR #116 (pure documentation) would trigger all of it.
4. **Redundant solution rebuilds.** CodeQL builds the solution once; `comprehensive-testing.yml` rebuilds it **4+ times** across `unit-coverage-linux`, `integration-tests`, `end-to-end-tests`, `azure-integration-tests`, and `load-tests`, plus `skill-gate` builds SkillForge. No build artifact is shared between jobs.
5. **Snyk configuration drift.** The PR-time `snyk-sca-sast` job uses `--all-projects` with **no exclusions** (scans `.kilo`/`.opencode` tooling junk); the nightly `snyk.yml` uses `--exclude=".kilo,.opencode,.pnpm-store"`. Two configs, same scanner, inconsistent results.
6. **Trigger asymmetry.** `codeql.yml` targets PRs to `main` **and** `develop`; `comprehensive-testing.yml` targets PRs to `main` only. The **default branch is `develop`**, so most PRs (per git config and open PRs #89/#114/#115/#116) target `develop` and **skip the test suite entirely** while still running CodeQL — inconsistent, unpredictable gating.
7. **AI review bots are a separate cost layer (out of scope, documented).** Evidence from PR #116: `copilot-pull-request-reviewer[bot]` (8 reviews) and `kilo-code-bot[bot]` (3 reviews) both auto-review on every commit. These are GitHub Apps running on their own infrastructure and **cannot be gated from a workflow**. Per the approved scope (D1), this plan does not touch the bots; the cost reduction comes from the Actions pipeline consolidation.

## 2. Approved decisions

- **D1 — Scope: Actions pipeline consolidation only; AI review bots left as-is.** The Copilot and Kilo bots are GitHub Apps that fire on every commit independently of workflows and cannot be orchestrated from `.github/workflows/`. Fixing duplicate-bot cost requires repo-settings changes (Settings → Copilot code review; Settings → GitHub Apps → Kilo Code Bot), which are out of scope. The repository-settings remediation is documented in §6 as a residual recommendation.
- **D2 — PR pipeline depth: full depth on every PR.** Keep integration tests, E2E, Snyk SCA/SAST, and SkillForge on every PR (current behavior), but orchestrated into one pipeline with proper job ordering, gating, and concurrency. Trade-off accepted: slower PRs (~20–30 min) in exchange for catching everything pre-merge.
- **D3 — Security gates deep tests.** CodeQL and Snyk run after the build+unit-test phase succeeds. Integration tests, E2E, and mobile unit tests run only after CodeQL + Snyk pass. This satisfies "security scanning completes before expensive steps" and avoids wasting deep-test time when security fails.
- **D4 — Single orchestrator file (`pr-gate.yml`).** One workflow file contains all PR-time jobs with `needs:`-based phase ordering, rather than a caller-workflow + reusable-workflows split. Rationale: matches the "single uber pipeline" intent, produces simple branch-protection check names (`pr-gate / <job>`), and is easier to reason about than a multi-file `workflow_call` graph.
- **D5 — Build once, share artifact.** The `build-test` job restores + builds the solution once and uploads the output as an artifact. Downstream test jobs (integration, E2E) download it and run `dotnet test --no-build`. Eliminates the 4× redundant rebuilds (problem P4). CodeQL cannot reuse a plain build (it requires its own instrumented build via `init` before build, `analyze` after) and remains self-contained, but is gated behind `build-test` success so a broken build does not waste CodeQL time.
- **D6 — Path filtering via `dorny/paths-filter`.** A fast `changes` job classifies the diff into `code` / `docs` / `infra` buckets and exposes them as outputs. Code jobs (build, CodeQL, Snyk, tests) are gated on `code == true`; the docs job is gated on `docs == true`. A docs-only PR runs only `docs-quality` and the summary.
- **D7 — Concurrency cancel-in-progress per ref.** `concurrency: { group: pr-gate-${{ github.ref }}, cancel-in-progress: true }` on the PR orchestrator. Stale runs on the same ref are cancelled when a new push arrives (fixes P2).
- **D8 — Trigger symmetry: PRs to `main` and `develop`.** Fixes P6. Both branches receive the same gate so develop PRs (the majority) no longer skip the test suite.
- **D9 — Nightly consolidation.** A new `nightly.yml` absorbs the scheduled jobs from `comprehensive-testing.yml` (full matrix + Azure integration + load + performance, daily 2 AM) and `snyk.yml` (SCA + SAST + 3 container scans, daily 3 AM), unified under one schedule and one Snyk config (with the `.kilo`/`.opencode`/`.pnpm-store` exclusions). Keeps `workflow_dispatch` inputs for manual load/Azure-integration runs.
- **D10 — `deploy.yml` and `claude.yml` unchanged.** Deploy is post-merge (push to main/develop only, never on PR). Claude is on-demand @mention. Neither is part of the PR gate.

## 3. Investigation findings

**Workflows examined (read-only, full contents):** `claude.yml`, `codeql.yml`, `comprehensive-testing.yml`, `deploy.yml`, `docs.yml`, `snyk.yml`, plus `.github/dependabot.yml`, `.github/PULL_REQUEST_TEMPLATE.md`, `.github/agents/code-reviewer.agent.md`, `6-Docs/deployment/overview.md`.

**Bot behavior evidence (PR #116, documentation-only):** 12 reviews recorded — 8 from `copilot-pull-request-reviewer[bot]`, 3 from `kilo-code-bot[bot]`, 1 owner. Both bots fired on every push independently. Confirms bots are GitHub Apps outside workflow control (basis for D1).

**Default branch:** `develop` (confirmed via git config and the GitHub API `default_branch` field). This is why the current `comprehensive-testing.yml` `pull_request: [main]` trigger silently skips most PRs.

**CodeQL build requirement:** `github/codeql-action/init@v4` must wrap the build for compiled languages; the build cannot be a shared artifact from another job. Verified against `codeql.yml` (init → setup-dotnet → build → analyze). CodeQL therefore stays self-contained but is ordered after `build-test` so a broken build fails fast before CodeQL spends time.

**Snyk config drift:** PR-time job (`comprehensive-testing.yml:533`) runs `snyk test --severity-threshold=high --all-projects --sarif-file-output=...` with no exclusions. Nightly (`snyk.yml:49`) runs the same with `--exclude=".kilo,.opencode,.pnpm-store"`. Resolved by D9: one shared invocation with exclusions in both `pr-gate` and `nightly`.

**Docs workflow dependencies to preserve:** `docs.yml` runs markdownlint-cli2, `scripts/validate-docs.sh`, lychee (with a specific file-list argument), and gitleaks. All four must move into the `docs-quality` job. The lychee argument list and the `fetch-depth: 0` checkout (needed for lychee link checking) must be preserved.

**SkillForge gate:** Currently `continue-on-error: true` with a TODO to remove once all skills pass. The plan preserves `continue-on-error: true` and the TODO; it is not this plan's job to fix SkillForge validation.

**Branch protection:** Could not be read programmatically (no tool access to protection rules). The new pipeline produces check names under the `pr-gate` workflow. The implementer MUST verify and update branch protection required-status-checks to reference the new names and remove stale references to the deleted workflows (see §6 residual item).

## 4. Task list

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| 1 | Create | `.github/workflows/pr-gate.yml` | Build the unified PR orchestrator per §5 spec: `changes` job + Phase 1 (`build-test`, `docs-quality`) + Phase 2 (`codeql`, `snyk`) + Phase 3 (`unit-mobile`, `integration`, `e2e`, `skill-gate`) + `pr-gate-summary`. Includes concurrency, path filters, build-artifact upload/download, single required check. | `github-devops` |
| 2 | Create | `.github/workflows/nightly.yml` | Consolidate scheduled jobs: full test matrix, Azure integration (gated), load (gated), performance analysis, Snyk SCA+SAST+3 container scans. Unified Snyk config with `.kilo`/`.opencode`/`.pnpm-store` exclusions. `workflow_dispatch` inputs for load/Azure-integration toggles. Schedules: daily 2 AM (tests) + 3 AM (Snyk) preserved. | `github-devops` |
| 3 | Delete | `.github/workflows/codeql.yml` | Folded into `pr-gate` phase 2. Verify the CodeQL job in `pr-gate` preserves: `languages: csharp`, `queries: security-extended`, `category: "/language:csharp"`, weekly schedule fallback, and `workflow_dispatch`. | `github-devops` |
| 4 | Delete | `.github/workflows/comprehensive-testing.yml` | PR jobs moved to `pr-gate`; scheduled + manual jobs moved to `nightly`. Preserve all script invocations (`run_unit_coverage.py`, `aggregate_coverage.py`), Codecov upload, the `AZURE_*` env block, the mockserver service container for E2E, and the `testing` environment for Azure integration. | `github-devops` |
| 5 | Delete | `.github/workflows/docs.yml` | Folded into `pr-gate` `docs-quality` job. Preserve: markdownlint-cli2, `scripts/validate-docs.sh`, lychee with its exact file-list argument, gitleaks, and `fetch-depth: 0`. Preserve the `push` trigger for main/develop (docs validation still runs post-merge). | `github-devops` |
| 6 | Delete | `.github/workflows/snyk.yml` | Nightly SCA/SAST/container jobs moved to `nightly`. Preserve pinned Snyk CLI version (`snyk@1.1293.0`), all 3 container Dockerfile paths, and SARIF categories. | `github-devops` |
| 7 | Verify | Branch protection (repo settings) | After merge, update branch protection required status checks on `develop` and `main`: add `pr-gate / pr-gate summary` (and any per-job checks desired), remove stale references to `CodeQL`, `Comprehensive Testing Suite/*`, `Documentation Quality`. This is a manual repo-settings step; the implementer documents the exact check names produced. | `github-devops` |
| 8 | Update docs | `6-Docs/deployment/overview.md` §4 (CI/CD Flow) | Replace the description of per-workflow CI behavior with the unified pipeline description (pr-gate phases + nightly). Reflect the new file inventory. | `app-docs-standard`, `github-devops` |

## 5. Sequencing / dependency graph

```
Task 1 (create pr-gate.yml)  ─┐
Task 2 (create nightly.yml)   ├─ can be created in parallel (independent files)
                              │
Task 3,4,5,6 (delete old)    ─┘  delete ONLY after Tasks 1+2 exist and are merged,
                                   to avoid a window with no PR gate

Task 7 (branch protection)  ─── depends on Task 1 merged (needs final check names)
Task 8 (docs update)        ─── depends on Tasks 1-6 complete (final file inventory)
```

**Implementation order:** Tasks 1+2 first (parallel) → review + merge → Tasks 3-6 (delete old workflows, can be one commit) → Task 7 (branch protection) → Task 8 (docs). Deleting old workflows before the new ones are merged creates a gap with no gate; do not do that.

### `pr-gate.yml` job graph and specification

```
changes ─────────────────────────────────────────────────────────────────
  │  outputs: code, docs, infra   (dorny/paths-filter@v3, ubuntu, ~15s)
  │
  ├─────────────────┬────────────────────────────────────────────────────
  │ PHASE 1         │ PHASE 1 (path-gated, parallel)
  ▼                 ▼
build-test       docs-quality
(if: code)       (if: docs)
  │ restore + build   markdownlint-cli2
  │ unit coverage     validate-docs.sh
  │ (run_unit_        lychee (exact file list)
  │  coverage.py)     gitleaks
  │ upload artifact   fetch-depth: 0
  ▼
  ├──────────────────────────────────────────────────────────────────────
  │ PHASE 2 — security gate (needs: build-test, if: code)
  ├── codeql ────────┐  (init→setup-dotnet→build→analyze, own instrumented build)
  └── snyk ──────────┘  (test + code test → SARIF; shared exclude list)
       │
       ▼ (needs: build-test + codeql + snyk, if: code)
  ├──────────────────────────────────────────────────────────────────────
  │ PHASE 3 — deep validation (parallel)
  ├── unit-mobile   (macOS, run_unit_coverage.py mobileapp-unit suite)
  ├── integration   (download build artifact, dotnet test --no-build)
  ├── e2e           (download build artifact, mockserver service, dotnet test --no-build)
  └── skill-gate    (SkillForge validate/lint/scan + SARIF; continue-on-error)
       │
       ▼
  pr-gate-summary   (needs: all jobs, if: always())
                    single required check; fails if any required job failed
```

**Required configuration details:**

- **Triggers:** `pull_request` (branches: main, develop), `push` (branches: main, develop), `workflow_dispatch`.
- **Concurrency:** `group: pr-gate-${{ github.ref }}`, `cancel-in-progress: true`.
- **Permissions:** `contents: read`, `security-events: write` (CodeQL/Snyk/SkillForge SARIF), `actions: read`, `checks: write`, `pull-requests: write` (summary comment).
- **`changes` job path filters:** `code` = `**/*.cs`, `**/*.csproj`, `**/*.sln`, `2-Application/local-processing-service/**/*.py`, `1-Presentation/**/*.{ts,tsx}`, `**/package.json`, `**/package-lock.json`, `**/pyproject.toml`, `**/poetry.lock`; `docs` = `**/*.md`, `**/AGENTS.md`, `scripts/validate-docs.sh`, `.markdownlint-cli2.yaml`; `infra` = `.github/**`, `7-Deployment/**`.
- **`build-test`:** `actions/setup-dotnet@v5` with `cache: true` and `cache-dependency-path: MotorcycleRAG.sln`; setup-python 3.12; setup-node 22 with npm cache on Admin Desktop lockfile; `run_unit_coverage.py` with dotnet-unit/bff-unit/python-unit/admindesktop-unit suites; upload artifact `build-output` (the built solution) and `unit-coverage-linux-results`.
- **`codeql`:** self-contained init + setup-dotnet (cached) + build + analyze; preserve `security-extended` and `category: "/language:csharp"`. Gated `needs: build-test` (broken build → skip CodeQL).
- **`snyk`:** pinned `snyk@1.1293.0`; `--exclude=".kilo,.opencode,.pnpm-store"` (fixes P5 drift); poetry lock generation for the processor; `continue-on-error: true` preserved (matches current behavior so Snyk findings do not hard-block); upload SCA + SAST SARIF.
- **`integration` / `e2e`:** download `build-output` artifact; `dotnet test --no-build`. E2E keeps the `mockserver/mockserver:latest` service container and the `TestConfiguration__*` env vars.
- **`skill-gate`:** `continue-on-error: true` preserved with TODO; checkout with `submodules: true` (SkillForge is a submodule).
- **`pr-gate-summary`:** `needs: [build-test, docs-quality, codeql, snyk, unit-mobile, integration, e2e, skill-gate]`, `if: always()`. Step evaluates `needs.*.result`; fails the job if any non-skipped required job is `failure`/`cancelled`. Optional PR comment with status table.

### `nightly.yml` specification

- **Triggers:** `schedule` (daily 2 AM tests, daily 3 AM Snyk — preserve offsets), `workflow_dispatch` with `run_load_tests` and `run_integration_tests` boolean inputs.
- **Jobs:** full test matrix (Linux + mobile unit, integration, E2E, Azure integration [gated], load [gated], performance analysis), SkillForge gate, Snyk SCA+SAST, Snyk container scans (API, UI, processor).
- **Carry over verbatim:** the `AZURE_*` secrets env block, the `testing` environment on Azure integration, the Codecov upload, all Dockerfile paths, SARIF categories.

## 6. Residual decisions / risks

- **Branch protection required checks (owner: repo admin, after merge).** This plan cannot read or set protection rules. The implementer must, after `pr-gate.yml` is merged, open Settings → Branches → develop/main protection and (a) add the new `pr-gate` check name(s) as required, (b) remove stale required checks referencing the deleted workflows. Until this is done, the new pipeline runs but is not enforced as a merge gate. Condition to resolve: post-merge verification run.
- **AI review bots (owner: user, repo settings).** Per D1, the Copilot + Kilo duplicate-reviewer cost is out of scope. Recommendation for a future change: disable Copilot auto-review (Settings → Copilot → code review) and keep Kilo as the single reviewer, OR configure Kilo to trigger only on a label applied after `pr-gate` passes. This is the single biggest remaining PR-cost lever and is documented here so it is not lost.
- **SkillForge `continue-on-error` (owner: future task).** The skill gate currently cannot fail the build. Removing `continue-on-error` is a separate task dependent on all skills passing SkillForge validation; not in scope here.
- **Snyk `continue-on-error` (owner: user decision).** The plan preserves current behavior (Snyk does not hard-block). If the team later wants Snyk high-severity findings to block PRs, flip `continue-on-error: false` — a one-line change, but a policy decision that needs explicit approval.
- **Risk: lychee file-list drift.** The `docs-quality` job copies the exact lychee argument list from `docs.yml`. If new top-level READMEs are added later, the list must be updated. Low risk; mitigated by keeping the list in one place.
- **Risk: build-artifact size.** Uploading the full built solution as an artifact may be large. If Actions reports size/time issues, narrow the artifact to test-output directories only and let integration/E2E rebuild (reverting the build-once optimization). Verify in §9.

## 7. Out of scope

- **AI review bot orchestration (Copilot + Kilo).** Out of scope per D1; bots are GitHub Apps outside workflow control. Documented as a residual recommendation in §6.
- **`deploy.yml` and `claude.yml` changes.** Deploy is post-merge; Claude is on-demand. Neither is part of the PR gate and neither changes.
- **Test-suite logic changes.** This plan moves existing jobs into a new orchestration; it does not rewrite `run_unit_coverage.py`, `aggregate_coverage.py`, the test projects, or test filters. Behavior is preserved.
- **SkillForge validation fixes.** The `continue-on-error` TODO is preserved as-is.
- **Adding new scanners or changing security thresholds.** Query suites, severity thresholds, and SARIF categories are preserved from the current workflows.
- **Dependabot configuration changes.** `.github/dependabot.yml` is unchanged.

## 8. Required skills

- `github-devops` — primary skill for all workflow authoring (Tasks 1–6), branch-protection check-name verification (Task 7), and the CI/CD-flow documentation update (Task 8). This is the controlling skill.
- `app-docs-standard` — for Task 8, to update `6-Docs/deployment/overview.md` §4 following the repository documentation standard.
- `code-review` — review of the workflow YAML diffs (YAML correctness, secret handling, permissions least-privilege, action pinning).
- No source-code, database, Azure-infrastructure, or application-logic skills are required. This is a pure CI/CD-pipeline change.

## 9. Verification harness

**Primary gate (must pass on a real PR after merge):**

1. **Docs-only PR** (e.g., a README typo fix targeting `develop`): only `changes` + `docs-quality` + `pr-gate-summary` run. `build-test`, `codeql`, `snyk`, `unit-mobile`, `integration`, `e2e`, `skill-gate` are all skipped (`if: code == 'false'`). Confirms path filtering (fixes P3) and that docs-only PRs no longer trigger CodeQL/tests.
2. **Code PR** targeting `develop`: full depth runs in the documented phase order. Confirm in the Actions run graph that Phase 2 jobs (`codeql`, `snyk`) start only after `build-test` succeeds, and Phase 3 jobs start only after Phase 2 succeeds. Confirms security gating (D3) and trigger symmetry (D8).
3. **Rapid double-push** to the same PR: the first run is cancelled by concurrency when the second push arrives. Confirms D7.

**Correctness gates:**

4. **Build-once verification:** in a code-PR run, confirm `integration` and `e2e` download the `build-output` artifact and run `dotnet test --no-build` (no separate `dotnet build` step). Confirm `codeql` still performs its own build (it must). Confirms D5.
5. **No duplicate security scans:** confirm exactly one CodeQL analysis job and one Snyk job run per PR (not the previous parallel CodeQL + Snyk + SkillForge-SARIF-via-codeql-action confusion). Confirm Snyk uses `--exclude=".kilo,.opencode,.pnpm-store"`. Confirms P1/P5 fixes.
6. **Nightly parity:** trigger `nightly.yml` via `workflow_dispatch` with `run_integration_tests=true` and `run_load_tests=true`; confirm Azure integration, load, performance, and all Snyk container scans execute with the same scripts and env as the old `comprehensive-testing.yml` + `snyk.yml`.

**Hygiene gates:**

7. **No stale workflows:** confirm `codeql.yml`, `comprehensive-testing.yml`, `docs.yml`, `snyk.yml` are deleted from `.github/workflows/`; only `pr-gate.yml`, `nightly.yml`, `deploy.yml`, `claude.yml` remain.
8. **Branch protection:** confirm Settings → Branches → develop requires `pr-gate` and no longer references deleted workflow checks.
9. **Docs updated:** `6-Docs/deployment/overview.md` §4 describes the unified pipeline.

**Code review:** a `code-reviewer` pass on the full workflow diff — check YAML validity, least-privilege permissions, action pinning (`@v4`/`@v5`/`@v7` preserved), no secrets in plaintext, and that no job accidentally lost a `continue-on-error` or env var from the originals. No security-review or Azure validation needed (no application code, no infrastructure, read-only CI).
