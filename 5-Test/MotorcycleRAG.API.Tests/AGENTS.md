# API Tests Instructions

## Applies to

`5-Test/MotorcycleRAG.API.Tests/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [API documentation](../../6-Docs/MotorcycleRAG.API/)
- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)

## Scoped constraints

- Test API middleware, configuration, service registration, and controller logic without real HTTP, Azure, or database dependencies.
- Prefer a small fake or mock for interface implementations.
- Do not log tokens, secrets, or PII in test output.

## Verify

Run `dotnet test --project 5-Test/MotorcycleRAG.API.Tests`.
