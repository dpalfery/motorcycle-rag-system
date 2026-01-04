# Agent Context: MotorcycleRAG.UnitTests

## What this test suite covers
- Unit tests for `0-Base`, `2-Application`, and `3-Domain` projects
- Pure logic only (no DB/HTTP/Azure; no filesystem)

## Project-specific expectations
- Keep tests fast and deterministic.
- If a test needs an interface implementation, prefer a simple fake/mock instead of bringing in infrastructure.

## Useful commands
- Run: `dotnet test --project 5-Test/tests/MotorcycleRAG.UnitTests`
