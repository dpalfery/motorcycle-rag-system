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

### Model classification and placement

- An **Entity** has stable identity and owns at least one invariant, legal state transition, or other domain behavior. A persistence key, public properties, default initializers, or declarative validation attributes alone do not make a type an Entity.
- A **DTO** is a property bag used to carry data across a boundary. Shared DTOs belong in `MotorcycleRAG.Contracts.Models` and use the `Dto` suffix; use-case-local DTOs belong in Application. Do not put transport, search, or persistence-shaped bags in Domain `Entities`.
- A **value object** is immutable, compared by value, and represents a domain meaning. It may contain behavior, but it has no independent identity and is neither a DTO nor an Entity.
- Entity state must not expose public setters or mutable collections that bypass invariants. Use constructors, factories, and named transition methods that validate every state change.
- A database/persistence row that does not form a shared contract remains private to Persistence. Map it at the Persistence boundary instead of leaking a row-shaped type into Domain or `Contracts.Models`.
- Keep one top-level type per file, and keep the filename, namespace, and `Dto` suffix aligned with the type's classification.

## Verify

Run the affected unit-test projects.
