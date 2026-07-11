# Integration Tests Instructions

## Applies to

`5-Test/tests/MotorcycleRAG.IntegrationTests/` only. Read the repository [AGENTS.md](../../../AGENTS.md) first.

## Read before changing

- [System requirements](../../../6-Docs/system/requirements.md)
- [Architecture placement rules](../../../6-Docs/rules/architecture-general.md)

## Scoped constraints

- Test cross-layer behavior, authorization, and public contracts using isolated test configuration/resources.
- Do not embed secrets or use environment variables for .NET application settings.

## Verify

Run `dotnet test --project 5-Test/tests/MotorcycleRAG.IntegrationTests`.
