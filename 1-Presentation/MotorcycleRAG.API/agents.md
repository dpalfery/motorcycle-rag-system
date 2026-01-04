# Agent Context: MotorcycleRAG.API (1-Presentation / ASP.NET Core)

This file adds **API-specific** reminders. For global rules (security, clean architecture, secrets), use the root `AGENTS.md`.

## What to read first (authoritative)
- Root rules: `AGENTS.md` (security + dependency rule)
- Baseline requirements: `specs/001-system-spec/spec.md`
- OpenAPI contract reference: `specs/001-system-spec/contracts/openapi.yaml`
- Environment variable naming: `6-Docs/environment-variables.md` (look for `MCR_API_*`)

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
- Secrets must come from environment variables/user-secrets only (no connection strings/keys in config files).
- Authorize every admin action explicitly (Entra app roles like `Admin` / `Operator` / `Viewer`).

## Useful commands
- Run: `dotnet run --project 1-Presentation/MotorcycleRAG.API`
- Test: `dotnet test`