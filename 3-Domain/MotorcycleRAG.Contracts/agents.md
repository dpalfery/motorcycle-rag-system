# Agent Context: MotorcycleRAG.Contracts (3-Domain / Interfaces)

This file is **contracts-project specific** context. Root rules live in `AGENTS.md`.

## What to read first (authoritative)
- DTO vs Contracts vs Domain guidance: `AGENTS.md`
- System baseline: `specs/001-system-spec/spec.md`

## What belongs in `MotorcycleRAG.Contracts`
- Interfaces only (repositories, service abstractions, factories)
- Signatures may reference Domain types (this repo treats Contracts as “domain-owned abstractions”)

## What must NOT be here
- No DTOs/models (shared DTO shapes live in `3-Domain/MotorcycleRAG.Contracts.Models`)
- No implementations (those belong in Persistence / Application / Presentation depending on concern)