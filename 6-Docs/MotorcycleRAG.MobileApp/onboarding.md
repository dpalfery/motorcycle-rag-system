---
id: mobile/onboarding
title: MotorcycleRAG Mobile App Developer Onboarding
doc-type: onboarding
status: current
component: MotorcycleRAG Mobile App
source-root: 1-Presentation/MotorcycleRAG.MobileApp
owner: Mobile maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# MotorcycleRAG Mobile App Developer Onboarding

## Prerequisites

- .NET 10 SDK with the .NET MAUI workload installed.
- For Mac Catalyst development: Xcode and the installed Mac Catalyst workload. For Windows development: the Windows SDK and a compatible Windows target.
- An Entra application registration configured for the mobile client and the API scopes in the embedded application configuration.
- A reachable HTTPS MotorcycleRAG API. The client rejects a non-HTTPS base URL.

## Run locally

1. Restore the solution from the repository root with `dotnet restore MotorcycleRAG.sln`.
2. Review `appsettings.json` and the development configuration embedded by the project. Supply only approved configuration values; do not store client secrets in the application.
3. Build the platform target. For example, on macOS run `dotnet build -f net10.0-maccatalyst --project 1-Presentation/MotorcycleRAG.MobileApp`.
4. Launch through the IDE or a supported `dotnet` run target. Choose the real device/simulator appropriate to the target framework.
5. Sign in through the system browser, then submit a query to verify API connectivity and local persistence.

## Debugging

- Set breakpoints in a ViewModel for user interaction and in the associated service for API, authentication, or persistence behavior.
- Inspect `motorcyclerag.db3` in the platform app-data directory to diagnose local conversations, messages, citations, and user-memory records. Treat the database as sensitive user data.
- Authentication uses MSAL's system-browser flow (`UseEmbeddedWebView = false`). Diagnose redirect URI and Entra registration mismatches before modifying authentication code.
- The API client throws distinct authentication, rate-limit, and API exceptions. Inspect the exception type and correlation-safe logs rather than displaying raw response bodies.

## Non-standard procedures

- The local storage quota is 100 MB. The storage service prunes oldest conversations and extracts user-memory candidates before deletion; do not bypass it with direct SQLite writes.
- The mobile target frameworks are conditional on the host OS. Do not assume Android or iOS targets are configured in the current project file simply because MAUI supports them.
- PDF rendering is platform-specific behind `IPdfRenderer` and `IPdfViewerService`; keep shared ViewModels free of platform-specific APIs.
