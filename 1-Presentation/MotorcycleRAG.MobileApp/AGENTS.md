# Mobile App Instructions

## Applies to

`1-Presentation/MotorcycleRAG.MobileApp/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Mobile onboarding](../../6-Docs/MotorcycleRAG.MobileApp/onboarding.md)
- [Mobile architecture](../../6-Docs/MotorcycleRAG.MobileApp/architecture.md)
- [Mobile requirements](../../6-Docs/MotorcycleRAG.MobileApp/requirements.md)

## Scoped constraints

- Use CommunityToolkit.Mvvm source generators and Shell navigation through the established navigation abstraction.
- Preserve offline viewing of cached conversations; block new requests when connectivity is unavailable.
- Store tokens only through approved secure storage and use the system browser for authentication. Do not log conversation content.

## Verify

Run `dotnet test --project 5-Test/MotorcycleRAG.MobileApp.Tests` and the affected platform build when available.
