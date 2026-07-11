# Mobile App Tests Instructions

## Applies to

`5-Test/MotorcycleRAG.MobileApp.Tests/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Mobile requirements](../../6-Docs/MotorcycleRAG.MobileApp/requirements.md)
- [Mobile architecture](../../6-Docs/MotorcycleRAG.MobileApp/architecture.md)

## Scoped constraints

- Test ViewModels, services, storage, and user-memory behavior without requiring a device/emulator unless a dedicated harness exists.
- Keep tests deterministic and do not log tokens or conversation text.

## Verify

Run `dotnet test --project 5-Test/MotorcycleRAG.MobileApp.Tests`.
