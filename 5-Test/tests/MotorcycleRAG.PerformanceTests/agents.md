# Agent Context: MotorcycleRAG.PerformanceTests & LoadTests

## What this test suite covers
- Latency/regression testing for key workflows (query + ingestion) where implemented

## Project-specific expectations
- Keep benchmarks reproducible; record environment assumptions in test code (not in new docs).
- Never publish internal endpoints/IDs/secrets in test output.

## Useful commands
- Run: `dotnet test --project 5-Test/tests/MotorcycleRAG.PerformanceTests`
