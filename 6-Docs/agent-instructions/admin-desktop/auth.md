# Admin Desktop — Auth (Entra PKCE Loopback)

**Decision:** no MSAL library. The entire flow lives in one Rust command (`auth_sign_in`).

## Flow (`src-tauri/src/lib.rs → auth_sign_in`)

1. Generate 32-byte PKCE `code_verifier` (base64url via `getrandom` + `base64`).
2. `code_challenge` = SHA-256(`code_verifier`) base64url (`sha2` crate).
3. Bind `tokio::net::TcpListener` on `127.0.0.1:0` → random ephemeral port.
4. Open `{authority}/oauth2/v2.0/authorize?...&redirect_uri=http://localhost:{port}&...` in the system browser via `tauri_plugin_opener`.
5. Accept one TCP connection, read the GET request, write a "Sign-in complete" HTML response, extract `code` + `state` from the query string.
6. Validate state (CSRF check), POST to `{authority}/oauth2/v2.0/token` with `reqwest`.
7. Decode `id_token` JWT payload (base64url, no signature verification) to extract `preferred_username` / `email` / `name`.
8. Return `{ accessToken, account, expiresIn }` to TypeScript.
9. TypeScript (`auth.ts → signIn()`) calls `useAuth.getState().setToken(...)` — this flips `signedIn: true` and `App.tsx` immediately renders the full shell.

**5-minute timeout** via `tokio::time::timeout`. State mismatch → hard error.

## Entra App Registration Requirements

- Platform: Mobile and desktop applications
- Redirect URI: `http://localhost` (Entra's loopback exception covers any ephemeral port)
- Allow public client flows: Yes
- Tenant ID: `0f8f8a52-f135-43af-af88-e0b54ca9ff91`
- Client ID: `a86e8458-4482-4bb6-808a-28d65b2668ef`
- Scope: `api://motorcyclerag-api/admin` (default in `DEFAULT_CONFIG.authScope`)

## Sign-out

Purely client-side: `useAuth.getState().signOut()` clears the token; `App.tsx` re-renders the sign-in screen. No server-side logout endpoint is called.
