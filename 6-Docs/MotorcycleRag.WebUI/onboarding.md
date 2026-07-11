# MotorcycleRAG Web UI Developer Onboarding

## Prerequisites

- Node.js and npm compatible with `package-lock.json`.
- .NET 10 SDK for `MotorcycleRag.WebUI.BFF`.
- A configured MotorcycleRAG API destination and Entra/CIAM application settings for the BFF.
- HTTPS development trust configured for ASP.NET Core when using the BFF locally.

## Run locally

1. In `1-Presentation/MotorcycleRag.WebUI`, run `npm install`.
2. Build the SPA with `npm run build`; the BFF serves its compiled assets from `MotorcycleRag.WebUI.BFF/wwwroot` for an integrated run.
3. Configure the BFF's development settings for its allowed browser origins, Entra/CIAM values, data-protection storage where applicable, and `/api/*` reverse-proxy destination.
4. Start the BFF with `dotnet run --project 1-Presentation/MotorcycleRag.WebUI.BFF` and open the BFF URL. Use `npm run dev` only for frontend-focused work that does not need the authenticated BFF flow.
5. Sign in and verify `/auth/me`, an API query, and sign-out through the BFF origin.

## Debugging

- Frontend: run `npm test`, `npm run lint`, or `npm run test:coverage`; use browser developer tools for rendering and same-origin requests.
- Browser authentication: inspect `/auth/me`, `/auth/login`, and `/auth/logout` traffic. Do not expect access tokens in browser storage or client-side code.
- BFF: debug the authentication controller, YARP configuration, host-header validation, and security-header middleware from the BFF project.
- API requests: check that the browser calls `/api/*` on the BFF origin and that the BFF destination points to the intended API. Do not change the SPA to call a cross-origin API as a workaround.

## Non-standard procedures

- The SPA chat client posts queries to `/api/motorcycles/query` and uses a two-minute browser-side abort timeout. Test cancellation/error UI separately from server failures.
- The BFF can persist data-protection keys to blob storage outside Development. Treat loss of key persistence as a session-continuity issue and diagnose it through BFF health/telemetry.
- The current chat UI contains presentation-state conversation history. Do not represent it as durable user conversation storage unless a server-backed contract is added.
