# Agent Context: MotorcycleRAG.IntegrationTests

## What this test suite covers
- Cross-layer slices through API/Application/Domain/Persistence
- Authn/authz and request/response contracts where feasible

## Project-specific expectations
- Prefer running against a dedicated test configuration (e.g., `appsettings.Testing.json`) and isolated resources.
- Do not embed secrets; use environment variables/user-secrets.

## Useful commands
- Run: `dotnet test --project 5-Test/tests/MotorcycleRAG.IntegrationTests`
