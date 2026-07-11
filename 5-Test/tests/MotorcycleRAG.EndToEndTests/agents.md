# Agent Context: MotorcycleRAG.EndToEndTests

## Documentation

Before changing this cataloged component, read the [documentation standard](../../../6-Docs/documentation-standard.md) and [component catalog](../../../6-Docs/catalog.md), then update the canonical documentation when applicable.

## What this test suite covers
- Full user journeys across the system (UI/BFF/API) where the harness exists

## Project-specific expectations
- Use non-production environments and dedicated test identities.
- Avoid leaking query text or secrets into test logs/artifacts.

## Useful commands
- Run: `dotnet test --project 5-Test/tests/MotorcycleRAG.EndToEndTests`
