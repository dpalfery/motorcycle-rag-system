# Web UI BFF Instructions

## Applies to

`1-Presentation/MotorcycleRag.WebUI.BFF/` only. Read the repository [AGENTS.md](../../AGENTS.md) first.

## Read before changing

- [Web UI architecture](../../6-Docs/MotorcycleRag.WebUI/architecture.md)
- [Web UI requirements](../../6-Docs/MotorcycleRag.WebUI/requirements.md)
- [UI technology reference](../../6-Docs/reference/ui-technology-stack.md)

## Scoped constraints

- The BFF owns the browser authentication/session boundary and UI-specific aggregation or proxying.
- Prefer server-side sessions and HTTP-only cookies. Do not expose client secrets or access tokens to the browser.
- Keep customer and Admin identity flows distinct; the Admin UI is the Tauri desktop application.

## Verify

Run the affected `dotnet test` projects.
