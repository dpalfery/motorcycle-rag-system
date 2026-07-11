# MotorcycleRAG.AdminDesktop — Agent Context

## Documentation

Before changing this cataloged component, read the [documentation standard](../../6-Docs/documentation-standard.md) and [component catalog](../../6-Docs/catalog.md), then update the canonical documentation when applicable.

Tauri v2 desktop admin console (macOS + Windows 11) that replaces the retired MAUI admin app. Two backends: the cloud .NET API (`MotorcycleRAG.API`, port 7215) and the local Python processor (`2-Application/local-processing-service`, default port 8100).

## Stack

| Layer | Choice |
|---|---|
| Shell | Tauri v2 |
| Frontend | React 19 + TypeScript + Vite |
| Styling | Tailwind CSS v3 (dark surface `#1a1a1a` / orange accent `#ff6600`) |
| State | Zustand (`useAuth`, `useConfig`) |
| Data fetching | TanStack Query v5 (15s polling; `retry: 1`, `refetchOnWindowFocus: false`) |
| HTTP (cloud API) | axios: `api` (30s timeout), `uploadApi` (no timeout) |
| HTTP (processor) | Rust `reqwest` via Tauri command |
| Config persistence | `tauri-plugin-store` → `config.json` |
| Routing | React Router v7 |

## Running and Building

```bash
npm run tauri dev              # Dev (hot-reload Vite + Tauri watch)
npx tsc --noEmit               # Type-check only
npm test                       # Unit tests (Vitest + jsdom)
npm run tauri build            # Production build
~/.cargo/bin/cargo check --manifest-path src-tauri/Cargo.toml  # Rust only
```

VS Code: use the **`Admin Desktop + API`** compound launch (F5 dropdown). Starts Tauri dev mode and .NET API together. The Python processor is started from within the app's Processor screen.

## Detailed Instructions

- [Project Layout](../../6-Docs/agent-instructions/admin-desktop/project-layout.md)
- [Auth — Entra PKCE](../../6-Docs/agent-instructions/admin-desktop/auth.md)
- [Config Persistence](../../6-Docs/agent-instructions/admin-desktop/config.md)
- [Cloud API Client](../../6-Docs/agent-instructions/admin-desktop/cloud-api-client.md)
- [Local Processor](../../6-Docs/agent-instructions/admin-desktop/local-processor.md)
- [Screen → Endpoint Map](../../6-Docs/agent-instructions/admin-desktop/screen-endpoint-map.md)
- [UI / Design Conventions](../../6-Docs/agent-instructions/admin-desktop/ui-conventions.md)
- [Pending Work](../../6-Docs/agent-instructions/admin-desktop/pending-work.md)
