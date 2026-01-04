# Agent Context: MotorcycleRAG.Contracts.Models (3-Domain / Shared DTO Shapes)

This file is **supplemental** to the root `AGENTS.md`. It exists to prevent “DTO drift” and keep contract models clean and stable across boundaries in *this repository*.

## What to read first (authoritative)
- Root rules: `AGENTS.md` (especially: Model vs DTO + secrets/logging rules)
- Baseline requirements + contracts: `specs/001-system-spec/spec.md` and `specs/001-system-spec/contracts/openapi.yaml`
- Security checklist: `specs/001-system-spec/checklists/asvs-v5-level2.md`

## What `Contracts.Models` is for (in this repo)
Use `MotorcycleRAG.Contracts.Models` for **data-only contract shapes** that cross boundaries:
- API request/response DTOs
- Application use-case input/output models (when shared)
- Shared “wire” shapes used between Presentation/Application/Persistence

These types should be:
- Serialization-friendly (records/classes with simple properties)
- Boring and stable (avoid churn)
- Free of behavior/invariants (those belong in Domain)

## What must NOT be here
- No interfaces (those belong in `MotorcycleRAG.Contracts`)
- No implementations/services (those belong in Application/Persistence/Presentation)
- No framework dependencies (ASP.NET Core attributes, EF Core types, Azure SDKs, etc.)
- No domain behavior/invariants (put those in `MotorcycleRAG.Domain` entities/value objects)

## Dependency boundaries (strict)
- Allowed: `System.*`, `MotorcycleRAG.Core` (0-Base), and (when truly necessary) `MotorcycleRAG.Domain`
- Forbidden: any references to Presentation/Application/Persistence frameworks or SDKs

## Security & logging gotchas
- Do not add fields that encourage logging raw user prompts/query text.
- Avoid storing secrets in DTOs; secret values must flow via environment variables/config at runtime.
- When adding new externally-exposed fields, prefer explicit naming and consider whether the field is PII.

## Quick checklist before adding/changing a DTO
1. Is this a transport shape (not a business rule)? If not, it’s not a DTO.
2. Is this shape already defined in the OpenAPI contract? If yes, align names/types.
3. Will adding this field require clients to store secrets or sensitive data? If yes, redesign.
4. Can the type be represented without any framework attributes? If no, redesign.
