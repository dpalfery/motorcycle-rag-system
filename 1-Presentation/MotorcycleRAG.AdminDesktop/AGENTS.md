# MotorcycleRAG.AdminDesktop — Agent Context

Tauri v2 desktop admin console (macOS + Windows 11) that replaces the retired MAUI admin app.
It has two backends: the cloud .NET API (`MotorcycleRAG.API`, port 7215) and the local Python
processor (`2-Application/local-processing-service`, default port 8100).

---

## Stack

| Layer | Choice | Why |
|---|---|---|
| Shell | Tauri v2 | Native desktop binary; bundles a Rust core + Chromium webview |
| Frontend | React 19 + TypeScript + Vite | Same toolchain as `MotorcycleRag.WebUI` |
| Styling | Tailwind CSS v3 | Dark surface `#1a1a1a` / orange accent `#ff6600` — see `tailwind.config.js` |
| State | Zustand | Auth store (`useAuth`) + config store (`useConfig`) |
| Data fetching | TanStack Query v5 | 15s polling on live screens; `retry: 1`, `refetchOnWindowFocus: false` |
| HTTP (cloud API) | axios | Two instances: `api` (30s timeout) and `uploadApi` (no timeout) — see below |
| HTTP (processor) | Rust `reqwest` via Tauri command | Avoids CORS / mixed-content from the webview to `http://127.0.0.1` |
| Config persistence | `tauri-plugin-store` → `config.json` | Replaces MAUI `Preferences` |
| Routing | React Router v7 | `BrowserRouter`; all routes inside `AppShell` layout |

---

## Project layout

```
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

---

## Auth — Entra auth-code + PKCE loopback

**Decision:** no MSAL library. The entire flow lives in one Rust command (`auth_sign_in`).

Flow (`src-tauri/src/lib.rs → auth_sign_in`):
1. Generate 32-byte PKCE `code_verifier` (base64url via `getrandom` + `base64`).
2. `code_challenge` = SHA-256(`code_verifier`) base64url (`sha2` crate).
3. Bind `tokio::net::TcpListener` on `127.0.0.1:0` → random ephemeral port.
4. Open `{authority}/oauth2/v2.0/authorize?...&redirect_uri=http://localhost:{port}&...` in the
   system browser via `tauri_plugin_opener`.
5. Accept one TCP connection, read the GET request, write a "Sign-in complete" HTML response,
   extract `code` + `state` from the query string.
6. Validate state (CSRF check), POST to `{authority}/oauth2/v2.0/token` with `reqwest`.
7. Decode `id_token` JWT payload (base64url, no signature verification) to extract
   `preferred_username` / `email` / `name`.
8. Return `{ accessToken, account, expiresIn }` to TypeScript.
9. TypeScript (`auth.ts → signIn()`) calls `useAuth.getState().setToken(...)` — this flips
   `signedIn: true` and `App.tsx` immediately renders the full shell.

**5-minute timeout** via `tokio::time::timeout`. State mismatch → hard error.

**Entra app registration requirements:**
- Platform: Mobile and desktop applications
- Redirect URI: `http://localhost` (Entra's loopback exception covers any ephemeral port)
- Allow public client flows: Yes
- Tenant ID: `0f8f8a52-f135-43af-af88-e0b54ca9ff91`
- Client ID: `a86e8458-4482-4bb6-808a-28d65b2668ef`
- Scope: `api://motorcyclerag-api/admin` (default in `DEFAULT_CONFIG.authScope`)

**Sign-out** is purely client-side: `useAuth.getState().signOut()` clears the token;
`App.tsx` re-renders the sign-in screen. No server-side logout endpoint is called.

---

## Config persistence

`AppConfig` fields (all in `src/lib/config.ts → DEFAULT_CONFIG`):

| Field | Default |
|---|---|
| `apiBaseUrl` | `https://localhost:7215` |
| `authAuthority` | `https://login.microsoftonline.com/0f8f8a52-...` |
| `authClientId` | `a86e8458-...` |
| `authScope` | `api://motorcyclerag-api/admin` |
| `embeddingProviderEndpoint` | `http://localhost:11434` |
| `embeddingModel` | `qwen3-embedding` |
| `localProcessorPort` | `8100` |
| `localProcessorWorkingDir` | `""` (must be set to the local-processing-service root) |

Stored in `tauri-plugin-store` → `config.json` (OS app-data dir). Loaded at startup in
`main.tsx`; `apiClient.ts` subscribes to changes and updates its base URL live.

---

## Cloud API client (`src/lib/apiClient.ts`)

Two axios instances sharing the same interceptor (base URL from `useConfig` + Bearer token
from `useAuth`):

- **`api`** — `timeout: 30_000`. Use for all standard API calls.
- **`uploadApi`** — `timeout: 0` (unlimited). Use for file uploads **only**.

**Important:** never set `Content-Type: multipart/form-data` manually on a FormData upload.
Axios sets it with the correct `boundary` automatically. Setting it manually strips the
boundary and the server cannot parse the body.

The base URL and token provider are wired in `main.tsx`:
```ts
setTokenProvider(getAccessToken);
useConfig.getState().load().then(() => setApiBaseUrl(...));
useConfig.subscribe((s) => setApiBaseUrl(s.config.apiBaseUrl));
```

---

## Local processor (`src/lib/processor.ts` + `src-tauri/src/lib.rs`)

All communication with the Python processor goes through Rust commands, not browser `fetch`.
The Tauri webview cannot call `http://127.0.0.1` directly (CORS + mixed-content).

### Rust commands

| Command | What it does |
|---|---|
| `processor_start(config)` | Spawns `python3 -m uvicorn main:app --host 127.0.0.1 --port {port}` in `{workingDir}/src` with env vars |
| `processor_stop()` | POST `/control/shutdown` then kills the child process |
| `processor_running()` | Returns whether a child handle exists |
| `processor_request(method, path, body, port)` | HTTP proxy: GET/POST/PUT/DELETE to `127.0.0.1:{port}{path}` |

### Env vars passed to the processor on start

`PORT`, `PYTHONUNBUFFERED=1`, `EMBEDDING_PROVIDER_ENDPOINT`, `EMBEDDING_MODEL`,
`MCR_API_BASE_URL`, `PYTHON_UPLOAD_JOB_SECRET` (if provided).

### Dev mode vs production

Currently spawns `python3 -m uvicorn` from source (dev mode). The plan is to replace this
with a PyInstaller sidecar registered as Tauri `externalBin` — same Rust command surface,
different program invocation. Do not change the TypeScript `processor.*` API surface when
making this switch.

---

## Screen → endpoint map

| Screen | Client | Key endpoints |
|---|---|---|
| ProcessorScreen | Rust commands | `GET /health`, `GET /jobs`, `POST /jobs/cleanup`, `POST /control/shutdown` |
| IngestionScreen | `uploadApi` + `api` | `POST /api/file-upload/with-processing`, `GET /api/datapipeline/upload-constraints`, `GET /api/ingestion/jobs/upload` |
| JobsScreen | `api` | `GET /api/ingestion/jobs`, `PUT /api/ingestion/jobs/{id}/retry`, `DELETE /api/ingestion/jobs/{id}` |
| WebSourcesScreen | `api` | `GET/POST /api/admin/web-sources`, `PUT/DELETE /api/admin/web-sources/{id}` |
| McpToolsScreen | `api` | `GET /api/admin/mcp-tools`, `PUT /api/admin/mcp-tools/{id}` |
| UsersScreen | `api` | `GET /api/admin/users` |
| SettingsScreen | `useConfig` store | No API calls — reads/writes `tauri-plugin-store` |

---

## UI / design conventions

- **Theme tokens** — defined in `tailwind.config.js`: `background` `#1a1a1a`, `card` `#222222`,
  `border` `#333333`, `primary` / `accent` `#ff6600`, `muted` `#999`, `secondary` `#2a2a2a`,
  `success` `#28c840`, `danger` `#e24b4a`, `warning` `#ef9f27`.
- **Layout** — `AppShell` has a 180px fixed sidebar with `NavLink` active state (left orange
  border + `bg-secondary/60`). Content scrolls in `<main>`.
- **Titlebar** — a 36px drag region at the top (`data-tauri-drag-region` equivalent, class
  `drag`). Do not put interactive elements in this strip.
- **Shared primitives** — import from `@/components/ui`: `Button` (variants: default/primary/danger),
  `Card`, `MetricCard`, `StatusPill`, `PageHeader` (supports `actions` slot), `Empty`.
- **Icons** — lucide-react only.
- **No dashboard screen** — it was removed; the sidebar is the navigation.

---

## Running and building

```bash
# Dev (hot-reload Vite + Tauri watch)
npm run tauri dev

# Type-check only
npx tsc --noEmit

# Unit tests (Vitest + jsdom)
npm test

# Production build
npm run tauri build
```

VS Code: use the **`Admin Desktop + API`** compound launch (F5 dropdown). It starts Tauri dev
mode and the .NET API together. The Python processor is started from within the app's
Processor screen — it is not part of the launch compound.

Cargo check (Rust only, no Vite):
```bash
~/.cargo/bin/cargo check --manifest-path src-tauri/Cargo.toml
```

---

## Pending work

| Task | Status | Notes |
|---|---|---|
| Entra PKCE auth | Implemented; untested end-to-end | Needs Entra app reg redirect URI `http://localhost` added |
| All screens | Implemented | DTOs are best-guess; adjust field names to match actual API responses |
| PyInstaller sidecar | Not started | Replace `python3 -m uvicorn` spawn in `processor_start` with `externalBin` |
| Tests | Not started | Vitest + RTL for view logic; `cargo test` for Rust commands |
| Retire MAUI project | Not started | Remove `MotorcycleRAG.Admin` + `MotorcycleRAG.Admin.Tests` from solution |
