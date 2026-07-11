# Web UI Instructions

## Applies to

`1-Presentation/MotorcycleRag.WebUI/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Web UI onboarding](../../6-Docs/MotorcycleRag.WebUI/onboarding.md)
- [Web UI architecture](../../6-Docs/MotorcycleRag.WebUI/architecture.md)
- [UI technology reference](../../6-Docs/reference/ui-technology-stack.md)

## Scoped constraints

- Keep browser code focused on presentation; session and token handling belongs in the BFF.
- Use CSP-compatible styling patterns and do not store sensitive data in browser storage.
- Keep generated API types aligned with the API's current public contract.

## Verify

Run the affected `npm run build` and test commands.
