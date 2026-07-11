# Agent Context: MotorcycleRAG.UnitTests

## Documentation

Before changing this cataloged component, read the [documentation standard](../../../6-Docs/documentation-standard.md) and [component catalog](../../../6-Docs/catalog.md), then update the canonical documentation when applicable.

## What this test suite covers
- Unit tests for `0-Base`, `2-Application`, and `3-Domain` projects
- Pure logic only (no DB/HTTP/Azure; no filesystem)

## Project-specific expectations
- Keep tests fast and deterministic.
- If a test needs an interface implementation, prefer a simple fake/mock instead of bringing in infrastructure.

## Useful commands
- Run: `dotnet test --project 5-Test/tests/MotorcycleRAG.UnitTests`
