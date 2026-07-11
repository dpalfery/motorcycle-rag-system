# Plan Index

This is the authoritative inventory for plans under `6-Docs/plans/`. It lets people retain planning history without making completed work agent noise.

## How to use this index

Read this file before opening a plan. Open a plan only when it is both relevant to the task and listed below as `Draft`, `Ready`, `In progress`, or `Blocked`.

- `Draft` is planning-only; it is not authority to implement.
- `Ready`, `In progress`, and `Blocked` are the only implementation-actionable states.
- `Review required`, `Completed`, `Superseded`, and `Archived` are not implementation authority.
- A plan is archived only after its acceptance criteria and canonical-documentation updates have been reviewed. A final plan document is not necessarily a completed implementation.

## Active inventory

| Plan | Status | Goal / note |
| --- | --- | --- |
| [2026-07-07 Chunk upload fix and serverless search](2026-07-07-chunk-upload-fix-and-serverless-search.md) | Ready | Approved implementation plan. |
| [2026-07-08 PDF chunking dimension mismatch](2026-07-08-pdf-chunking-dimension-mismatch-analysis.md) | Draft | Proposed embedding-dimension fix. |
| [2026-07-08 PDF metadata extraction](2026-07-08-pdf-metadata-extraction.md) | Draft | Proposed iterative metadata extraction flow. |
| [2026-07-09 Metadata extraction RCA](2026-07-09-metadata-extraction-rca.md) | Draft | Investigation and proposed fixes. |
| [2026-07-09 Metadata extraction UI fixes](2026-07-09-metadata-extraction-ui-fixes.md) | Draft | Proposed Admin Desktop fixes. |
| [2026-07-10 Metadata sampling and LLM constraints](2026-07-10-metadata-extraction-sampling-and-llm-constraints.md) | Draft | Proposed extraction constraints. |
| [2026-07-11 Deployment secrets table](2026-07-11-issue-109-deployment-secrets-table.md) | Draft | Proposed Issue 109 documentation correction. |
| [2026-07-11 Validate-docs ripgrep remediation](2026-07-11-validate-docs-ripgrep-remediation.md) | Draft | Proposed validation-script correction. |

## Completion review required

These plans used legacy `Final` or `Finalized` labels. Their implementation state has not been re-verified as part of this lifecycle migration, so agents must not use them as authority. Review them against source and canonical documentation; then either archive them or return them to an active status.

| Plan | Status | Next action |
| --- | --- | --- |
| [2026-07-09 Metadata extraction fix plan](2026-07-09-metadata-extraction-fix-plan.md) | Review required | Verify remaining tasks and canonical documentation. |
| [2026-07-10 Admin Desktop auth enhancements](2026-07-10-admin-desktop-auth-enhancements.md) | Review required | Verify implementation and Admin Desktop documentation. |
| [2026-07-10 Pipeline logging and graph batching](2026-07-10-pipeline-logging-and-graph-batching.md) | Review required | Verify implementation and processor documentation. |

## Operational status lists

[Admin Desktop pending work](admin-desktop-pending-work.md) is a status list, not an implementation plan. It is retained for tracking but is never implementation authority. Promote a selected item into its own dated plan before acting on it.

## Archive register

Completed and superseded plans live in [`../archive/plans/`](../archive/plans/). Add an entry here when archiving: plan title, archive date, outcome, and the canonical documentation that replaced its operational guidance.

| Plan | Archived | Outcome / canonical guidance |
| --- | --- | --- |
| [2026-07-04 Local-first ingestion](../archive/plans/2026-07-04-local-first-ingestion.md) | Existing archive; review date unknown | Historical only; use current API, Admin Desktop, and local-processing-service documentation. |
