# Agent Context: MotorcycleRAG.Application (2-Application / Use Cases)

This file is **application-layer specific** context. Root rules live in `AGENTS.md`.

## What to read first (authoritative)
- Baseline requirements + trust policy: `specs/001-system-spec/spec.md`
- Security checklist: `specs/001-system-spec/checklists/asvs-v5-level2.md`

## What belongs here (in this repo)
- Use-case orchestration (commands/queries/handlers)
- Interfaces/abstractions for infrastructure (repositories, external services)
- Authorization decisions that are policy-like (enforce “who can do what” without framework specifics)

## What must NOT be here
- No HTTP concerns (that’s Presentation)
- No SQL/Azure/SDK usage (that’s Persistence)
- No domain invariants baked into DTOs (those belong in Domain entities/value objects)

## Useful commands
- Test: `dotnet test`