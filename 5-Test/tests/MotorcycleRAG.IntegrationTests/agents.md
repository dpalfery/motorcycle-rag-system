# Agent Context: MotorcycleRAG.IntegrationTests

## Documentation

Before changing this cataloged component, read the [documentation standard](../../../6-Docs/documentation-standard.md) and [component catalog](../../../6-Docs/catalog.md), then update the canonical documentation when applicable.

## What this test suite covers
- Cross-layer slices through API/Application/Domain/Persistence
- Authn/authz and request/response contracts where feasible

## Project-specific expectations
- Prefer running against a dedicated test configuration (e.g., `appsettings.Testing.json`) and isolated resources.
- Do not embed secrets. .NET tests should use `IConfiguration` test providers, App Configuration abstractions, or Key Vault references; do not add environment-variable setup for .NET application settings. Environment variables are only acceptable for the Python local processor runtime surface set by the Admin app.

## Useful commands
- Run: `dotnet test --project 5-Test/tests/MotorcycleRAG.IntegrationTests`
