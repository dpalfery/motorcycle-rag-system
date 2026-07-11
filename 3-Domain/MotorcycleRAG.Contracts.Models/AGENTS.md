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

## Verify

Run the affected `dotnet test` projects and contract tests.
