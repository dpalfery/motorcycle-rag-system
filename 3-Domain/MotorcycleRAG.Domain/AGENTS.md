# Domain Instructions

## Applies to

`3-Domain/MotorcycleRAG.Domain/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)
- [System architecture](../../6-Docs/system/architecture.md)

## Scoped constraints

- Domain owns framework-free entities, value objects, domain events, domain services, and business invariants.
- Do not add transport DTOs, infrastructure SDKs, HTTP clients, ORMs, or logging sinks.
- Model trust tiers and business rules here; enforce caller-specific policies in Application or Presentation.

## Verify

Run the affected unit-test projects.
