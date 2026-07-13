# Contracts Models Instructions

## Applies to

`3-Domain/MotorcycleRAG.Contracts.Models/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)
- [System architecture](../../6-Docs/system/architecture.md)

## Scoped constraints

- This project contains shared, data-only DTOs that cross boundaries.
- Keep models serialization-friendly and free of interfaces, implementations, framework dependencies, and business invariants.
- Do not add fields that expose secrets or encourage logging prompts, query text, or PII.
- This project is excluded from the per-file/per-class 85% line-coverage gate because it holds pure data-carrier DTOs. Behavior-bearing members (factories, computed properties, custom converters) remain subject to direct unit tests in `5-Test/MotorcycleRAG.Contracts.Tests/`. See [`6-Docs/DevOps/overview.md`](../../6-Docs/DevOps/overview.md) §4.1 (Coverage exclusions).

## Verify

Run the affected `dotnet test` projects and contract tests.
