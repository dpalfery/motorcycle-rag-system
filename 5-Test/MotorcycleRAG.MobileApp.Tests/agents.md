# Agent Context: MotorcycleRAG.MobileApp.Tests (5-Test)

## Documentation

Before changing this cataloged component, read the [documentation standard](../../6-Docs/documentation-standard.md) and [component catalog](../../6-Docs/catalog.md), then update the canonical documentation when applicable.

This file is **mobile-test specific** context. Root rules live in `AGENTS.md`.

## What to read first (authoritative)
- Mobile requirements: `specs/001-mobile-app/spec.md`
- MAUI testing constraints: `6-Docs/MAUI_ARCHITECT.md` (testing strategy section)

## What we test here (in this repo)
- ViewModels and services (logic + orchestration)
- Storage and “user memory” behavior (pruning, persistence), where feasible without UI

## What we do NOT test here
- No UI view snapshot testing unless a dedicated UI test harness exists
- Avoid tests that require device/emulator unless explicitly set up

## Useful commands
- Run tests: `dotnet test --project 5-Test/MotorcycleRAG.MobileApp.Tests`
