# Agent Context: MotorcycleRAG.Persistence (4-Persistence / Infrastructure)

This file is **persistence-layer specific** context. Root rules live in `AGENTS.md`.

## What to read first (authoritative)
- Security + secrets rules: `AGENTS.md`
- Environment variables naming: `6-Docs/environment-variables.md` (API expects `MCR_API_*` for most infra dependencies)
- Baseline system requirements: `specs/001-system-spec/spec.md`

## What this project is responsible for
- Implement repository/service interfaces from `3-Domain/MotorcycleRAG.Contracts`
- Data access + external service integrations (SQL Server, Azure services)

## Project-specific constraints
- SQL access must be parameterized (no string concatenation).
- No secrets in code/config; use environment variables / managed identity.
- Keep dependencies one-way: Persistence depends inward on Domain/Contracts/Base, never on Application/Presentation.

## Useful commands
- Run tests: `dotnet test`