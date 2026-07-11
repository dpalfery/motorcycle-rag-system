# Unit Tests Instructions

## Applies to

`5-Test/tests/MotorcycleRAG.UnitTests/` only. Read the repository [AGENTS.md](../../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../../6-Docs/rules/architecture-general.md)
- affected component documentation

## Scoped constraints

- Keep tests fast and deterministic. Cover pure 0-Base, Application, and Domain logic without database, HTTP, Azure, or filesystem dependencies.
- Prefer a small fake or mock for an interface implementation.

## Verify

Run `dotnet test --project 5-Test/tests/MotorcycleRAG.UnitTests`.
