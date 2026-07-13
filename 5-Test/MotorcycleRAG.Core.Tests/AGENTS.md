# Core Tests Instructions

## Applies to

`5-Test/MotorcycleRAG.Core.Tests/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)

## Scoped constraints

- Test shared kernel utilities, constants, result types, and exception types without external dependencies.
- Keep tests fast, deterministic, and pure-logic only.

## Verify

Run `dotnet test --project 5-Test/MotorcycleRAG.Core.Tests`.
