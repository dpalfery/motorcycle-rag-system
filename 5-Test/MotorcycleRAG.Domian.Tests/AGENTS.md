# Domain Tests Instructions

## Applies to

`5-Test/MotorcycleRAG.Domian.Tests/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)
- affected component documentation

## Scoped constraints

- Test entities, value objects, domain services, and domain events as pure logic without external dependencies.
- No database, HTTP, Azure, or filesystem dependencies.
- Keep tests fast, isolated, and deterministic.

## Verify

Run `dotnet test --project 5-Test/MotorcycleRAG.Domian.Tests`.
