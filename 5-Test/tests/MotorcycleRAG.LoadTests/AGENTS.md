# Load Tests Instructions

## Applies to

`5-Test/tests/MotorcycleRAG.LoadTests/` only. Read the repository [AGENTS.md](../../../AGENTS.md) first.

## Read before changing

- [System requirements](../../../6-Docs/system/requirements.md)
- affected API or BFF documentation

## Scoped constraints

- Test API/BFF throughput only against approved non-production targets with non-sensitive data.
- Never point load tests at production.

## Verify

Run `dotnet test --project 5-Test/tests/MotorcycleRAG.LoadTests`.
