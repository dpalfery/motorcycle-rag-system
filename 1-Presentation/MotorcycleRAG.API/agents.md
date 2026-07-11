# Agent Context: MotorcycleRAG.API (1-Presentation / ASP.NET Core)

## Documentation

Before changing this cataloged component, read the [documentation standard](../../6-Docs/documentation-standard.md) and [component catalog](../../6-Docs/catalog.md), then update the canonical documentation when applicable.

This file adds **API-specific** reminders. For global rules (security, clean architecture, secrets), use the root `AGENTS.md`.

## What to read first (authoritative)
- Root rules: `AGENTS.md` (security + dependency rule)
- Baseline requirements: `specs/001-system-spec/spec.md`
- OpenAPI contract reference: `specs/001-system-spec/contracts/openapi.yaml`
- Configuration rules: root `AGENTS.md`. API C# code uses Azure App Configuration + Key Vault, not direct environment-variable reads.

## What this project is responsible for
- Public HTTP API (query + ingestion pipeline + health)
- Authentication + authorization enforcement at the edge (customer vs admin)
- Input validation + safe error handling (no sensitive detail leakage)

## Boundaries (clean architecture)
- Can depend on: `2-Application`, `0-Base`
- Must NOT depend on: `4-Persistence` (no “reach-around” data access)
- Keep controllers/endpoints thin: map HTTP ⇄ use-case DTOs, call application handlers, return responses

## Security and observability gotchas
- Never log raw query text/prompts/PII; prefer correlation/query IDs (see `spec.md` requirements FR-004b, FR-021).
- Secrets must come from Azure Key Vault through configuration, never from appsettings or direct environment-variable reads.
- **Client Isolation**: Admin endpoints must enforce `azp` matches Admin Client ID.
- **Rate Limiting**: Use role-based limits (`Demo`, `Pro`, `Roadrunner`, `Admin`).
- Authorize every admin action explicitly (Entra app roles like `Admin` / `Operator` / `Viewer`).

## Useful commands
- Run: `dotnet run --project 1-Presentation/MotorcycleRAG.API`
- Test: `dotnet test`
