# Agent Context: MotorcycleRag.WebUI (1-Presentation / Frontend)

This file is **WebUI-specific** context. For global rules (security, clean architecture), use the root `AGENTS.md`.

## What to read first (authoritative)
- UI stack guidance: `6-Docs/ui-technology-stack.md`
- Baseline requirements: `specs/001-system-spec/spec.md` (US1/US1a)
- If generating API types: `specs/001-system-spec/contracts/openapi.yaml`

## What this project is responsible for
- End-user UI for asking questions and viewing answers with citations
- Delegates auth/session handling to the BFF (prefer server-side sessions / HTTP-only cookies)

## Project-specific constraints
- Styling must remain CSP-friendly: prefer MUI v7 + Pigment CSS patterns (avoid `unsafe-inline`).
- Do not store sensitive data in browser storage.
- Keep client code focused on UI concerns; don’t embed business logic that belongs in Application/Domain.

## Useful commands (typical)
- Dev: `npm run dev`
- Build: `npm run build`
- Tests: `npm run test`
- API type generation (if configured): `npm run generate:api`
