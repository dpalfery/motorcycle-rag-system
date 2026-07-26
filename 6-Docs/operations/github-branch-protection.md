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

### 4. Repeat for `main`

If the `main` branch has its own protection rule (separate from `develop`), repeat steps 2–3 for it.

### 5. Verify

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
| `nightly / Snyk SCA + SAST` | `snyk-sca-sast` | Full dependency + code security scan |
| `nightly / Snyk Container — API` | `snyk-container-api` | API container image scan |
| `nightly / Snyk Container — UI` | `snyk-container-ui` | UI container image scan |
| `nightly / Snyk Container — Local Processor` | `snyk-container-processor` | Local Processor container image scan |
| `nightly / Test Summary` | `test-summary` | Consolidated nightly results report |
