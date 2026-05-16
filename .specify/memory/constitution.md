<!--
Sync Impact Report

- Version change: 1.1.0 -> 1.2.0
- Modified principles:
  - informal "First principals" notes -> absorbed into III. Execution Discipline & Code Quality and VII. Process & Workflow
  - III. Code Quality (Zero Warnings) -> III. Execution Discipline & Code Quality
  - V. Observability -> V. Observability & Diagnostics
  - VII. Process & Workflow -> VII. Process & Workflow (expanded approval and repository hygiene rules)
- Added sections: None
- Removed sections: informal "First principals" notes
- Templates requiring updates:
  - ✅ updated: .specify/templates/plan-template.md
  - ✅ updated: .specify/templates/spec-template.md
  - ✅ updated: .specify/templates/tasks-template.md
  - N/A: .specify/templates/commands/*.md (folder not present)
- Follow-up TODOs: None
-->

# Motorcycle RAG System Constitution

## Core Principles

### I. Security (NON-NEGOTIABLE)
The system MUST meet OWASP ASVS Level 2 expectations.

Non-negotiable rules:
- Secrets MUST NOT be stored in source control or hardcoded in code/config; use secure applications storage locations, environment variables and/or Key Vault.
- All inputs MUST be validated and sanitized; SQL access MUST be parameterized (no string concatenation).
- HTTPS MUST be enforced for runtime traffic; security headers and safe defaults are required.
- Logs MUST redact sensitive data (PII, tokens, connection strings, raw prompts/queries).

Rationale: security failures are high-impact and costly to remediate late.

### II. Clean Architecture Boundaries
The codebase MUST follow Clean Architecture with strict dependency direction.

Non-negotiable rules:
- Dependencies MUST point inward only (Presentation -> Application -> Domain; Persistence implements abstractions).
- Domain MUST remain framework-free (no HTTP/EF/Azure SDK/ASP.NET concerns).
- Controllers/endpoints MUST be thin adapters that delegate to Application use cases.
- Cross-cutting concerns MUST be implemented via abstractions and composition, not by leaking infrastructure into Domain.

Rationale: maintainability and testability require hard boundaries.

### III. Execution Discipline & Code Quality
Production-quality completion is the default, not an aspirational target.

Non-negotiable rules:
- Feature work MUST be carried through to an executable, production-quality state in the same change
  set unless the user explicitly approves a narrower slice.
- Placeholder implementations, fake-success paths, and deferred TODOs MUST NOT be introduced to stand in
  for required behavior unless explicitly approved.
- Builds MUST be free of warnings (treat warnings as failures for gated builds).
- New code MUST follow single-responsibility and clear naming; avoid placeholder implementations.
- C# rule: one class/interface per file.
- Async I/O MUST be async/await end-to-end; avoid blocking calls.

Rationale: incomplete work and warning debt hide defects and create false confidence.

### IV. Testing Discipline
Testing is required, not optional.

Non-negotiable rules:
- Each feature MUST include meaningful automated tests (unit and integration as appropriate).
- Target: >= 80% meaningful coverage for ViewModels and Services (when applicable), focused on behavior.
- Tests MUST be deterministic and run in CI/local without manual steps.

Rationale: the system spans multiple clients and agents; regressions are otherwise hard to detect.

### V. Observability & Diagnostics
The system MUST be diagnosable in production-like environments.

Non-negotiable rules:
- Use structured logging (not string concatenation) with consistent event naming.
- Emit health checks and key operational metrics (latency, error rate, throughput).
- Tracing/correlation MUST exist across request boundaries (API -> agents -> dependencies).
- When failures are non-fatal by design, logs MUST make that distinction explicit.

Rationale: multi-agent workflows require strong visibility to debug and operate safely.

### VI. Resilience
External dependencies MUST be treated as unreliable.

Non-negotiable rules:
- All outbound calls MUST have timeouts.
- Use retries with backoff, circuit breakers, and graceful degradation where appropriate.
- Public endpoints MUST be rate limited; apply role-based partitions when applicable.

Rationale: resilience protects availability, cost, and user experience.

### VII. Process & Workflow
Work MUST be planned, reviewable, and reproducible.

Non-negotiable rules:
- Features MUST have a spec and plan (see `specs/` workflow) and must pass a Constitution Check.
- New or upgraded dependencies, infrastructure/ops automation, generated documentation, and cross-cutting
  behavior changes MUST receive explicit user approval before implementation.
- Repository hygiene MUST be maintained; agent-generated status, plan, summary, and analysis files MUST NOT
  be created in the repo root.
- Git history-changing or destructive git operations MUST be human-driven or explicitly approved for the
  current action.

Rationale: the project is security- and architecture-sensitive; process prevents drift.

## Additional Constraints

- **Primary stack**: .NET 10 (C# 13) backend, React (Vite) Web UI, .NET MAUI (Windows-first) Admin app.
- **Primary development environment**: Windows-native solutions are the default; Docker-first alternatives
  require explicit approval.
- **Infrastructure as Code (IaC)**: All infrastructure MUST be provisioned using Pulumi. Do not use Bicep, ARM templates, or Terraform.
- **Deployment path**: GitHub Actions is the only approved deployment path. Direct `pulumi up` is forbidden.
- **Azure operations**: `az` CLI usage is read-only by default. Any create/update/delete action requires
  explicit approval.
- **Container delivery**: Local `docker build`, `docker push`, `az acr build`, and direct ACR pushes are
  forbidden; image publication MUST occur through the pipeline.
- **Secrets management**: environment variables and/or Key Vault only; never commit `.env`-style secrets.
- **Data access**: parameterized SQL only; no dynamic SQL built from untrusted input.
- **Architecture**: keep DTOs/contract shapes in `MotorcycleRAG.Contracts.Models`; Domain enforces invariants.
- **Documentation generation**: Summary, findings, and analysis documents MUST NOT be generated unless the
  user explicitly requests them.

## Development Workflow & Quality Gates

- **Artifact flow**: `specs/<feature>/spec.md` -> `specs/<feature>/plan.md` -> `specs/<feature>/tasks.md`.
- **Approval gates**: Before implementation, capture explicit approval when work introduces dependencies,
  infrastructure/ops automation, generated docs, or cross-cutting behavior.
- **Pre-commit gate**:
  - Build MUST pass with zero warnings.
  - Tests MUST pass (unit/integration/E2E as applicable).
  - No secrets present in staged changes.
  - Clean Architecture dependency direction remains valid.
  - Deployment-affecting changes MUST preserve the GitHub Actions-only delivery path.
  - Manual cloud mutations and local container publication MUST NOT be used to complete the task.

## Governance

- This constitution is the highest authority for engineering practices in this repo.
- Supporting instruction files (`AGENTS.md`, integration instructions, and agent guidance) MUST remain
  consistent with this constitution.
- Amendments MUST include:
  - The rationale for the change.
  - Any migration plan needed to restore compliance.
  - Updates to dependent templates under `.specify/templates/`.
- Versioning policy (SemVer):
  - MAJOR: removing/redefining a principle or changing governance in a breaking way.
  - MINOR: adding a principle/section or materially expanding requirements.
  - PATCH: clarifications, wording, and non-semantic refinements.
- Compliance review expectation: plans and reviews MUST explicitly check for constitution compliance.

**Version**: 1.2.0 | **Ratified**: 2026-02-08 | **Last Amended**: 2026-04-23
