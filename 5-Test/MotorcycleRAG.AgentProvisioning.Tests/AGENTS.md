# Agent Provisioning Tests Instructions

## Applies to

`5-Test/MotorcycleRAG.AgentProvisioning.Tests/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)

## Scoped constraints

- Test provisioning service logic and client adapters without real Azure Foundry or network calls.
- Prefer a small fake or mock for interface implementations.
- Do not log tokens, secrets, or PII in test output.

## Verify

Run `dotnet test --project 5-Test/MotorcycleRAG.AgentProvisioning.Tests`.
