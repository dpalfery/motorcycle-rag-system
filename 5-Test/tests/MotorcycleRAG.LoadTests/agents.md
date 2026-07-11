# Agent Context: MotorcycleRAG.LoadTests

## Documentation

Before changing this cataloged component, read the [documentation standard](../../../6-Docs/documentation-standard.md) and [component catalog](../../../6-Docs/catalog.md), then update the canonical documentation when applicable.

## What this test suite covers
- Concurrency and throughput testing for API/BFF endpoints

## Project-specific expectations
- Never point load tests at production.
- Keep test data non-sensitive.

## Useful commands
- Run: `dotnet test --project 5-Test/tests/MotorcycleRAG.LoadTests`
