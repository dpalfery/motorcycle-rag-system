# Agent Context: MotorcycleRAG.Admin (1-Presentation / MAUI Admin)

This file is **Admin-app specific** context. Global rules (secrets, auth separation, clean architecture) live in the root `AGENTS.md`.

## What to read first (authoritative)
- MAUI Golden Path: `6-Docs/MAUI_ARCHITECT.md`
- Baseline requirements: `specs/001-system-spec/spec.md` (see US3a + US7)
- Environment variables: `6-Docs/environment-variables.md` (look for `MCR_ADMIN_*`)

## What this project is responsible for
- Windows-first ingestion operations UI (upload PDFs/CSVs, trigger/monitor ingestion)
- Local processing mode for chunking/vectorization (per US3a requirements)
- Admin-only authentication (Entra ID workforce) + role-based authorization

## Design constraints (project-specific)
- MVVM via `CommunityToolkit.Mvvm` source generators (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`)
- Shell navigation via an `INavigationService` wrapper (ViewModels must stay UI-agnostic)
- Settings access via an `ISettingsService` wrapper (no direct `Preferences.*` usage in ViewModels)
- Connectivity/resilience checks before/around HTTP calls

## Security and data handling
- Store tokens only in secure storage (implemented via MSAL Extensions w/ DPAPI).
- **Client Isolation**: Ensure tokens include `azp` claim matching configured Client ID.
- Avoid logging file paths, query text, or document content; keep logs to IDs + status.

## Useful commands
- Build/run (Windows): `dotnet build -t:Run -f net10.0-windows10.0.19041.0 --project 1-Presentation/MotorcycleRAG.Admin`
- Test: `dotnet test`