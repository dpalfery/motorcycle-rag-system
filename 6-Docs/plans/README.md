# Plan Index

This is the authoritative inventory for plans under `6-Docs/plans/`. It lets people retain planning history without making completed work agent noise.

## How to use this index

Read this file before opening a plan. Open a plan only when it is both relevant to the task and listed below as `Draft`, `Ready`, `In progress`, or `Blocked`.

- `Draft` is planning-only; it is not authority to implement.
- `Ready`, `In progress`, and `Blocked` are the only implementation-actionable states.
- `Review required`, `Completed`, `Superseded`, and `Archived` are not implementation authority.
- A plan is archived only after its acceptance criteria and canonical-documentation updates have been reviewed. A final plan document is not necessarily a completed implementation.

## Active inventory

| Plan | Status | Goal |
| --- | --- | --- |
| [2026-07-19 Local processor start failure](2026-07-19-local-processor-start-failure.md) | Ready | Stop Admin Desktop processor exit status 1 from import-time embedding discovery; lazy-init embedder; surface redacted stderr on start failure. |
| [2026-07-18 Security and Quality Alert Remediation](2026-07-18-security-quality-remediation.md) | Review required | Local T0–T15 implemented; canonical docs updated. T16 partial: #399 dismissed (`used in tests`); #401 awaits post-merge Semgrep. Not archived: T16/#401, T17 (124 open legacy CodeQL on develop), T18 matrix migration, and remaining T19 GitHub gates remain open. |
| ~~2026-07-18 Snyk CWE-117 log forging — central provider remediation~~ | Archived | Completed 2026-07-18. SanitizingLoggerProvider + SanitizingLogger in MotorcycleRAG.Core.Logging, registered in all 5 .NET hosts. 126+ per-call LogSanitizer.Sanitize sites decommissioned; BFF SanitizeLogValue helper removed. DbSetup SanitizingLogger<T>/SecureLoggerExtensions deleted. 29 core tests + 8 registration-gate tests passing. Canonical guidance updated in 6-Docs/system/security.md §Log value encoding. |

## Operational status lists

[Admin Desktop pending work](../archive/plans/admin-desktop-pending-work.md) was a status list, not an implementation plan. It has been moved to the archive and is historical only. It is never implementation authority; promote a selected item into its own dated plan before acting on it.

## Archive register

Completed and superseded plans live in [`../archive/plans/`](../archive/plans/). Add an entry here when archiving: plan title, archive date, outcome, and the canonical documentation that replaced its operational guidance.

| Plan | Archived | Outcome / canonical guidance |
| --- | --- | --- |
| [2026-07-19 Admin Desktop Chrome profile auth launch](../archive/plans/2026-07-19-admin-desktop-chrome-profile-auth-launch.md) | 2026-07-19 | Verified complete (T1–T7). Fixed silent sole-Default enumeration fallback and macOS Chrome launch (`open -a` without `-n`). Automated: `cargo test auth::` 16 passed; Vitest auth+SignInScreen 28 passed; clippy clean; code-reviewer Approve; security-review 0 medium/high/critical. Canonical guidance: `6-Docs/MotorcycleRAG.AdminDesktop/authentication.md` (enumeration failure UX, system-default browser escape hatch, macOS binary/`-na` launch). Residual operator check: manual macOS smoke with Chrome already running. |
| [2026-07-16 Domain entity setter encapsulation and inventory gate](../archive/plans/2026-07-16-domain-entity-setter-encapsulation-and-inventory-gate.md) | 2026-07-17 | Verified complete. All four entities (IngestionJob, BikeModel, ManualDocument, McpToolConfiguration) refactored to private-ctor + Create/Rehydrate factories + get-only/private-set properties + named behavior methods. Repositories converted to Row+Map. IngestionJob legacy columns dropped via idempotent T-SQL script. Post-migration inventory gate enabled and passing. Completes criteria 3 and 6 of ADR-2026-07-13. Canonical guidance: `6-Docs/adr/ADR-2026-07-13-domain-entity-dto-exceptions.md` (gate policy), `6-Docs/rules/architecture-general.md` (architecture placement), `6-Docs/adr/ADR-2026-07-16-schema-deployment-idempotent-sql.md` (DDL pattern). |
| [2026-07-12 API persistence test coverage draft](../archive/plans/2026-07-12-api-persistence-test-coverage-plan.md) | 2026-07-17 | Superseded by [2026-07-12 persistence test coverage](../archive/plans/2026-07-12-persistence-test-coverage.md) (already implemented and archived). |
| [2026-07-13 Domain Entity and DTO Rationalization](../archive/plans/2026-07-13-domain-entity-dto-rationalization.md) | 2026-07-17 | Relocation of DTOs completed; remaining gaps (setter encapsulation and inventory gate) satisfied by [2026-07-16 Domain entity setter encapsulation and inventory gate](../archive/plans/2026-07-16-domain-entity-setter-encapsulation-and-inventory-gate.md) (now completed and archived). |
| [2026-07-13 IoC/DI and Clean Architecture remediation](../archive/plans/2026-07-13-ioc-di-clean-architecture-remediation.md) | 2026-07-13 | Verified complete; DI composition and ownership rules are in `6-Docs/rules/architecture-general.md` and `6-Docs/system/architecture.md`; component specifics are in the API, Web UI/BFF, Mobile, and DbSetup documentation. Mobile verification remains host-blocked by the installed Xcode/MacCatalyst SDK mismatch. |
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
| [2026-07-12 Checkov + CrossGuard IaC scanning](../archive/plans/2026-07-12-checkov-crossguard-iac-scanning.md) | 2026-07-12 | Verified complete; see `6-Docs/DevOps/overview.md` §4 (CI/CD Flow), `7-Deployment/scanning/README.md`, and `7-Deployment/infrastructure/AGENTS.md` for scanning tool documentation. |
| [2026-07-12 Python tests to 5-Test](../archive/plans/2026-07-12-issue-118-python-tests-to-5-test.md) | 2026-07-12 | Verified complete (issue #118); see `6-Docs/rules/architecture-general.md` 5-Test Layer section, `5-Test/local-processing-service.Tests/AGENTS.md`, and `2-Application/local-processing-service/AGENTS.md` for the new test-suite location. |
| [2026-07-12 Persistence test coverage 46.92% to 90%+](../archive/plans/2026-07-12-persistence-test-coverage.md) | 2026-07-13 | Verified complete; 1,430 tests created under `5-Test/MotorcycleRAG.Persistence.Tests/`, 95.87% aggregate coverage, 0 Persistence files below 85%. See `6-Docs/rules/architecture-general.md` §5-Test Layer, `6-Docs/catalog.md`, and `6-Docs/DevOps/overview.md` §4.1 (`persistence-unit` suite). |
| [2026-07-13 Solution-level .NET unit coverage in CI](../archive/plans/2026-07-13-solution-level-unit-coverage.md) | 2026-07-13 | Verified complete; `MotorcycleRAG.UnitTests.slnf` created, runner/aggregator support multi-coverage merging, `coverage-config.json` consolidated to `dotnet-unit`/`python-unit`/`admindesktop-unit`, stale paths corrected in both workflows. See `6-Docs/DevOps/overview.md` §4 (CI/CD Flow) for solution-level `.slnf` coverage and multi-coverage aggregation. |
| [2026-07-13 Contracts.Models testing & coverage strategy](../archive/plans/2026-07-13-contracts-models-testing-coverage-strategy.md) | 2026-07-13 | Verified complete; `MotorcycleRAG.Contracts.Models` excluded from the per-file/per-class 85% line gate via `coverlet.runsettings` `<Exclude>` + `coverage-config.json` `pathContains` (additive-only; `MotorcycleRAG.Contracts` still gated), `RecordCoverageTests.cs` trimmed to behavior-only, new `WebSourceValidationResultTests` added. See `6-Docs/DevOps/overview.md` §4.1 (Coverage exclusions), `5-Test/MotorcycleRAG.Contracts.Tests/AGENTS.md`, and `3-Domain/MotorcycleRAG.Contracts.Models/AGENTS.md`. |
| [2026-07-18 Snyk CWE-117 log forging — central provider remediation](../archive/plans/2026-07-18-snyk-log-forging-central-provider.md) | 2026-07-18 | Verified complete. Central `SanitizingLoggerProvider : ILoggerProvider` in `MotorcycleRAG.Core.Logging` registered in all 5 .NET hosts, decommissioning 126+ per-call `LogSanitizer.Sanitize()` sites. DbSetup `SanitizingLogger<T>` / `SecureLoggerExtensions` deleted. 29 provider/extensions tests + 8 registration-gate tests passing. Canonical guidance: `6-Docs/system/security.md` §Log value encoding (central provider approach). |
