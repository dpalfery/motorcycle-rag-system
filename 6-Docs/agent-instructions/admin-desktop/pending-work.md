# Admin Desktop — Pending Work

| Task | Status | Notes |
|---|---|---|
| Entra PKCE auth | Implemented | PKCE loopback via `oauth2` crate; keyring token persistence; `offline_access` scope; dual refresh strategy (proactive `useAuthExpiry` + reactive 401 interceptor with single-flight); session restore on startup; Chrome profile picker; 5 Rust commands (`auth_sign_in`, `auth_refresh_token`, `auth_restore_session`, `auth_sign_out`, `auth_list_chrome_profiles`); unit tests for token serde and Chrome profile parsing |
| All screens | Implemented | DTOs are best-guess; adjust field names to match actual API responses |
| PyInstaller sidecar | Not started | Replace `python3 -m uvicorn` spawn in `processor_start` with `externalBin` |
| Tests | Not started | Vitest + RTL for view logic; `cargo test` for Rust commands |
| Retire MAUI project | Completed | Removed `MotorcycleRAG.Admin` + `MotorcycleRAG.Admin.Tests` from solution |
