<!--
Sync Impact Report

- Version change: (template / unversioned) -> 1.0.0
- Modified principles: N/A (template placeholders replaced with concrete principles)
- Added sections: Core Principles (7 principles), Additional Constraints, Development Workflow & Quality Gates, Governance
- Removed sections: Placeholder tokens and example comments
- Templates requiring updates:
  - ✅ updated: .specify/templates/plan-template.md
  - ✅ updated: .specify/templates/tasks-template.md
  - ✅ updated: .specify/templates/spec-template.md
  - N/A: .specify/templates/commands/*.md (folder not present)
- Follow-up TODOs: None
-->

# Motorcycle RAG System Constitution

## Core Principles

### I. Security (NON-NEGOTIABLE)
The system MUST meet OWASP ASVS Level 2 expectations.

Non-negotiable rules:
- Secrets MUST NOT be stored in source control or hardcoded in code/config; use environment variables and/or Key Vault.
- Every externally reachable action MUST be explicitly authorized; default is deny.
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

### III. Code Quality (Zero Warnings)
Production-quality code is the standard.

Non-negotiable rules:
- Builds MUST be free of warnings (treat warnings as failures for gated builds).
- New code MUST follow single-responsibility and clear naming; avoid placeholder implementations.
- C# rule: one class/interface per file.
- Async I/O MUST be async/await end-to-end; avoid blocking calls.

Rationale: warning debt and unclear code scale poorly and hide defects.

### IV. Testing Discipline
Testing is required, not optional.

Non-negotiable rules:
- Each feature MUST include meaningful automated tests (unit and integration as appropriate).
- Target: >= 80% meaningful coverage for ViewModels and Services (when applicable), focused on behavior.
- Tests MUST be deterministic and run in CI/local without manual steps.

Rationale: the system spans multiple clients and agents; regressions are otherwise hard to detect.

### V. Observability
The system MUST be diagnosable in production-like environments.

Non-negotiable rules:
- Use structured logging (not string concatenation) with consistent event naming.
- Emit health checks and key operational metrics (latency, error rate, throughput).
- Tracing/correlation MUST exist across request boundaries (API -> agents -> dependencies).

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
- New dependencies, infra/ops automation, or cross-cutting changes MUST be explicitly approved and documented.
- Repository hygiene MUST be maintained (no agent-generated status/plan output in repo root).
- Git history-changing or destructive git operations MUST be human-driven in this environment.

Rationale: the project is security- and architecture-sensitive; process prevents drift.

## Additional Constraints

- **Primary stack**: .NET 10 (C# 13) backend, React (Vite) Web UI, .NET MAUI (Windows-first) Admin app.
- **Secrets management**: environment variables and/or Key Vault only; never commit `.env`-style secrets.
- **Data access**: parameterized SQL only; no dynamic SQL built from untrusted input.
- **Architecture**: keep DTOs/contract shapes in `MotorcycleRAG.Contracts.Models`; Domain enforces invariants.

## Development Workflow & Quality Gates

- **Artifact flow**: `specs/<feature>/spec.md` -> `specs/<feature>/plan.md` -> `specs/<feature>/tasks.md`.
- **Pre-commit gate**:
  - Build MUST pass with zero warnings.
  - Tests MUST pass (unit/integration/E2E as applicable).
  - No secrets present in staged changes.
  - Clean Architecture dependency direction remains valid.

## Governance

- This constitution is the highest authority for engineering practices in this repo.
- Amendments MUST include:
  - The rationale for the change.
  - Any migration plan needed to restore compliance.
  - Updates to dependent templates under `.specify/templates/`.
- Versioning policy (SemVer):
  - MAJOR: removing/redefining a principle or changing governance in a breaking way.
  - MINOR: adding a principle/section or materially expanding requirements.
  - PATCH: clarifications, wording, and non-semantic refinements.
- Compliance review expectation: plans and reviews MUST explicitly check for constitution compliance.

**Version**: 1.0.0 | **Ratified**: 2026-02-08 | **Last Amended**: 2026-02-08
