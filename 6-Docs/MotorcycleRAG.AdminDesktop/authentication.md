# Admin Desktop — Auth (Entra PKCE Loopback)

**Decision:** no MSAL library. The entire OAuth2 flow is implemented in Rust using the `oauth2` crate (v5), with token persistence via the OS keyring (`keyring` crate v3). The frontend calls Tauri commands and manages session state via Zustand.

## Architecture Overview

```mermaid
sequenceDiagram
    participant TS as TypeScript (Zustand)
    participant Rust as Rust (Tauri Commands)
    participant Keyring as OS Keychain
    participant Entra as Microsoft Entra ID
    participant API as Cloud .NET API

    Note over TS,Rust: SIGN-IN
    TS->>Rust: invoke("auth_sign_in", { profileDirectory })
    Rust->>Rust: Generate PKCE verifier/challenge
    Rust->>Rust: Bind 127.0.0.1:0 (ephemeral port)
    Rust->>Browser: Open Entra authorize URL
    Browser->>Rust: Redirect with auth code
    Rust->>Entra: POST /token (code + PKCE verifier)
    Entra-->>Rust: { access_token, refresh_token, id_token }
    Rust->>Keyring: store_tokens()
    Rust-->>TS: { accessToken, account, expiresAt }
    TS->>TS: useAuth.getState().setSession()

    Note over TS,Rust: SESSION RESTORE (startup)
    TS->>Rust: invoke("auth_restore_session")
    Rust->>Keyring: load_tokens()
    alt Token valid
        Rust-->>TS: { accessToken, account, expiresAt }
    else Token expired + refresh token exists
        Rust->>Entra: POST /token (refresh_token)
        Rust->>Keyring: store_tokens()
        Rust-->>TS: { accessToken, account, expiresAt }
    else No valid session
        Rust-->>TS: null
    end

    Note over TS,Rust: PROACTIVE REFRESH (5 min before expiry)
    TS->>Rust: invoke("auth_refresh_token")
    Rust->>Keyring: load_tokens()
    Rust->>Entra: POST /token (refresh_token)
    Rust->>Keyring: store_tokens()
    Rust-->>TS: { accessToken, account, expiresAt }

    Note over TS,Rust: REACTIVE REFRESH (on 401)
    API-->>TS: 401 Unauthorized
    TS->>Rust: invoke("auth_refresh_token")
    Rust->>Entra: POST /token (refresh_token)
    Rust->>Keyring: store_tokens()
    Rust-->>TS: { accessToken, account, expiresAt }
    TS->>API: Retry with new Bearer token

    Note over TS,Rust: SIGN-OUT
    TS->>Rust: invoke("auth_sign_out")
    Rust->>Keyring: delete_tokens()
    TS->>TS: Clear Zustand state → signedIn = false
```

## Rust Commands

All auth commands are registered in `lib.rs` via `tauri::generate_handler![]` and implemented in `auth.rs`.

### `auth_sign_in`

| Aspect | Detail |
| --- | --- |
| **File** | `auth.rs` → `sign_in()` |
| **Signature** | `async fn sign_in(config: &AuthConfig, profile_directory: Option<String>) -> Result<AuthSession, String>` |
| **Tauri command** | `async fn auth_sign_in(app: tauri::AppHandle, profile_directory: Option<String>)` — reads `authority`, `clientId`, `scope` from the Tauri config store internally via `read_auth_config()` |
| **Purpose** | Full Entra auth-code + PKCE loopback flow |

**Flow:**

1. Generate PKCE `code_verifier` and `code_challenge` via `oauth2::PkceCodeChallenge::new_random_sha256()`.
2. Bind `tokio::net::TcpListener` on `127.0.0.1:0` (random ephemeral port).
3. Build the Entra authorize URL with scopes: `{scope}`, `openid`, `profile`, `offline_access`.
4. Open the authorize URL. If `profile_directory` is `Some`, launch **Google Chrome stable** with `--profile-directory` (see [Chrome launch by platform](#chrome-launch-by-platform)). The directory value is validated as a Chrome profile folder name before spawn. If `None`, open the system default browser via `open` (macOS), `cmd /c start` (Windows), or `xdg-open` (Linux).
5. Accept one TCP connection on the loopback listener, read the GET request, write a "Sign-in complete" HTML response, extract `code` + `state` from the query string.
6. Validate CSRF state (mismatch → hard error).
7. Exchange the authorization code for tokens via `POST {authority}/oauth2/v2.0/token` using the `oauth2` crate's `exchange_code()` with PKCE verifier.
8. Decode `id_token` JWT payload (base64url, no signature verification) to extract `preferred_username` → `email` → `name` as the account identifier.
9. Persist `StoredToken` to the OS keyring via `store_tokens()`.
10. Return `AuthSession { accessToken, account, expiresAt }` to TypeScript.

**5-minute timeout** via `tokio::time::timeout`. State mismatch → hard error `"state mismatch — possible CSRF attack"`.

### `auth_refresh_token`

| Aspect | Detail |
| --- | --- |
| **File** | `auth.rs` → `refresh()` |
| **Signature** | `async fn refresh(config: &AuthConfig) -> Result<AuthSession, String>` |
| **Tauri command** | `async fn auth_refresh_token(app: tauri::AppHandle)` |
| **Config source** | Reads `authAuthority`, `authClientId`, `authScope` from the Tauri config store (`config.json`) via `read_auth_config()` |

**Flow:**

1. Load stored tokens from the OS keyring via `load_tokens()`. Fail if no stored session or no refresh token.
2. Build an OAuth2 client (no redirect URI needed for refresh).
3. Exchange the refresh token via `POST {authority}/oauth2/v2.0/token` using `oauth2::Client::exchange_refresh_token()`.
4. Persist the new tokens (keeping the old refresh token if Entra didn't return a new one).
5. Return the updated `AuthSession`.

### `auth_restore_session`

| Aspect | Detail |
| --- | --- |
| **File** | `auth.rs` → `restore()` |
| **Signature** | `async fn restore(config: &AuthConfig) -> Result<Option<AuthSession>, String>` |
| **Tauri command** | `async fn auth_restore_session(app: tauri::AppHandle)` |

**Flow:**

1. Load stored tokens from the OS keyring via `load_tokens()`. If none exist, return `Ok(None)`.
2. If the access token is still valid (with a 60-second skew buffer), return the session immediately.
3. If a refresh token is available, try to exchange it:
   - On success → persist new tokens and return the new session.
   - On failure → delete stored tokens and return `None`.
4. Otherwise (no refresh token, token expired) → delete stored tokens and return `None`.

### `auth_sign_out`

| Aspect | Detail |
| --- | --- |
| **File** | `auth.rs` → `sign_out()` |
| **Signature** | `fn sign_out() -> Result<(), String>` |
| **Tauri command** | `fn auth_sign_out()` |

Deletes the persisted session tokens from the OS keyring via `delete_tokens()`. Returns `Ok(())` even if no entry existed. The TypeScript side then clears the Zustand store (`accessToken: null`, `signedIn: false`), which causes `App.tsx` to re-render the `SignInScreen`.

### `auth_list_chrome_profiles`

| Aspect | Detail |
| --- | --- |
| **File** | `auth.rs` → `list_chrome_profiles()` / `discover_chrome_profiles_at()` / `parse_chrome_profiles_from_local_state()` |
| **Signature** | `fn list_chrome_profiles() -> ChromeProfilesResult` |
| **Tauri command** | `fn auth_list_chrome_profiles() -> ChromeProfilesResult` |
| **Return shape** | `{ profiles: ChromeProfile[], error: string \| null }` (camelCase over IPC) |

**Scope:** Google Chrome **stable** only (`Google/Chrome` / `google-chrome` Local State paths). Chrome Beta, Canary, Chromium, Edge, and Arc are out of scope for this picker.

**Flow:**

1. Resolve the Chrome stable `Local State` file path (platform-specific):
   - macOS: `~/Library/Application Support/Google/Chrome/Local State`
   - Windows: `%LOCALAPPDATA%\Google\Chrome\User Data\Local State`
   - Linux: `~/.config/google-chrome/Local State`
2. Read the file and parse the JSON `profile.info_cache` object into each profile's `directory`, `name`, and `user_name` (pure helper `parse_chrome_profiles_from_local_state`).
3. Sort: `"Default"` first, then alphabetical by directory.
4. On success: return `{ profiles: [...], error: null }`.
5. On failure (unsupported platform path, I/O error, invalid JSON, or missing/non-object `info_cache`): return `{ profiles: [], error: "<diagnostic>" }`. **Never** invent a silent sole fake `Default` profile that looks like successful discovery.

## Keyring Storage

Tokens are persisted to the OS keychain using the `keyring` crate (v3).

| Constant | Value |
| --- | --- |
| `KEYRING_SERVICE` | `com.palfrey.motorcyclerag.admin.desktop` |
| `KEYRING_ACCOUNT` | `session` |

### `StoredToken` (serialized to JSON in keyring)

```rust
struct StoredToken {
    access_token: String,
    refresh_token: Option<String>,
    id_token: Option<String>,
    account: String,       // preferred_username | email | name from id_token
    expires_at: u64,       // Unix timestamp (seconds)
    scope: String,
}
```

### Functions

| Function | Purpose |
| --- | --- |
| `store_tokens(token: &StoredToken)` | Serializes to JSON and writes to the OS keychain via `keyring::Entry::set_password()`. |
| `load_tokens() -> Option<StoredToken>` | Reads and deserializes from the OS keychain. Returns `None` if no entry exists or deserialization fails. |
| `delete_tokens()` | Deletes the credential from the OS keychain. Returns `Ok(())` even if no entry existed. |

## Scopes

The authorize URL includes four scopes:

| Scope | Purpose |
| --- | --- |
| `{config.scope}` | The API scope (default: `api://motorcyclerag-api/admin`) — grants access to the cloud .NET API |
| `openid` | Required for OIDC — returns the `id_token` |
| `profile` | Requests the `preferred_username` claim in the `id_token` |
| `offline_access` | Requests a `refresh_token` — enables silent token refresh without user interaction |

The `offline_access` scope is critical: without it, Entra does not return a `refresh_token`, and the refresh strategy cannot function.

## Refresh Strategy

The system uses a **dual refresh strategy**: proactive (timer-based) and reactive (401 interceptor).

### Proactive Refresh (`useAuthExpiry`)

A React hook (`useAuthExpiry.ts`) sets a timer to refresh the access token **5 minutes before it expires**.

```typescript
const REFRESH_BUFFER_MS = 5 * 60 * 1000; // 5 minutes
```

- When `expiresAt` changes (after sign-in or refresh), a `setTimeout` is scheduled for `expiresAt - 5min - now`.
- If the token is already within 5 minutes of expiry (or expired), refresh fires immediately.
- On refresh failure, the user is signed out with a toast notification.
- The timer is cleared and re-armed whenever `expiresAt` or `signedIn` changes.
- Uses a `mountedRef` to avoid state updates after unmount.

### Reactive Refresh (401 Interceptor)

The axios response interceptor in `apiClient.ts` handles 401 responses:

1. On receiving a 401, the interceptor sets `_retry = true` on the original request.
2. Calls `refreshAccessToken()` which uses a **single-flight pattern** (`refreshPromise`) to prevent concurrent refresh calls when multiple requests receive 401 simultaneously.
3. On success, retries the original request with the new Bearer token.
4. On failure, signs the user out and shows a toast: "Your session has expired. Please sign in again."

The single-flight pattern is critical: `refreshPromise` is a module-level variable that all concurrent 401 callers share. Only the first caller triggers the actual `invoke("auth_refresh_token")`; subsequent callers await the same promise.

## Session Restore (Startup)

In `main.tsx`, after the config store loads:

```typescript
void useAuth.getState().restoreSession()
    .catch((err) => console.warn("Session restore failed:", err));
```

This is fire-and-forget — the UI never blocks on it. If a valid session is restored, `useAuth.getState().setSession()` flips `signedIn: true` and `App.tsx` immediately renders the full `AppShell` instead of `SignInScreen`.

## Chrome Profile Selection

The `SignInScreen` browser picker (`listChromeProfiles()` → `auth_list_chrome_profiles`) works as follows:

1. On mount, loads `{ profiles, error }` from Chrome stable's `Local State` (see [`auth_list_chrome_profiles`](#auth_list_chrome_profiles)).
2. Always offers **System default browser (recommended)** (`SYSTEM_DEFAULT_BROWSER` = `""` in `auth.ts`). Choosing it passes `profileDirectory: null` to `auth_sign_in`, which opens the OS default browser (not a Chrome profile).
3. When enumeration succeeds, appends each real Chrome profile (`name` and optional `userName`) to the `<select>`.
4. When enumeration fails or returns no profiles, shows a warning with the diagnostic (or `"Could not read Chrome profiles"`) and defaults the selection to system default browser. Sign-in remains available via that escape hatch; Full Disk Access is not a shipping prerequisite.
5. Persists the selection in the Tauri config store as `selectedChromeProfile` (empty string for system default; otherwise a Chrome profile directory such as `Default` or `Profile 1`).
6. Sign-in invoke failures (including Rust launch/timeout/CSRF strings) are shown in the error banner via `formatInvokeError` (string errors are not collapsed to a generic `"Sign-in failed."`).

### Chrome launch by platform

When `profileDirectory` is non-null, Rust validates it with `is_valid_chrome_profile_directory` (non-empty, ≤128 chars, no path separators or `..`, ASCII alphanumeric plus space / `.` / `_` / `-` / `'`) before spawning. Invalid values return an actionable error such as `invalid Chrome profile directory '…': expected a Chrome profile folder name`.

| Platform | Launch behavior |
| --- | --- |
| **macOS** | Prefer the Chrome stable binary at `/Applications/Google Chrome.app/Contents/MacOS/Google Chrome` with argv `--profile-directory={dir}` and the authorize URL. If that binary is missing, fall back to `open -na "Google Chrome" --args --profile-directory={dir} {url}` (`-n` / `-na` required so args apply when Chrome is already running). Do **not** use `open -a` without `-n`. Spawn failures map to `failed to open Chrome on macOS: …`. |
| **Windows** | Try `C:\Program Files\Google\Chrome\Application\chrome.exe` then the `(x86)` path with `--profile-directory` + URL. |
| **Linux** | `google-chrome --profile-directory={dir} {url}`. |

## Sign-out Flow

1. User clicks sign-out in `AppShell.tsx`.
2. TypeScript calls `useAuth.getState().signOut()`.
3. The Zustand action calls `invoke("auth_sign_out")` → Rust deletes the keyring entry.
4. Zustand state is reset: `accessToken: null`, `account: null`, `signedIn: false`, `expiresAt: null`.
5. `App.tsx` re-renders the `SignInScreen` (no server-side logout endpoint is called).

## Entra App Registration Requirements

| Setting | Value |
| --- | --- |
| Platform | Mobile and desktop applications |
| Redirect URI | `http://localhost` (Entra's loopback exception covers any ephemeral port) |
| Allow public client flows | Yes |
| Tenant ID | `0f8f8a52-f135-43af-af88-e0b54ca9ff91` |
| Client ID | `a86e8458-4482-4bb6-808a-28d65b2668ef` |
| Scope | `api://motorcyclerag-api/admin` (default in config store `authScope`) |

## Config Store Integration

Auth configuration is read from the Tauri config store (`config.json`) by `read_auth_config()` in `lib.rs`:

| Config Key | Default Value |
| --- | --- |
| `authAuthority` | `https://login.microsoftonline.com/0f8f8a52-f135-43af-af88-e0b54ca9ff91` |
| `authClientId` | `a86e8458-4482-4bb6-808a-28d65b2668ef` |
| `authScope` | `api://motorcyclerag-api/admin` |

All three commands (`auth_sign_in`, `auth_refresh_token`, `auth_restore_session`) read the config internally via `read_auth_config()` — the TypeScript side never passes authority, client ID, or scope explicitly.

## TypeScript Types

```typescript
interface AuthSession {
  accessToken: string;
  account: string;
  expiresAt: number;  // Unix timestamp (seconds)
}

interface ChromeProfile {
  directory: string;
  name: string;
  userName?: string;
}

/** Result of auth_list_chrome_profiles — never a silent fake sole Default. */
interface ChromeProfilesResult {
  profiles: ChromeProfile[];
  error: string | null;
}

/** Select value / persisted marker for system-default-browser sign-in. */
const SYSTEM_DEFAULT_BROWSER = "";
```

## Zustand Store (`useAuth`)

| State | Type | Description |
| --- | --- | --- |
| `accessToken` | `string \| null` | Current Bearer token |
| `account` | `string \| null` | User identifier (email/username) |
| `signedIn` | `boolean` | Whether a session is active |
| `expiresAt` | `number \| null` | Unix timestamp when the token expires |
| `isRefreshing` | `boolean` | Guards against concurrent `refreshToken()` calls from the store |

| Action | Description |
| --- | --- |
| `setSession(token, account, expiresAt)` | Sets session fields and flips `signedIn = true` |
| `signIn(chromeProfileDirectory?)` | Calls `auth_sign_in` with a Chrome profile directory, or `null`/`undefined` for system default browser; then `setSession` |
| `signOut()` | Calls `auth_sign_out` Rust command, then clears all state |
| `restoreSession()` | Calls `auth_restore_session` Rust command; returns `true` if session restored |
| `refreshToken()` | Calls `auth_refresh_token` Rust command; guarded by `isRefreshing` |

## Dependencies

| Crate | Version | Purpose |
| --- | --- | --- |
| `oauth2` | 5 (no default features, `reqwest` feature) | OAuth2 client for PKCE, token exchange, refresh |
| `keyring` | 3 | OS keychain integration (macOS Keychain, Windows Credential Manager, Linux Secret Service) |
| `reqwest` | (via oauth2) | HTTP client for token endpoint calls |
| `tokio` | (Tauri) | Async runtime, TCP listener for loopback redirect |
| `serde` / `serde_json` | (Tauri) | Token serialization, Chrome Local State parsing |
| `base64` | (Tauri) | URL-safe no-pad base64 for id_token payload decoding |
| `urlencoding` | (Tauri) | Decode URL-encoded query parameters from the redirect |
