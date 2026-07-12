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
| [2026-07-12 Checkov + CrossGuard IaC scanning](2026-07-12-checkov-crossguard-iac-scanning.md) | Draft | Add Pulumi CrossGuard for the C# stack + Checkov for Dockerfiles/GitHub Actions workflows; premise correction: Checkov has no `pulumi` framework. |

## Operational status lists

[Admin Desktop pending work](../archive/plans/admin-desktop-pending-work.md) was a status list, not an implementation plan. It has been moved to the archive and is historical only. It is never implementation authority; promote a selected item into its own dated plan before acting on it.

## Archive register

Completed and superseded plans live in [`../archive/plans/`](../archive/plans/). Add an entry here when archiving: plan title, archive date, outcome, and the canonical documentation that replaced its operational guidance.

| Plan | Archived | Outcome / canonical guidance |
| --- | --- | --- |
| [2026-07-04 Local-first ingestion](../archive/plans/2026-07-04-local-first-ingestion.md) | Existing archive; review date unknown | Historical only; use current API, Admin Desktop, and local-processing-service documentation. |
| [2026-07-07 Chunk upload fix and serverless search](../archive/plans/2026-07-07-chunk-upload-fix-and-serverless-search.md) | 2026-07-11 | Approved proposal (Ready); historical only. |
| [2026-07-08 PDF chunking dimension mismatch](../archive/plans/2026-07-08-pdf-chunking-dimension-mismatch-analysis.md) | 2026-07-11 | Proposal (Draft); historical only. |
| [2026-07-08 PDF metadata extraction](../archive/plans/2026-07-08-pdf-metadata-extraction.md) | 2026-07-11 | Proposal (Draft); historical only. |
| [2026-07-09 Metadata extraction RCA](../archive/plans/2026-07-09-metadata-extraction-rca.md) | 2026-07-11 | Investigation (Draft); historical only. |
| [2026-07-09 Metadata extraction fix plan](../archive/plans/2026-07-09-metadata-extraction-fix-plan.md) | 2026-07-11 | Verified complete; see local-processing-service and Admin Desktop documentation. |
| [2026-07-09 Metadata extraction UI fixes](../archive/plans/2026-07-09-metadata-extraction-ui-fixes.md) | 2026-07-11 | Proposal (Draft); historical only. |
| [2026-07-10 Admin Desktop auth enhancements](../archive/plans/2026-07-10-admin-desktop-auth-enhancements.md) | 2026-07-11 | Verified complete; see Admin Desktop documentation. |
| [2026-07-10 Metadata sampling and LLM constraints](../archive/plans/2026-07-10-metadata-extraction-sampling-and-llm-constraints.md) | 2026-07-11 | Proposal (Draft); historical only. |
| [2026-07-10 Pipeline logging and graph batching](../archive/plans/2026-07-10-pipeline-logging-and-graph-batching.md) | 2026-07-11 | Verified complete; see local-processing-service documentation. |
| [2026-07-11 Deployment secrets table](../archive/plans/2026-07-11-issue-109-deployment-secrets-table.md) | 2026-07-11 | Proposal (Draft); historical only. |
| [2026-07-11 Validate-docs ripgrep remediation](../archive/plans/2026-07-11-validate-docs-ripgrep-remediation.md) | 2026-07-11 | Proposal (Draft); historical only. |
| [2026-07-11 Unified PR pipeline](../archive/plans/2026-07-11-unified-pr-pipeline.md) | 2026-07-11 | Implementation complete; see `6-Docs/DevOps/overview.md` §4 (CI/CD Flow) and `6-Docs/agent-notes/branch-protection-update.md` for operational guidance. |
