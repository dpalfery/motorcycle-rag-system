---
id: admin-desktop/project-layout
title: Admin Desktop — Project Layout
doc-type: reference
status: current
component: MotorcycleRAG Admin Desktop
owner: Admin Desktop maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# Admin Desktop — Project Layout

```text
src/
  main.tsx              Entry — wires QueryClient, BrowserRouter, config load, apiClient
  App.tsx               Auth gate: shows SignInScreen when !signedIn, AppShell+routes when signed in
  components/
    AppShell.tsx        Sidebar nav + drag titlebar + <Outlet>
    ui.tsx              Primitives: Card, MetricCard, Button, StatusPill, PageHeader, Empty
  lib/
    auth.ts             Zustand auth store + signIn() (Entra PKCE) + getAccessToken()
    config.ts           Zustand config store + AppConfig interface + DEFAULT_CONFIG
    apiClient.ts        axios instances (api, uploadApi) + setTokenProvider/setApiBaseUrl
    processor.ts        TypeScript bridge to Rust processor_* commands
    utils.ts            cn(), isValidUrl(url, allowLocalhost?), trimTrailingSlash()
  screens/
    SignInScreen.tsx     Sign-in wall — shown when !useAuth().signedIn
    ProcessorScreen.tsx  Local processor control (start/stop/health/jobs)
    IngestionScreen.tsx  File upload + recent upload jobs
    JobsScreen.tsx       Cloud ingestion jobs table (retry/delete)
    WebSourcesScreen.tsx Web source CRUD
    McpToolsScreen.tsx   MCP tool enable/disable toggles (optimistic)
    UsersScreen.tsx      User list from api/admin/users
    SettingsScreen.tsx   All AppConfig fields; persisted via tauri-plugin-store
src-tauri/
  Cargo.toml            Dependencies: tauri, tauri-plugin-shell, tauri-plugin-store,
                        tauri-plugin-opener, reqwest, sha2, base64, getrandom,
                        urlencoding, tokio (net/time/io-util)
  src/lib.rs            All Rust commands (see below)
  tauri.conf.json       productName, identifier, window size (1180×760, min 920×600)
  capabilities/
    default.json        Permissions: core:default, opener:default, store:default
```
