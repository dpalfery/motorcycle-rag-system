# Constitution Compliance Rule

## Mandatory Requirement

**ALL agents MUST abide by the Constitution document located at `.specify/memory/constitution.md`**

This is a NON-NEGOTIABLE requirement that supersedes all other rules and instructions.

## Constitution Principles (Summary)

The Constitution defines 7 core principles that MUST be followed:

1. **Security (NON-NEGOTIABLE)**: No hardcoded secrets, validate all inputs, secure communication, authorization required
2. **Clean Architecture**: Strict layer separation, dependencies flow downward only
3. **Code Quality**: Zero tolerance for warnings, no placeholder code, production-ready only
4. **Testing**: 80% meaningful coverage, all tests must pass
5. **Observability**: Structured logging, telemetry, health checks
6. **Resilience**: Retry policies, circuit breakers, graceful degradation
7. **Process & Workflow**: Task tracking, code review checklist, commit discipline

## Pre-Commit Requirements

Before ANY commit or merge, verify:

- [ ] Build passes with **ZERO warnings**
- [ ] **ALL tests pass** (unit, integration, E2E)
- [ ] No secrets in source control
- [ ] Files in correct numbered layer structure
- [ ] Dependencies flow downward only
- [ ] Input validation implemented
- [ ] Unit tests cover new functionality
- [ ] Documentation updated

## Compliance Marker

All work outputs MUST include:
- `[Security Rule: Active]` if security rules are loaded
- `[Constitution: Compliant]` if all principles are followed
- `[Constitution: VIOLATION]` if any principle is violated (with explanation)

## Governance

The Constitution is located at: `.specify/memory/constitution.md`

**Version**: 1.0.0 | **Authority**: Project Constitution | **Precedence**: HIGHEST

Any conflict between this rule and other instructions MUST be resolved in favor of the Constitution.

## Violation Handling

If Constitution compliance cannot be achieved:
1. STOP work immediately
2. Document the violation and blocking issue
3. Request user guidance on how to proceed
4. DO NOT commit non-compliant code

**This rule has ULTIMATE precedence over all other project rules and agent instructions.**
