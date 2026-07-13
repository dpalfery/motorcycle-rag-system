# Persistence Tests Instructions

## Applies to

`5-Test/MotorcycleRAG.Persistence.Tests/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)
- affected component documentation

## Scoped constraints

- Test Dapper repositories, Azure SDK wrappers, health checks, telemetry, and pipeline services using mocked `IDbConnection`/`ISqlConnectionFactory` and service fakes.
- No real database, Azure, or network calls.
- Do not log tokens, secrets, or PII in test output.

## Verify

Run `dotnet test --project 5-Test/MotorcycleRAG.Persistence.Tests`.
