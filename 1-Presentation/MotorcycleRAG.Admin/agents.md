# Agent Context: MotorcycleRAG.Admin (1-Presentation / MAUI Admin)

This file is **Admin-app specific** context. Global rules (secrets, auth separation, clean architecture) live in the root `AGENTS.md`.

## What to read first (authoritative)
- MAUI Golden Path: `6-Docs/MAUI_ARCHITECT.md`
- Baseline requirements: `specs/001-system-spec/spec.md` (see US3a + US7)
- Configuration rules: root `AGENTS.md`. Admin .NET code uses approved configuration services; environment variables are only for Python local processor values that the Admin app sets at run time.

## What this project is responsible for
- Windows-first ingestion operations UI (upload PDFs/CSVs, trigger/monitor ingestion)
- Local processing mode for chunking/vectorization (per US3a requirements)
- Admin-only authentication (Entra ID workforce) + role-based authorization

## Design constraints (project-specific)
- MVVM via `CommunityToolkit.Mvvm` source generators (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`)
- Shell navigation via an `INavigationService` wrapper (ViewModels must stay UI-agnostic)
- Settings access via an `ISettingsService` wrapper (no direct `Preferences.*` usage in ViewModels)
- Connectivity/resilience checks before/around HTTP calls

## MAUI Thread Affinity (non-optional)
- The MAUI main thread is for UI work only: binding updates, `ObservableCollection` mutation, property-change-visible state, dialogs, navigation, page lifecycle UI transitions, and file-picker/browser launcher interactions.
- All dependent work must run off the MAUI thread on worker threads: HTTP calls, auth/MSAL calls, settings persistence, secure storage, file system access, local processor control, PDF/CSV chunking, embedding generation, and any database/blob/persistence operation.
- Never do blocking waits on a UI path. Do not use `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` in code that can run from MAUI pages, viewmodels, shell events, or bindings.
- After background work completes, hop back to the MAUI thread only to apply UI-visible results. Do not update `ObservableObject` properties or `ObservableCollection` instances from worker threads.

## Background Polling And Resilience (non-optional)
- Do not stack multiple retry/circuit-breaker layers for the same admin HTTP request path. Prefer one `HttpClientFactory` resilience pipeline with explicit settings over nested Polly wrappers.
- Automatic polling must have a failure budget. After no more than 5 consecutive transient failures/timeouts for a section, stop background polling for that section and surface one clear problem state in the UI. Manual refresh may re-arm polling.
- Expected transient background failures must not spam logs with full exception stacks on every poll tick. Log a concise diagnostic only when polling is paused or when the user explicitly triggers the action.
- Keep polling conservative. Do not refresh large jobs/status collections every few seconds unless there is active work that truly needs fast updates.

## Security and data handling
- Store tokens only in secure storage (implemented via MSAL Extensions w/ DPAPI).
- **Client Isolation**: Ensure tokens include `azp` claim matching configured Client ID.
- Avoid logging file paths, query text, or document content; keep logs to IDs + status.

## Useful commands
- Build/run (Windows): `dotnet build -t:Run -f net10.0-windows10.0.19041.0 --project 1-Presentation/MotorcycleRAG.Admin`
- Test: `dotnet test`
