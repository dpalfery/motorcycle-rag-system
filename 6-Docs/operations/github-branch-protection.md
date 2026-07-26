---
id: operations/github-branch-protection
title: GitHub Branch Protection Rules
doc-type: runbook
status: current
component: MotorcycleRAG system
owner: Platform maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---

# GitHub Branch Protection Rules

Operator procedure for keeping required status checks on the protected branches aligned
with the unified `pr-gate.yml` pipeline. This is a process runbook: it configures GitHub
repository settings and operates no indexed source, so it carries no `code-refs`.

## Background

The workflows `codeql.yml`, `comprehensive-testing.yml`, `docs.yml`, and `snyk.yml` have been replaced by the unified PR pipeline (`pr-gate.yml`) and the consolidated nightly pipeline (`nightly.yml`). Branch protection rules on `develop` and `main` must be updated to reflect the new check names and remove stale references.

## Required Actions

### 1. Navigate to branch protection settings

Go to **GitHub → Settings → Branches → Branch protection rules**.

Edit the rule for `develop` (or `main`, or whichever branch each setting applies to — repeat for each protected branch that had the old checks).

### 2. Add required status checks

In the **"Require status checks to pass before merging"** section, search for and add:

| Check name | Notes |
| --- | --- |
| `pr-gate / PR Gate Summary` | **Primary required check.** Aggregates all Phase 1–3 job results and reports the final pass/fail. This is the single check the team should mark as required. |
| `pr-gate / Build & Unit Tests (Linux)` | (Optional) Add if the team wants the build-test job visible and individually required in the PR UI. |
| `pr-gate / CodeQL Analysis` | (Optional) Add if the team wants the code-scan job individually required. |
| `pr-gate / Integration Tests` | (Optional) Add if the team wants integration tests individually required. |
| `pr-gate / End-to-End Tests` | (Optional) Add if the team wants E2E tests individually required. |

**Recommendation:** Require only `pr-gate / PR Gate Summary`. This single check reflects the full pipeline status and avoids maintaining a long list of individual checks. The pipeline itself enforces that each subsystem must pass.

### 3. Remove stale required status checks

Delete any of the following from the required checks list (they no longer exist as workflow jobs):

| Stale check name | Former workflow |
| --- | --- |
| `CodeQL` | `codeql.yml` |
| `Comprehensive Testing Suite / build-test` | `comprehensive-testing.yml` |
| `Comprehensive Testing Suite / integration` | `comprehensive-testing.yml` |
| `Comprehensive Testing Suite / e2e` | `comprehensive-testing.yml` |
| `Documentation Quality` | `docs.yml` |
| `Snyk` or `snyk` | `snyk.yml` |
| Any other `Comprehensive Testing Suite/*` check | `comprehensive-testing.yml` |

Also remove any status checks referencing the deleted per-workflow jobs listed above that GitHub might have auto-suggested from previous runs.

### 4. Disconnect the Snyk PR-check integration

Snyk has been retired from this repository. The `snyk.yml` workflow is deleted and no
workflow job invokes the Snyk CLI, but the **`security/snyk (dpalfery)` commit status**
is posted directly by Snyk's GitHub integration, not by GitHub Actions. It therefore
survives workflow deletion and keeps reporting on every pull request — including
`error: You have used your limit of private tests`, which appears as a failed check.

This status can only be removed from Snyk, not from this repository:

1. Sign in to <https://app.snyk.io> as an admin of the `dpalfery` organization.
2. Go to **Settings → Integrations → GitHub** and either
   remove `dpalfery/motorcycle-rag-system` from the imported targets,
   or disconnect the GitHub integration entirely.
3. Alternatively, to keep the integration for other repositories, open the project's
   **Settings → PR checks** and disable *Pull request status checks* (both the
   "Fail for issues" and "Fail for new licence issues" toggles) so no status is posted.
4. Confirm no ruleset or classic branch-protection rule still lists `security/snyk (dpalfery)`
   as a required check (see step 3 above), otherwise pull requests will block on a status
   that will never be reported again.

Existing pull requests keep the stale status on their current head commit; it clears on
the next push once the integration is disconnected.

### 5. Repeat for `main`

If the `main` branch has its own protection rule (separate from `develop`), repeat steps 2–3 for it.

### 6. Verify

After saving the updated rules, the next PR to the protected branch should show `pr-gate / PR Gate Summary` as a required check. Older in-flight PRs will show it as "Expected — Waiting for status to be reported" once they are rebased or retriggered.

> **Until this update is made**, the new `pr-gate.yml` pipeline runs on every PR and push but its results are **not** enforced as a merge gate. The `deploy.yml` workflow is unaffected.

## Nightly Workflow Check Names (Reference Only)

The `nightly.yml` pipeline runs on schedule and does not block PRs. These check names are useful for monitoring, dashboards, and alerting — not for merge requirements.

| Check name | Job ID | Purpose |
| --- | --- | --- |
| `nightly / Unit Coverage (Linux)` | `unit-coverage-linux` | Full Linux unit test matrix |
| `nightly / Unit Coverage (Mobile)` | `unit-mobile` | MAUI mobile unit tests |
| `nightly / Unit Coverage Gate` | `unit-tests` | Aggregation + Codecov upload |
| `nightly / Integration Tests` | `integration-tests` | Non-Azure integration tests |
| `nightly / End-to-End Tests` | `end-to-end-tests` | E2E with MockServer |
| `nightly / Azure Integration Tests` | `azure-integration-tests` | Real Azure service tests |
| `nightly / Load Tests` | `load-tests` | NBomber load testing |
| `nightly / Performance Analysis` | `performance-analysis` | Performance metrics reporting |
| `nightly / Kyber-Weave Skill Gate` | `skill-gate` | Skill directory validation |
| `nightly / IaC Security Scan (Checkov)` | `iac-scan` | Dockerfile + workflow IaC scan |
| `nightly / Trivy Container Scan` | `trivy-container-scan` | API and UI container image scans |
| `nightly / Test Summary` | `test-summary` | Consolidated nightly results report |
