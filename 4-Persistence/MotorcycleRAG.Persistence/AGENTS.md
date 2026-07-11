# Persistence Instructions

## Applies to

`4-Persistence/MotorcycleRAG.Persistence/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)
- [System architecture](../../6-Docs/system/architecture.md)
- [Azure environment](../../6-Docs/AzureEnvironment/agent-access.md) for Azure diagnostics or integrations

## Scoped constraints

- Implement interfaces from Contracts and keep dependencies inward-facing; do not reference Application or Presentation.
- Use parameterized SQL only. Keep external-service and data-access integrations here.
- Obtain configuration and secrets through approved configuration/Key Vault abstractions; never log sensitive values.

## Verify

Run the affected unit and integration tests.
