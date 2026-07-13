# Contracts Tests Instructions

## Applies to

`5-Test/MotorcycleRAG.Contracts.Tests/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)

## Scoped constraints

- Test DTO serialization, model validation, and contract invariants without external dependencies.
- Keep tests fast and deterministic.

## Verify

Run `dotnet test --project 5-Test/MotorcycleRAG.Contracts.Tests`.
