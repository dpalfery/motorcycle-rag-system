# Admin Desktop Auth Enhancements — Persistent Sessions, Refresh, Logout, Chrome Profiles

**Status:** Finalized
**Date:** 2026-07-10
**Goal:** Replace the hand-rolled PKCE-only auth in the Admin Desktop Tauri v2 app with `oauth2` + `keyring`-backed persistent sessions, refresh tokens, 401 interception, logout, Chrome profile selection, and expiry toasts.

---

## 1. Problem / Motivation

The current auth implementation (`src-tauri/src/lib.rs` lines 802–983) is a single `auth_sign_in` command that performs PKCE + loopback + token exchange and returns an access token to the frontend. It has five gaps:

1. **No refresh tokens** — the scope construction (`{scope} openid profile`, line 848) omits `offline_access`, so Entra never returns a `refresh_token`. Tokens expire after ~1 hour with no way to renew silently.
2. **No persistent sessions** — tokens live only in the Zustand store (`src/lib/auth.ts`). Restarting the app always shows SignInScreen.
3. **No logout** — `signOut()` in `auth.ts` clears in-memory state but leaves no server-side or credential-store cleanup. No logout button exists in the UI.
4. **No 401 handling** — `src/lib/apiClient.ts` has a request interceptor (token injection) but no response interceptor. An expired token causes unhandled 401s with no refresh-and-retry.
5. **No browser profile selection** — `tauri_plugin_opener::open_url` launches the system default browser. If the user's Entra session lives in a specific Chrome profile, the OAuth flow opens the wrong browser context.

Research confirmed no Microsoft Rust library for desktop auth exists. The `oauth2` + `keyring` crate combination is the standard approach.

---

## 2. Approved Decisions

| ID | Decision | Chosen Approach |
|----|----------|-----------------|
| D1 | Token storage | `keyring` crate (OS keychain — macOS Keychain / Windows Credential Manager) |
| D2 | Persistent session | Restore from keyring on startup; silently refresh if expired |
| D3 | Refresh strategy | Dual: proactive timer (fires 5 min before expiry) + reactive 401 response interceptor |
| D4 | Browser profile | Chrome only; enumerate profiles from Chrome `Local State` file; dropdown on SignInScreen; persisted via tauri-plugin-store |
| D5 | Logout | Rust command deletes keyring entry + frontend clears Zustand state → SignInScreen |
| D6 | Expiry notifications | `sonner` toast library for session-expiry and refresh-failure notifications |
| D7 | Auth module structure | Extract auth from `lib.rs` monolith into `src-tauri/src/auth/` module (mirrors existing `local_ingestion_queue` module pattern) |
| D8 | Keyring entry key | Service: `"com.palfery.motorcyclerag.admin.desktop"` (matches Tauri identifier); Account: `"session"` (single active session, not multi-account) |

---

## 3. Investigation Findings

### 3.1 Current codebase state (verified against live source)

- **`src-tauri/src/lib.rs`** (1260 lines): Monolith containing processor management + auth. Auth section is lines 802–983 (`AuthResult` struct, `auth_sign_in` command, `extract_id_token_account` helper). No refresh, no persistence, no sign-out.
- **`src/lib/auth.ts`** (47 lines): Zustand store with `accessToken`, `account`, `signedIn`. `signIn()` invokes Rust `auth_sign_in`. `getAccessToken()` wired to axios via `setTokenProvider`. `signOut()` only clears in-memory state.
- **`src/lib/apiClient.ts`** (37 lines): Two axios instances (`api` 30s timeout, `uploadApi` no timeout). Request interceptor injects Bearer token. **No response interceptor.**
- **`src/main.tsx`** (118 lines): Wires `setTokenProvider(getAccessToken)`, loads config, fires health probe. **No session restore.**
- **`src/App.tsx`**: Auth gate — `!signedIn` → SignInScreen; else AppShell + routes.
- **`src/components/AppShell.tsx`**: Sidebar nav. No account display, no logout button.
- **`src/screens/SignInScreen.tsx`** (51 lines): Single "Sign in with Microsoft" button. No profile selection.
- **`src/lib/config.ts`**: `AppConfig` interface persisted via `tauri-plugin-store` → `config.json`. Has `authAuthority`, `authClientId`, `authScope`. No profile/token fields.
- **`Cargo.toml`**: Has `tokio`, `reqwest` (0.12, rustls-tls), `sha2`, `base64`, `getrandom`, `urlencoding`. **Missing:** `oauth2`, `keyring`.
- **`package.json`**: Has `zustand`, `axios`, `react-router-dom`, `lucide-react`. **Missing:** `sonner`.
- **`capabilities/default.json`**: Permissions: `core:default`, `opener:default`, `store:default`. The `shell:default` permission is NOT listed (though `tauri-plugin-shell` is initialized in `lib.rs`). Chrome profile launch will use `std::process::Command` directly (not the shell plugin), so no capability change needed.

### 3.2 Critical finding — `offline_access` scope

Entra v2.0 requires `offline_access` in the requested scopes to return a `refresh_token`. The current code (line 848) constructs `{scope} openid profile` — missing `offline_access`. No app registration change is needed (offline_access is granted by default for public clients); this is purely a runtime scope addition in the Rust auth code.

### 3.3 Chrome profile detection

Chrome stores profile metadata in a `Local State` JSON file:
- **macOS:** `~/Library/Application Support/Google/Chrome/Local State`
- **Windows:** `%LOCALAPPDATA%\Google\Chrome\User Data\Local State`

The `profile.info_cache` object maps directory names (`Default`, `Profile 1`, `Profile 2`, ...) to profile metadata including `name` and `user_name`. The Rust auth module reads this file, parses `profile.info_cache`, and returns `[{ directory, name, userName }]`.

Chrome launch command with profile:
- **macOS:** `open -a "Google Chrome" --args --profile-directory="{dir}" {url}`
- **Windows:** `"C:\Program Files\Google\Chrome\Application\chrome.exe" --profile-directory="{dir}" {url}`

### 3.4 Entra app registration prerequisites (verified from `auth.md`)

- Platform: Mobile and desktop applications
- Redirect URI: `http://localhost` (Entra loopback exception covers any ephemeral port)
- Allow public client flows: Yes
- Tenant ID: `0f8f8a52-f135-43af-af88-e0b54ca9ff91`
- Client ID: `a86e8458-4482-4bb6-808a-28d65b2668ef`
- Scope: `api://motorcyclerag-api/admin`

The `pending-work.md` notes the redirect URI "needs to be added" — this is a prerequisite for end-to-end testing, not a code task.

---

## 4. Task List

| # | Phase | Component | Description | Files / Symbols | Skills |
|---|-------|-----------|-------------|-----------------|--------|
| T1 | 1-Rust | Cargo deps | Add `oauth2` (v5+, reqwest feature) and `keyring` (v3+) to `Cargo.toml`. Verify `cargo check` passes with no duplicate reqwest versions. If version conflict, pin compatible ranges. | `src-tauri/Cargo.toml` | tauri-dev |
| T2 | 1-Rust | Auth module | Create `src-tauri/src/auth.rs` module. Declare `mod auth;` in `lib.rs`. Define `StoredToken` struct (`accessToken`, `refreshToken`, `idToken`, `account`, `expiresAt: u64`, `scope`), `AuthSession` return type (camelCase serde), and keyring constants (`SERVICE_NAME`, `ACCOUNT_NAME`). | `src-tauri/src/auth.rs` (new), `src-tauri/src/lib.rs` (add `mod auth;`) | tauri-dev |
| T3 | 1-Rust | Keyring storage | Implement `store_tokens(&StoredToken)`, `load_tokens() -> Option<StoredToken>`, `delete_tokens()` using `keyring::Entry::new(SERVICE_NAME, ACCOUNT_NAME)`. Serialize/deserialize `StoredToken` as JSON string. | `src-tauri/src/auth.rs` | tauri-dev |
| T4 | 1-Rust | Chrome profiles | Implement `list_chrome_profiles() -> Vec<ChromeProfile>`: resolve `Local State` path per-platform, read JSON, parse `profile.info_cache` into `[{ directory, name, userName }]`. Graceful fallback to `[Default]` if Chrome not installed or parse fails. | `src-tauri/src/auth.rs` | tauri-dev |
| T5 | 1-Rust | Sign-in refactor | Rewrite sign-in flow in `auth.rs` using `oauth2` crate for PKCE generation + authorization URL building + token exchange. Append `offline_access` to scope. Keep custom loopback TCP listener (oauth2 crate doesn't provide one). Launch Chrome with selected profile via `std::process::Command` (replaces `tauri_plugin_opener::open_url`). Store resulting tokens via `store_tokens()`. Return `AuthSession` to frontend. Remove old `auth_sign_in` + `extract_id_token_account` from `lib.rs`. Expose as `auth::sign_in` Tauri command. | `src-tauri/src/auth.rs`, `src-tauri/src/lib.rs` (remove old auth code, re-register command) | tauri-dev |
| T6 | 1-Rust | Refresh command | Implement `auth_refresh_token` Tauri command: read keyring, use `oauth2` RefreshTokenRequest (or manual reqwest POST) with stored `refresh_token`, store refreshed tokens back to keyring, return new `AuthSession`. Use a `Mutex` guard on refresh state to prevent concurrent refresh races (same pattern as `ProcessorState`). | `src-tauri/src/auth.rs`, `src-tauri/src/lib.rs` (register command) | tauri-dev |
| T7 | 1-Rust | Restore command | Implement `auth_restore_session` Tauri command: read keyring → if token not expired, return `AuthSession`; if expired, attempt refresh (reuse T6 logic); if no keyring entry or refresh fails, return `null`. | `src-tauri/src/auth.rs`, `src-tauri/src/lib.rs` (register command) | tauri-dev |
| T8 | 1-Rust | Sign-out command | Implement `auth_sign_out` Tauri command: call `delete_tokens()` to remove keyring entry. | `src-tauri/src/auth.rs`, `src-tauri/src/lib.rs` (register command) | tauri-dev |
| T9 | 1-Rust | List profiles command | Implement `auth_list_chrome_profiles` Tauri command: expose T4's `list_chrome_profiles()` to frontend. Returns `Vec<ChromeProfile>`. | `src-tauri/src/auth.rs`, `src-tauri/src/lib.rs` (register command) | tauri-dev |
| T10 | 2-FE | Dependencies | Add `sonner` to `package.json` dependencies. Run `npm install`. | `package.json` | react-dev |
| T11 | 2-FE | Config | Add `selectedChromeProfile: string` field to `AppConfig` interface and `DEFAULT_CONFIG` (default: `"Default"`). | `src/lib/config.ts` | react-dev |
| T12 | 3-FE | Auth store | Extend Zustand store in `auth.ts`: add `expiresAt: number \| null`. Replace `setToken` with `setSession(token, account, expiresAt)`. Update `signIn()` to accept `chromeProfileDirectory` param and pass to Rust `auth_sign_in`. Update `signOut()` to call Rust `auth_sign_out` before clearing state. Add `restoreSession()` — calls `auth_restore_session`, hydrates store. Add `refreshToken()` — calls `auth_refresh_token`, updates store. | `src/lib/auth.ts` | react-dev |
| T13 | 3-FE | Startup | Update `main.tsx`: after config load, call `restoreSession()` before rendering. If session restored, `signedIn` flips true and AppShell renders directly. | `src/main.tsx` | react-dev |
| T14 | 3-FE | 401 interceptor | Add response interceptor to `apiClient.ts`: on 401, call `auth_refresh_token` via invoke (single-flight mutex — queue concurrent 401s until first refresh completes), retry original request with new token. On refresh failure, call `signOut()` + toast error. | `src/lib/apiClient.ts` | react-dev |
| T15 | 3-FE | Refresh timer | Create `src/lib/useAuthExpiry.ts` hook: reads `expiresAt` from `useAuth`, sets `setTimeout` at `expiresAt - 5min`. On fire, calls `refreshToken()`, reschedules. On failure, toast "Session expired" + `signOut()`. Mount in `AppShell`. | `src/lib/useAuthExpiry.ts` (new), `src/components/AppShell.tsx` (mount hook) | react-dev |
| T16 | 4-FE | SignInScreen | Add Chrome profile dropdown to `SignInScreen.tsx`: fetch profiles via `auth_list_chrome_profiles` on mount, render `<select>`, persist selection to config (`selectedChromeProfile`), pass to `signIn()`. Show error if no profiles detected. | `src/screens/SignInScreen.tsx` | react-dev |
| T17 | 4-FE | AppShell | Add account display (email/name from `useAuth`) and logout button to `AppShell.tsx` sidebar footer. Logout calls `signOut()` which triggers Rust `auth_sign_out` + clears Zustand + App.tsx renders SignInScreen. | `src/components/AppShell.tsx` | react-dev |
| T18 | 4-FE | Toaster | Mount `<Toaster />` from sonner in `main.tsx` (inside providers, after `<App />`). Wire toast calls in `useAuthExpiry.ts` (expiry warning) and `apiClient.ts` 401 interceptor (refresh failure). | `src/main.tsx`, `src/lib/useAuthExpiry.ts`, `src/lib/apiClient.ts` | react-dev |
| T19 | 5-Test | Rust tests | Add `#[cfg(test)] mod tests` to `auth.rs`: test `StoredToken` serde round-trip, Chrome `Local State` parsing (mock JSON), scope construction includes `offline_access`, PKCE verifier/challenge non-empty, profile fallback when Chrome absent. | `src-tauri/src/auth.rs` | tauri-dev |
| T20 | 5-Test | Frontend tests | Create `src/lib/auth.test.ts`: test `setSession`/`signOut` state transitions, `restoreSession` hydration from mock invoke, `refreshToken` store update. Create/update `apiClient` interceptor test: single-flight 401 refresh, retry with new token, sign-out on refresh failure. | `src/lib/auth.test.ts` (new), `src/lib/apiClient.test.ts` (new) | test-dev |
| T21 | 6-Docs | Auth docs | Rewrite `authentication.md`: document keyring storage model, scope with `offline_access`, refresh strategy (proactive + reactive), session restore, Chrome profile selection, sign-out flow, new Rust commands. | `6-Docs/MotorcycleRAG.AdminDesktop/authentication.md` | docs-dev |
| T22 | 6-Docs | Pending work | Update `admin-desktop-pending-work.md`: mark Entra PKCE auth row as complete with all enhancements. | `6-Docs/plans/admin-desktop-pending-work.md` | docs-dev |

---

## 5. Sequencing / Dependency Graph

```
T1 (Cargo deps)
 ├─ T2 (auth module)
 │   ├─ T3 (keyring storage) ──┬── T5 (sign-in refactor) ── T6 (refresh) ── T7 (restore)
 │   │                         ├── T8 (sign-out)
 │   │                         └── T4 (chrome profiles) ── T9 (list cmd)
 │   │                                                        │
 │   └─ T19 (rust tests) ← T5, T8, T9                         │
 │                                                             │
 T10 (sonner)     T11 (config field)                           │
  │                │                                           │
  │                ├─ T16 (SignInScreen) ← T12 (auth store) ───┘ (Rust commands)
  │                │                       ↑
  │                │             T13 (main.tsx) ─────── T12
  │                │             T14 (401 interceptor) ─ T12
  │                │             T15 (refresh timer) ─── T12
  │                │                       │
  │                ├─ T17 (AppShell logout) ← T12
  │                └─ T18 (Toaster) ← T10, T15
  │
  └─ T20 (FE tests) ← T12, T14, T15
     T21, T22 (docs) ← all complete
```

**Critical path:** T1 → T2 → T3 → T5 → T6 → T7 → T12 → T14 (longest chain).

**Parallelizable tracks:**
- Track A (Rust): T1 → T2 → {T3, T4 parallel} → T5 → {T6, T7, T8, T9} → T19
- Track B (FE foundation): T10, T11 (parallel, no deps on Rust)
- Track C (FE logic/UI): blocked on T12 which needs T6+T7+T8 Rust commands → T13, T14, T15, T16, T17, T18 → T20
- Track D (Docs): T21, T22 after all implementation

---

## 6. Residual Decisions / Risks

| ID | Risk / Decision | Impact | Mitigation / Owner |
|----|----------------|--------|--------------------|
| R1 | `oauth2` crate v5 may pull in a duplicate `reqwest` version conflicting with the existing `reqwest = "0.12"` with `rustls-tls`. | Build failure or binary bloat. | T1 verifies `cargo check` + `cargo tree -d`. If conflict, use `oauth2` only for PKCE/URL types and keep manual `reqwest` token exchange. |
| R2 | `keyring` crate requires OS keychain service. CI/headless Linux may not have one. | Tests fail in CI. | Use `keyring` mock feature for unit tests (`#[cfg(test)]`). Rust tests (T19) test parsing/storage serialization, not real keychain I/O. |
| R3 | Chrome `Local State` JSON structure is undocumented and may change across Chrome versions. | Profile dropdown empty or malformed. | Graceful fallback: if `profile.info_cache` missing or unparseable, return `[Default]`. T4 + T19 cover this. |
| R4 | Concurrent refresh race — proactive timer fires simultaneously with 401 interceptor refresh. | Double keyring write, token overwrite. | Rust `auth_refresh_token` uses `Mutex` (T6) — single-flight at the Rust level. Frontend 401 interceptor also has single-flight (T14). Belt and suspenders. |
| R5 | Entra app registration redirect URI `http://localhost` may not yet be configured (per `pending-work.md`). | End-to-end sign-in fails at redirect. | Prerequisite check before E2E testing. Not a code task — user must verify in Entra portal. |
| R6 | `oauth2` crate async API bridge with reqwest 0.12 — the crate's HTTP client abstraction may need a custom adapter. | Compilation errors in T5. | If `oauth2` crate's built-in reqwest integration doesn't work with 0.12, implement the `AsyncHttpClient` trait manually (thin wrapper over `reqwest::Client`). |

---

## 7. Out of Scope

- **Multi-account support** — D8 specifies single active session (`"session"` key in keyring). Multi-account (multiple keyring entries) would require keyring enumeration and account-switcher UI. Deferred.
- **Token revocation endpoint** — Entra supports `POST /oauth2/v2.0/logout` for server-side session cleanup, but D5 specifies keyring-delete + Zustand-clear only. Server-side logout deferred.
- **Non-Chrome browsers** — D4 specifies Chrome only. Edge/Firefox/Safari support deferred.
- **MAUI app parity** — MAUI admin app is retired. No mobile auth changes.
- **Silent auth (Windows broker / WAM)** — Windows Account Manager (WAM) provides silent token acquisition without browser. Would require platform-specific code. Deferred; the refresh-token approach covers the core need.
- **CI/CD pipeline changes** — No new test CI steps beyond existing `cargo test` and `npm test`.

---

## 8. Required Skills

| Skill | Used By Tasks | Purpose |
|-------|--------------|---------|
| `tauri-dev` | T1–T9, T19 | Rust/Tauri backend: Cargo deps, auth module, oauth2/keyring integration, Tauri commands, Rust unit tests |
| `react-dev` | T10–T18 | TypeScript/React frontend: Zustand store, axios interceptors, hooks, UI components, sonner integration |
| `test-dev` | T20 | Frontend test suite: auth store state transitions, 401 interceptor single-flight, session restore |
| `docs-dev` | T21–T22 | Update auth.md and pending-work.md documentation |

---

## 9. Verification Harness

### Build gates (must pass before code review)
- `~/.cargo/bin/cargo check --manifest-path src-tauri/Cargo.toml` — Rust compiles
- `~/.cargo/bin/cargo test --manifest-path src-tauri/Cargo.toml` — Rust unit tests pass (T19)
- `~/.cargo/bin/cargo tree -d --manifest-path src-tauri/Cargo.toml` — No duplicate reqwest versions
- `npx tsc --noEmit` — TypeScript type-checks clean
- `npm test` — Vitest unit tests pass (T20)

### Code review
- `code-reviewer` agent reviews all Rust + TypeScript changes before merge
- Focus areas: keyring credential handling (no token logging), PKCE/refresh correctness, 401 interceptor single-flight logic, Chrome launch command injection safety (profile directory is user-selected, must be sanitized)

### Manual E2E verification (after code review)
1. **Sign-in flow**: Launch app → select Chrome profile → click "Sign in with Microsoft" → Chrome opens with selected profile → complete Entra consent → app shows AppShell with account name
2. **Session restore**: After sign-in, quit app → relaunch → app boots directly to AppShell (no SignInScreen) with same account
3. **Proactive refresh**: Sign in → wait until 5 min before expiry → observe silent refresh (no UI disruption, token updates)
4. **Reactive 401 refresh**: With expired token, make an API call → interceptor catches 401 → refreshes → retries → succeeds
5. **Refresh failure**: Simulate expired refresh token (delete keyring entry mid-session) → trigger API call → observe toast notification + redirect to SignInScreen
6. **Logout**: Click logout button → keyring entry deleted → app returns to SignInScreen → relaunch app → boots to SignInScreen (not restored)
7. **Chrome profile fallback**: Rename Chrome `Local State` file → launch app → dropdown shows `Default` only (graceful fallback)

### Azure read-only validation
- `azure-reader` agent can verify Entra app registration configuration (redirect URI, public client setting) if the user needs confirmation before E2E testing
