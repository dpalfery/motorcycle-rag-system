# Web UI BFF Tests Instructions

## Applies to

`5-Test/MotorcycleRag.WebUI.BFF.Tests/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [BFF documentation](../../6-Docs/MotorcycleRag.WebUI/)
- [Architecture placement rules](../../6-Docs/rules/architecture-general.md)

## Scoped constraints

- Test BFF middleware, configuration, service registration, health checks, and YARP proxy logic without real HTTP or Azure dependencies.
- Prefer a small fake or mock for interface implementations.
- Do not log tokens, secrets, or PII in test output.

## Verify

Run `dotnet test --project 5-Test/MotorcycleRag.WebUI.BFF.Tests`.
