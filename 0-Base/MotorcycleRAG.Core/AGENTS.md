# Core Instructions

## Applies to

`0-Base/MotorcycleRAG.Core/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)
- [System architecture](../../6-Docs/system/architecture.md)

## Scoped constraints

- Core holds low-level, framework-free building blocks shared broadly across layers.
- Allow only the .NET BCL and other 0-Base projects. Do not reference Application, Domain, Persistence, Presentation, or infrastructure SDKs.
- Put business invariants in Domain and I/O in outer layers. Do not add configuration or logging helpers that expose secrets, prompts, or PII.

## Verify

Run the affected `dotnet test` projects.
