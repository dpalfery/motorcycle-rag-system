# Agent Context: MotorcycleRAG.Persistence (4-Persistence / Infrastructure)

## Documentation

Before changing this cataloged component, read the [documentation standard](../../6-Docs/documentation-standard.md) and [component catalog](../../6-Docs/catalog.md), then update the canonical documentation when applicable.

This file is **persistence-layer specific** context. Root rules live in `AGENTS.md`.

## What to read first (authoritative)
- Security + secrets rules: `AGENTS.md`
- Configuration rules: root `AGENTS.md`. C# infrastructure integrations use Azure App Configuration + Key Vault, not direct environment-variable reads.
- Baseline system requirements: `specs/001-system-spec/spec.md`

## What this project is responsible for
- Implement repository/service interfaces from `3-Domain/MotorcycleRAG.Contracts`
- Data access + external service integrations (SQL Server, Azure services)

## Project-specific constraints
- SQL access must be parameterized (no string concatenation).
- No secrets in code/config; use Azure Key Vault references through configuration and managed identity.
- Keep dependencies one-way: Persistence depends inward on Domain/Contracts/Base, never on Application/Presentation.

## Useful commands
- Run tests: `dotnet test`
