# Agent Context: MotorcycleRAG.MobileApp (1-Presentation / MAUI Mobile)

This file is **mobile-app specific** context. Global rules live in the root `AGENTS.md`.

## What to read first (authoritative)
- MAUI Golden Path: `6-Docs/MAUI_ARCHITECT.md`
- Mobile requirements: `specs/001-mobile-app/spec.md` (+ `specs/001-mobile-app/plan.md`)
- Environment variables: `6-Docs/environment-variables.md` (look for `MCR_MOBILE_*`)

## What this project is responsible for
- End-user mobile chat client (iOS/Android/Windows)
- Local persistence of conversations + “user memory” (100MB cap with pruning)
- Customer authentication (Entra External ID / B2C) and safe token storage

## Project-specific constraints
- MVVM via `CommunityToolkit.Mvvm` source generators
- Shell navigation via `INavigationService` abstraction
- Offline-friendly UX: allow viewing cached conversations offline; block new queries without connectivity

## Security and privacy
- Tokens only in secure storage; no secrets in files.
- Treat conversation text as sensitive: avoid logging raw messages/questions.

## Useful commands
- Run (Android example): `dotnet build -t:Run -f net10.0-android --project 1-Presentation/MotorcycleRAG.MobileApp`
- Test: `dotnet test`