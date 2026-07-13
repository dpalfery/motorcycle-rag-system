# Application Tests Instructions

## Applies to

`5-Test/MotorcycleRAG.Application.Tests/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)
- affected component documentation

## Scoped constraints

- Test application services, use-case handlers, pipeline orchestration, and validators with mocked repositories.
- No real database, HTTP, Azure, or filesystem dependencies.
- Prefer a small fake or mock for interface implementations.

## Verify

Run `dotnet test --project 5-Test/MotorcycleRAG.Application.Tests`.
