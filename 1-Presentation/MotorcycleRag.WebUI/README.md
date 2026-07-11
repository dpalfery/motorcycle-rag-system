# MotorcycleRAG Web UI

MotorcycleRAG Web UI is the browser client for the end-user motorcycle-question experience. The React 19/Vite SPA handles presentation, routing, local UI state, and calls to same-origin endpoints. `MotorcycleRag.WebUI.BFF` is its paired ASP.NET Core Backend for Frontend: it owns OpenID Connect login/session cookies and proxies `/api/*` traffic to MotorcycleRAG API.

The separation prevents the SPA from managing API bearer tokens directly. The Web UI must be hosted behind its BFF for authenticated end-to-end behavior.

## Documentation

- [Developer onboarding](../../6-Docs/MotorcycleRag.WebUI/onboarding.md)
- [Architecture](../../6-Docs/MotorcycleRag.WebUI/architecture.md)
- [Requirements](../../6-Docs/MotorcycleRag.WebUI/requirements.md)
