# API Instructions

## Applies to

`1-Presentation/MotorcycleRAG.API/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [API architecture](../../6-Docs/MotorcycleRAG.API/architecture.md)
- [API requirements](../../6-Docs/MotorcycleRAG.API/requirements.md)
- [System security requirements](../../6-Docs/system/requirements.md)

## Scoped constraints

- Keep HTTP endpoints and controllers thin: validate/map HTTP requests, invoke use cases, and return safe responses.
- API may depend on Application and Core; do not bypass Application with direct Persistence access.
- Explicitly authorize protected actions, validate Admin client isolation, rate-limit public endpoints, and never log raw queries, prompts, or PII.

## Verify

Run `dotnet test` for the affected API and integration-test projects.
