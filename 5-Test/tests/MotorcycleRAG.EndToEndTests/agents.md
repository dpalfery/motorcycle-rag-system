# Agent Context: MotorcycleRAG.EndToEndTests

## What this test suite covers
- Full user journeys across the system (UI/BFF/API) where the harness exists

## Project-specific expectations
- Use non-production environments and dedicated test identities.
- Avoid leaking query text or secrets into test logs/artifacts.

## Useful commands
- Run: `dotnet test --project 5-Test/tests/MotorcycleRAG.EndToEndTests`
