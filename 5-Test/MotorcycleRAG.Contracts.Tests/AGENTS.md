# Contracts Tests Instructions

## Applies to

`5-Test/MotorcycleRAG.Contracts.Tests/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)

## Scoped constraints

- Test DTO behavior without external dependencies. Direct unit tests target behavior only — factory methods, computed properties, validation logic, and serialization of custom converters — not per-property get/set round-trips on pure data-carrier DTOs.
- The data-only `MotorcycleRAG.Contracts.Models` project is excluded from the per-file line-coverage gate, so property-padding tests are neither required nor wanted. See [`6-Docs/DevOps/overview.md`](../../6-Docs/DevOps/overview.md) §4.1 (Coverage exclusions).
- Keep tests fast and deterministic.

## Verify

Run `dotnet test --project 5-Test/MotorcycleRAG.Contracts.Tests`.
