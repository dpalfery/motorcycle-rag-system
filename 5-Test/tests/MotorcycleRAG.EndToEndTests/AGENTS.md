# End-to-End Tests Instructions

## Applies to

`5-Test/tests/MotorcycleRAG.EndToEndTests/` only. Read the repository [AGENTS.md](../../../AGENTS.md) first.

## Read before changing

- [System requirements](../../../6-Docs/system/requirements.md)
- affected application requirements and onboarding documentation

## Scoped constraints

- Exercise production-shaped user journeys only against non-production environments and dedicated test identities.
- Fail loudly when required prerequisites are unavailable; do not introduce hidden substitutes or log sensitive request content.

## Verify

Run `dotnet test --project 5-Test/tests/MotorcycleRAG.EndToEndTests`.
