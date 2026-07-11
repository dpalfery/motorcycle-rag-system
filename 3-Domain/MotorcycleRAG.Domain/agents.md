# Agent Context: MotorcycleRAG.Domain (3-Domain / Business Rules)

## Documentation

Before changing this cataloged component, read the [documentation standard](../../6-Docs/documentation-standard.md) and [component catalog](../../6-Docs/catalog.md), then update the canonical documentation when applicable.

This file is **domain-layer specific** context. Global rules live in `AGENTS.md`.

## What to read first (authoritative)
- Clean architecture + DTO placement rules: `AGENTS.md`
- Trust policy + core requirements: `specs/001-system-spec/spec.md`

## What belongs here (in this repo)
- Entities/value objects that enforce invariants (the “source of truth”)
- Domain events and domain services (framework-free)

## What must NOT be here
- No transport DTOs for HTTP/UI/persistence (put shared DTOs in `3-Domain/MotorcycleRAG.Contracts.Models`)
- No infrastructure code, SDKs, ORMs, HTTP clients, or logging sinks

## Domain-specific reminders
- The system has explicit trust-tier rules (Tier A/B/C). If you model this concept, prefer a value object / enum here and keep policy enforcement in Application.

## Useful commands
- Run unit tests: `dotnet test`
