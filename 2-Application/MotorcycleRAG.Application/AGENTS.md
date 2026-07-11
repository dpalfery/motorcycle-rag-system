# Application Layer Instructions

## Applies to

`2-Application/MotorcycleRAG.Application/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)
- [System architecture](../../6-Docs/system/architecture.md)
- [System requirements](../../6-Docs/system/requirements.md)

## Scoped constraints

- Keep use-case orchestration and application service implementations in `Services`.
- Do not add interfaces here; they belong in Contracts. Shared DTOs belong in Contracts.Models.
- Do not add HTTP, SQL, Azure SDK, or persistence concerns, or encode Domain invariants in DTOs.

## Verify

Run the affected `dotnet test` projects.
