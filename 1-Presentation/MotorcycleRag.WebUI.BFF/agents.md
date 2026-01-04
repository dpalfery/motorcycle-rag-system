# Agent Context: MotorcycleRag.WebUI.BFF (1-Presentation / BFF)

This file is **BFF-specific** context. For global rules (security, clean architecture), use the root `AGENTS.md`.

## What to read first (authoritative)
- Baseline requirements (auth split + trust policy): `specs/001-system-spec/spec.md`
- Environment variable naming: `6-Docs/environment-variables.md` (look for `MCR_BFF_*`)
- UI stack context: `6-Docs/ui-technology-stack.md`

## What this project is responsible for
- Browser-facing backend for the WebUI (session/auth boundary)
- Reverse proxying and/or UI-specific aggregation (commonly via YARP)

## Security and auth boundaries
- Prefer server-side session management (HTTP-only cookies) and PKCE/OIDC flows.
- Do not forward client secrets to the browser; secrets stay in server environment variables.
- Keep token/claims handling consistent with the system spec:
	- Customers: Entra External ID / B2C
	- Admins: Entra ID workforce (admin UI is MAUI)

## Useful commands
- Run: `dotnet run --project 1-Presentation/MotorcycleRag.WebUI.BFF`
- Test: `dotnet test`