use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine as _};
use keyring::Entry;
use oauth2::basic::{BasicErrorResponse, BasicRevocationErrorResponse, BasicTokenType};
use oauth2::{
    AuthUrl, AuthorizationCode, Client, ClientId, CsrfToken, EmptyExtraTokenFields, EndpointNotSet,
    EndpointSet, ExtraTokenFields, PkceCodeChallenge, RedirectUrl, RefreshToken, Scope,
    StandardRevocableToken, StandardTokenIntrospectionResponse, StandardTokenResponse,
    TokenResponse, TokenUrl,
};
use serde::{Deserialize, Serialize};
use std::cmp::Ordering;
use std::fs;
use std::path::{Path, PathBuf};
use std::process::Command as StdCommand;
use std::time::{SystemTime, UNIX_EPOCH};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpListener;

/// Keyring service name matching the Tauri app identifier.
pub(crate) const KEYRING_SERVICE: &str = "com.palfrey.motorcyclerag.admin.desktop";
/// Keyring account name — single active session.
pub(crate) const KEYRING_ACCOUNT: &str = "session";

// ── OAuth2 type definitions ──────────────────────────────────────────────────

/// Extra OIDC fields captured from the Entra token response (specifically `id_token`).
#[derive(Clone, Deserialize, Serialize)]
struct OidcExtraTokenFields {
    #[serde(skip_serializing_if = "Option::is_none")]
    id_token: Option<String>,
}

impl std::fmt::Debug for OidcExtraTokenFields {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("OidcExtraTokenFields")
            .field("id_token", &"<redacted>")
            .finish()
    }
}
impl ExtraTokenFields for OidcExtraTokenFields {}

/// Token response that includes the OpenID Connect `id_token`.
type OidcTokenResponse = StandardTokenResponse<OidcExtraTokenFields, BasicTokenType>;

/// OAuth2 client configured for our Entra PKCE flow.
type OidcClient = Client<
    BasicErrorResponse,
    OidcTokenResponse,
    StandardTokenIntrospectionResponse<EmptyExtraTokenFields, BasicTokenType>,
    StandardRevocableToken,
    BasicRevocationErrorResponse,
    EndpointSet,    // HasAuthUrl
    EndpointNotSet, // HasDeviceAuthUrl
    EndpointNotSet, // HasIntrospectionUrl
    EndpointNotSet, // HasRevocationUrl
    EndpointSet,    // HasTokenUrl
>;

/// Configuration values needed for the OAuth2 flow, read from the Tauri store
/// by the command wrapper in `lib.rs`.
pub(crate) struct AuthConfig {
    pub(crate) authority: String,
    pub(crate) client_id: String,
    pub(crate) scope: String,
}

/// Token data persisted in the OS keyring. Serialized to JSON for storage.
///
/// Deliberately does NOT derive `Debug` — these structs carry bearer tokens
/// and must never be logged in their plaintext form.
#[derive(Serialize, Deserialize, Clone)]
pub(crate) struct StoredToken {
    pub(crate) access_token: String,
    pub(crate) refresh_token: Option<String>,
    pub(crate) id_token: Option<String>,
    pub(crate) account: String,
    pub(crate) expires_at: u64,
    pub(crate) scope: String,
}

/// Frontend-facing session representation. Serialized with camelCase.
#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
pub(crate) struct AuthSession {
    pub(crate) access_token: String,
    pub(crate) account: String,
    pub(crate) expires_at: u64,
}

/// Manual `Debug` impl that redacts the bearer access token. Only the account
/// and expiry are surfaced — never the token itself.
impl std::fmt::Debug for AuthSession {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("AuthSession")
            .field("access_token", &"<redacted>")
            .field("account", &self.account)
            .field("expires_at", &self.expires_at)
            .finish()
    }
}

/// A Chrome user profile discovered on disk.
#[derive(Serialize, Clone, Debug, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub(crate) struct ChromeProfile {
    pub(crate) directory: String,
    pub(crate) name: String,
    pub(crate) user_name: Option<String>,
}

/// Result of Chrome profile discovery for the sign-in picker (D2).
///
/// On I/O or parse failure, `profiles` is empty and `error` explains why —
/// never a silent sole fake `Default`.
#[derive(Serialize, Clone, Debug, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub(crate) struct ChromeProfilesResult {
    pub(crate) profiles: Vec<ChromeProfile>,
    pub(crate) error: Option<String>,
}

/// Failure modes for pure Local State profile parsing (D2/D5).
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum ChromeProfileParseError {
    InvalidJson,
    MissingInfoCache,
}

/// macOS Google Chrome stable binary path (D1/D3).
pub(crate) const MACOS_CHROME_BINARY: &str =
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome";

/// Convert a stored token into the frontend-facing session shape.
pub(crate) fn stored_token_to_session(token: &StoredToken) -> AuthSession {
    AuthSession {
        access_token: token.access_token.clone(),
        account: token.account.clone(),
        expires_at: token.expires_at,
    }
}

/// Persist tokens to the OS keychain.
pub(crate) fn store_tokens(token: &StoredToken) -> Result<(), String> {
    let entry = Entry::new(KEYRING_SERVICE, KEYRING_ACCOUNT)
        .map_err(|e| format!("Failed to create keyring entry: {e}"))?;
    let json =
        serde_json::to_string(token).map_err(|e| format!("Failed to serialize token: {e}"))?;
    entry
        .set_password(&json)
        .map_err(|e| format!("Failed to store token in keychain: {e}"))?;
    Ok(())
}

/// Load tokens from the OS keychain. Returns `None` if no token exists or deserialization fails.
pub(crate) fn load_tokens() -> Option<StoredToken> {
    let entry = Entry::new(KEYRING_SERVICE, KEYRING_ACCOUNT).ok()?;
    let json = entry.get_password().ok()?;
    serde_json::from_str(&json).ok()
}

/// Delete tokens from the OS keychain. Returns `Ok(())` even if no entry existed.
pub(crate) fn delete_tokens() -> Result<(), String> {
    let entry = Entry::new(KEYRING_SERVICE, KEYRING_ACCOUNT)
        .map_err(|e| format!("Failed to create keyring entry: {e}"))?;
    match entry.delete_credential() {
        Ok(()) => Ok(()),
        Err(keyring::Error::NoEntry) => Ok(()),
        Err(e) => Err(format!("Failed to delete token from keychain: {e}")),
    }
}

/// Clear the persisted session by deleting tokens from the keyring.
pub(crate) fn sign_out() -> Result<(), String> {
    delete_tokens()
}

/// Resolve the Chrome `Local State` file path for the current platform.
fn chrome_local_state_path() -> Option<PathBuf> {
    #[cfg(target_os = "windows")]
    let home = std::env::var("HOME")
        .or_else(|_| std::env::var("USERPROFILE"))
        .ok()?;
    #[cfg(not(target_os = "windows"))]
    let home = std::env::var("HOME").ok()?;

    #[cfg(target_os = "macos")]
    {
        Some(PathBuf::from(&home).join("Library/Application Support/Google/Chrome/Local State"))
    }
    #[cfg(target_os = "windows")]
    {
        std::env::var("LOCALAPPDATA").ok().map(|local_app_data| {
            PathBuf::from(local_app_data).join("Google/Chrome/User Data/Local State")
        })
    }
    #[cfg(target_os = "linux")]
    {
        Some(PathBuf::from(&home).join(".config/google-chrome/Local State"))
    }
    #[cfg(not(any(target_os = "macos", target_os = "windows", target_os = "linux")))]
    {
        None
    }
}

/// Enumerate Chrome profiles with an optional error diagnostic (D2).
///
/// Success: `profiles` from `profile.info_cache`, `error: None`.
/// Failure (missing path, I/O, parse): empty `profiles` and a non-empty `error` —
/// never a silent sole fake `Default`.
pub(crate) fn list_chrome_profiles() -> ChromeProfilesResult {
    let path = match chrome_local_state_path() {
        Some(p) => p,
        None => {
            let message = "Chrome Local State path is unavailable on this platform".to_string();
            eprintln!("auth: chrome profile enumeration failed: {message}");
            return ChromeProfilesResult {
                profiles: vec![],
                error: Some(message),
            };
        }
    };

    discover_chrome_profiles_at(&path)
}

/// Read + parse Chrome profiles from a specific `Local State` path (D5 testability).
fn discover_chrome_profiles_at(path: &Path) -> ChromeProfilesResult {
    let content = match fs::read_to_string(path) {
        Ok(c) => c,
        Err(e) => {
            let message = format!("could not read Chrome profiles at {}: {e}", path.display());
            eprintln!("auth: chrome profile enumeration failed: {message}");
            return ChromeProfilesResult {
                profiles: vec![],
                error: Some(message),
            };
        }
    };

    match parse_chrome_profiles_from_local_state(&content) {
        Ok(profiles) => ChromeProfilesResult {
            profiles,
            error: None,
        },
        Err(ChromeProfileParseError::InvalidJson) => {
            let message = format!(
                "could not parse Chrome Local State as JSON ({})",
                path.display()
            );
            eprintln!("auth: chrome profile enumeration failed: {message}");
            ChromeProfilesResult {
                profiles: vec![],
                error: Some(message),
            }
        }
        Err(ChromeProfileParseError::MissingInfoCache) => {
            let message = format!(
                "Chrome Local State is missing profile.info_cache ({})",
                path.display()
            );
            eprintln!("auth: chrome profile enumeration failed: {message}");
            ChromeProfilesResult {
                profiles: vec![],
                error: Some(message),
            }
        }
    }
}

/// Pure parse of Chrome `Local State` JSON into sorted profiles (Default first).
///
/// Valid `profile.info_cache` object → all entries with directory/name/user_name.
/// Invalid JSON → `Err(InvalidJson)`. Missing/non-object `info_cache` →
/// `Err(MissingInfoCache)`. Never invents a silent fake sole Default.
pub(crate) fn parse_chrome_profiles_from_local_state(
    local_state_json: &str,
) -> Result<Vec<ChromeProfile>, ChromeProfileParseError> {
    let json: serde_json::Value =
        serde_json::from_str(local_state_json).map_err(|_| ChromeProfileParseError::InvalidJson)?;

    let info_cache = json
        .get("profile")
        .and_then(|p| p.get("info_cache"))
        .and_then(|cache| cache.as_object())
        .ok_or(ChromeProfileParseError::MissingInfoCache)?;

    let mut profiles: Vec<ChromeProfile> = info_cache
        .iter()
        .map(|(dir, info)| ChromeProfile {
            directory: dir.clone(),
            name: info
                .get("name")
                .and_then(|n| n.as_str())
                .unwrap_or(dir)
                .to_string(),
            user_name: info
                .get("user_name")
                .and_then(|u| u.as_str())
                .map(|s| s.to_string()),
        })
        .collect();

    // Sort: "Default" first, then alphabetical by directory.
    profiles.sort_by(
        |a, b| match (a.directory == "Default", b.directory == "Default") {
            (true, false) => Ordering::Less,
            (false, true) => Ordering::Greater,
            _ => a.directory.cmp(&b.directory),
        },
    );

    Ok(profiles)
}

/// Build macOS Chrome launch `(program, args)` without spawning (D3).
///
/// When `chrome_binary_available` is true: Chrome binary + `--profile-directory` + URL.
/// Otherwise: `open -na "Google Chrome" --args --profile-directory=… URL`.
pub(crate) fn build_macos_chrome_launch_command(
    profile_directory: &str,
    url: &str,
    chrome_binary_available: bool,
) -> (String, Vec<String>) {
    let profile_arg = format!("--profile-directory={profile_directory}");
    if chrome_binary_available {
        (
            MACOS_CHROME_BINARY.to_string(),
            vec![profile_arg, url.to_string()],
        )
    } else {
        (
            "open".to_string(),
            vec![
                "-na".to_string(),
                "Google Chrome".to_string(),
                "--args".to_string(),
                profile_arg,
                url.to_string(),
            ],
        )
    }
}

/// Chrome profile folder names are short identifiers like `Default` or `Profile 1`.
/// Reject path separators and shell metacharacters before they reach process argv.
fn is_valid_chrome_profile_directory(profile_directory: &str) -> bool {
    if profile_directory.is_empty() || profile_directory.len() > 128 {
        return false;
    }
    if profile_directory.contains('/')
        || profile_directory.contains('\\')
        || profile_directory.contains("..")
    {
        return false;
    }
    profile_directory
        .chars()
        .all(|c| c.is_ascii_alphanumeric() || matches!(c, ' ' | '.' | '_' | '-' | '\''))
}

// ── OAuth2 sign-in flow ───────────────────────────────────────────────────────

/// Build the OAuth2 client configured for our Entra PKCE flow (no client secret).
fn build_oidc_client(config: &AuthConfig, redirect_uri: &str) -> Result<OidcClient, String> {
    let auth_url = format!("{}/oauth2/v2.0/authorize", config.authority);
    let token_url = format!("{}/oauth2/v2.0/token", config.authority);

    let client = Client::new(ClientId::new(config.client_id.clone()))
        .set_auth_uri(AuthUrl::new(auth_url).map_err(|e| format!("invalid auth URL: {e}"))?)
        .set_token_uri(TokenUrl::new(token_url).map_err(|e| format!("invalid token URL: {e}"))?)
        .set_redirect_uri(
            RedirectUrl::new(redirect_uri.to_string())
                .map_err(|e| format!("invalid redirect URI: {e}"))?,
        );
    Ok(client)
}

/// Open a URL in Google Chrome with a specific user profile.
/// Platform-specific: uses `open -a` on macOS, direct path on Windows, `google-chrome` on Linux.
fn open_browser(url: &str, profile_directory: &str) -> Result<(), String> {
    if !is_valid_chrome_profile_directory(profile_directory) {
        return Err(format!(
            "invalid Chrome profile directory '{profile_directory}': expected a Chrome profile folder name"
        ));
    }

    #[cfg(target_os = "macos")]
    {
        let chrome_binary_available = Path::new(MACOS_CHROME_BINARY).is_file();
        let (program, args) =
            build_macos_chrome_launch_command(profile_directory, url, chrome_binary_available);
        StdCommand::new(&program)
            .args(&args)
            .spawn()
            .map_err(|e| format!("failed to open Chrome on macOS: {e}"))?;
    }

    #[cfg(target_os = "windows")]
    {
        let chrome_paths = [
            r"C:\Program Files\Google\Chrome\Application\chrome.exe",
            r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
        ];
        let mut launched = false;
        for path in &chrome_paths {
            if StdCommand::new(path)
                .args([
                    format!("--profile-directory={profile_directory}"),
                    url.to_string(),
                ])
                .spawn()
                .is_ok()
            {
                launched = true;
                break;
            }
        }
        if !launched {
            return Err("failed to launch Chrome on Windows".to_string());
        }
    }

    #[cfg(target_os = "linux")]
    {
        StdCommand::new("google-chrome")
            .args([
                format!("--profile-directory={profile_directory}"),
                url.to_string(),
            ])
            .spawn()
            .map_err(|e| format!("failed to open Chrome on Linux: {e}"))?;
    }

    #[cfg(not(any(target_os = "macos", target_os = "windows", target_os = "linux")))]
    {
        let _ = (url, profile_directory);
        return Err("unsupported platform for Chrome profile launch".to_string());
    }

    Ok(())
}

/// Extract the account identifier from an OIDC `id_token` JWT.
/// Decodes the payload (part 1, base64url, no signature verification) and reads
/// `preferred_username` → `email` → `name`.
pub(crate) fn extract_account_from_id_token(id_token: &str) -> Option<String> {
    let payload = id_token.split('.').nth(1)?;
    let decoded = URL_SAFE_NO_PAD.decode(payload).ok()?;
    let json: serde_json::Value = serde_json::from_slice(&decoded).ok()?;
    json["preferred_username"]
        .as_str()
        .or_else(|| json["email"].as_str())
        .or_else(|| json["name"].as_str())
        .map(String::from)
}

/// Full Entra auth-code + PKCE loopback flow using the `oauth2` crate.
///
/// 1. Generates PKCE verifier/challenge via `oauth2` crate.
/// 2. Binds a random localhost port to catch the redirect.
/// 3. Opens the Entra authorize URL in Chrome (with profile if specified).
/// 4. Accepts the redirect, writes a "sign-in complete" page, extracts the code.
/// 5. Exchanges the code for tokens via `oauth2` crate.
/// 6. Persists tokens via `store_tokens`, returns session.
pub(crate) async fn sign_in(
    config: &AuthConfig,
    profile_directory: Option<String>,
) -> Result<AuthSession, String> {
    // ── Loopback listener (bind before building URL so we know the port) ──────
    let listener = TcpListener::bind("127.0.0.1:0")
        .await
        .map_err(|e| format!("bind failed: {e}"))?;
    let port = listener
        .local_addr()
        .map_err(|e| format!("local_addr: {e}"))?
        .port();
    let redirect_uri = format!("http://localhost:{port}");

    // ── OAuth2 client ──────────────────────────────────────────────────────────
    let client = build_oidc_client(config, &redirect_uri)?;

    // ── PKCE ──────────────────────────────────────────────────────────────────
    let (pkce_challenge, pkce_verifier) = PkceCodeChallenge::new_random_sha256();

    // ── Build auth URL with scopes ─────────────────────────────────────────────
    let (auth_url, csrf_state) = client
        .authorize_url(CsrfToken::new_random)
        .add_scope(Scope::new(config.scope.clone()))
        .add_scope(Scope::new("openid".to_string()))
        .add_scope(Scope::new("profile".to_string()))
        .add_scope(Scope::new("offline_access".to_string()))
        .add_extra_param("prompt", "select_account")
        .set_pkce_challenge(pkce_challenge)
        .url();

    let auth_url_string = auth_url.to_string();

    // ── Open browser ───────────────────────────────────────────────────────────
    match profile_directory.as_deref() {
        Some(profile_dir) => {
            open_browser(&auth_url_string, profile_dir)?;
        }
        None => {
            // Open system default browser when no Chrome profile is requested.
            #[cfg(target_os = "macos")]
            {
                StdCommand::new("open")
                    .arg(&auth_url_string)
                    .spawn()
                    .map_err(|e| format!("failed to open default browser: {e}"))?;
            }
            #[cfg(target_os = "windows")]
            {
                // `cmd /c start` treats the first quoted arg as the window
                // title. Passing an explicit empty title ("") before the URL
                // prevents a URL containing spaces/special chars from being
                // parsed as the title or as additional start subcommands —
                // closing the classic command-injection vector.
                StdCommand::new("cmd")
                    .args(["/c", "start", "", &auth_url_string])
                    .spawn()
                    .map_err(|e| format!("failed to open default browser: {e}"))?;
            }
            #[cfg(target_os = "linux")]
            {
                StdCommand::new("xdg-open")
                    .arg(&auth_url_string)
                    .spawn()
                    .map_err(|e| format!("failed to open default browser: {e}"))?;
            }
        }
    }

    // ── Wait for redirect (5-minute timeout) ──────────────────────────────────
    let (code, returned_state) = tokio::time::timeout(std::time::Duration::from_secs(300), async {
        let (mut stream, _) = listener.accept().await?;
        let mut buf = vec![0u8; 4096];
        let n = stream.read(&mut buf).await?;
        let request = String::from_utf8_lossy(&buf[..n]);

        let path = request
            .lines()
            .next()
            .and_then(|l| l.split_whitespace().nth(1))
            .unwrap_or("/");

        let html = "<html><body style='font-family:sans-serif;padding:2em'>\
                <h2 style='color:#ff6600'>Sign-in complete</h2>\
                <p>You can close this tab and return to MotorcycleRAG Admin.</p>\
                </body></html>";
        let _ = stream
            .write_all(
                format!(
                    "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\n\
                         Content-Length: {}\r\nConnection: close\r\n\r\n{html}",
                    html.len()
                )
                .as_bytes(),
            )
            .await;

        let query = path.split_once('?').map(|(_, q)| q).unwrap_or("");
        let mut code = String::new();
        let mut state = String::new();
        for pair in query.split('&') {
            let mut kv = pair.splitn(2, '=');
            match (kv.next(), kv.next()) {
                (Some("code"), Some(v)) => {
                    code = urlencoding::decode(v).unwrap_or_default().into_owned();
                }
                (Some("state"), Some(v)) => {
                    state = urlencoding::decode(v).unwrap_or_default().into_owned();
                }
                _ => {}
            }
        }

        if code.is_empty() {
            let desc = query
                .split('&')
                .find(|p| p.starts_with("error_description="))
                .and_then(|p| p.split_once('=').map(|(_, v)| v))
                .map(|s| urlencoding::decode(s).unwrap_or_default().into_owned())
                .unwrap_or_else(|| "authentication failed".into());
            return Err(std::io::Error::other(desc));
        }

        Ok((code, state))
    })
    .await
    .map_err(|_| "sign-in timed out after 5 minutes".to_string())?
    .map_err(|e| e.to_string())?;

    // ── Validate CSRF state ────────────────────────────────────────────────────
    if returned_state != *csrf_state.secret() {
        return Err("state mismatch — possible CSRF attack".into());
    }

    // ── Exchange code for tokens ───────────────────────────────────────────────
    let http_client = reqwest::Client::builder()
        .redirect(reqwest::redirect::Policy::none())
        .build()
        .map_err(|e| format!("failed to build HTTP client: {e}"))?;

    let token_response = client
        .exchange_code(AuthorizationCode::new(code))
        .set_pkce_verifier(pkce_verifier)
        .request_async(&http_client)
        .await
        .map_err(|e| format!("token exchange failed: {e}"))?;

    // ── Extract token fields ───────────────────────────────────────────────────
    let access_token = token_response.access_token().secret().to_string();
    let refresh_token = token_response
        .refresh_token()
        .map(|t| t.secret().to_string());
    let id_token = token_response.extra_fields().id_token.clone();
    let expires_in = token_response
        .expires_in()
        .map(|d| d.as_secs())
        .unwrap_or(3600);
    let expires_at = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_err(|e| format!("system clock error: {e}"))?
        .as_secs()
        + expires_in;

    let scope = config.scope.clone();
    let account =
        extract_account_from_id_token(id_token.as_deref().unwrap_or("")).unwrap_or_default();

    let stored = StoredToken {
        access_token,
        refresh_token,
        id_token,
        account: account.clone(),
        expires_at,
        scope,
    };

    store_tokens(&stored)?;
    Ok(stored_token_to_session(&stored))
}

/// Build an OAuth2 client with only auth + token endpoints (no redirect URI),
/// sufficient for the refresh-token exchange.
fn build_token_client(config: &AuthConfig) -> Result<OidcClient, String> {
    let auth_url = format!("{}/oauth2/v2.0/authorize", config.authority);
    let token_url = format!("{}/oauth2/v2.0/token", config.authority);

    let client = Client::new(ClientId::new(config.client_id.clone()))
        .set_auth_uri(AuthUrl::new(auth_url).map_err(|e| format!("invalid auth URL: {e}"))?)
        .set_token_uri(TokenUrl::new(token_url).map_err(|e| format!("invalid token URL: {e}"))?);
    Ok(client)
}

/// Exchange a stored refresh token for new tokens via Entra's token endpoint.
/// Returns a new `AuthSession` with the refreshed access token.
///
/// 1. Load stored tokens from keyring (fail if absent or no refresh token).
/// 2. Build an OAuth2 client (no redirect URI needed for refresh).
/// 3. Exchange the refresh token via Entra's token endpoint.
/// 4. Persist the new tokens (keeping the old refresh token if Entra didn't return one).
/// 5. Return the updated `AuthSession`.
pub(crate) async fn refresh(config: &AuthConfig) -> Result<AuthSession, String> {
    // ── Load stored tokens from keyring ────────────────────────────────────────
    let stored = load_tokens().ok_or("No stored session found")?;
    let refresh_token = stored
        .refresh_token
        .clone()
        .ok_or("No refresh token available")?;

    // ── OAuth2 client (no redirect URI needed) ─────────────────────────────────
    let client = build_token_client(config)?;

    // ── Exchange refresh token ─────────────────────────────────────────────────
    let http_client = reqwest::Client::builder()
        .redirect(reqwest::redirect::Policy::none())
        .build()
        .map_err(|e| format!("failed to build HTTP client: {e}"))?;

    let token_response = client
        .exchange_refresh_token(&RefreshToken::new(refresh_token))
        .request_async(&http_client)
        .await
        .map_err(|e| format!("token refresh failed: {e}"))?;

    // ── Extract new token fields ───────────────────────────────────────────────
    let access_token = token_response.access_token().secret().to_string();
    let new_refresh = token_response
        .refresh_token()
        .map(|t| t.secret().to_string());
    let id_token = token_response
        .extra_fields()
        .id_token
        .clone()
        .or(stored.id_token.clone());
    let expires_in = token_response
        .expires_in()
        .map(|d| d.as_secs())
        .unwrap_or(3600);
    let expires_at = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_err(|e| format!("system clock error: {e}"))?
        .as_secs()
        + expires_in;

    // ── Persist refreshed tokens ───────────────────────────────────────────────
    // Entra may not return a new refresh token — keep the old one if absent.
    let new_stored = StoredToken {
        access_token,
        refresh_token: new_refresh.or(stored.refresh_token),
        id_token,
        account: stored.account.clone(),
        expires_at,
        scope: stored.scope.clone(),
    };
    store_tokens(&new_stored)?;

    Ok(stored_token_to_session(&new_stored))
}

/// Attempt to restore the previously-persisted session from the OS keychain.
///
/// 1. Load stored tokens. If none exist, return `None`.
/// 2. If the access token is still valid (with a 60-second skew), return the session.
/// 3. If a refresh token is available, try to exchange it; on success return the new
///    session, on failure delete the stored tokens and return `None`.
/// 4. Otherwise delete any stored tokens and return `None`.
pub(crate) async fn restore(config: &AuthConfig) -> Result<Option<AuthSession>, String> {
    let stored = match load_tokens() {
        Some(t) => t,
        None => return Ok(None),
    };

    let now = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map_err(|e| format!("system clock error: {e}"))?
        .as_secs();

    if stored.expires_at > now + 60 {
        return Ok(Some(stored_token_to_session(&stored)));
    }

    if stored.refresh_token.is_some() {
        match refresh(config).await {
            Ok(session) => return Ok(Some(session)),
            Err(_) => {
                let _ = delete_tokens();
                return Ok(None);
            }
        }
    }

    let _ = delete_tokens();
    Ok(None)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_stored_token_serde_roundtrip() {
        let token = StoredToken {
            access_token: "test_access".to_string(),
            refresh_token: Some("test_refresh".to_string()),
            id_token: Some("test_id".to_string()),
            account: "user@example.com".to_string(),
            expires_at: 9999999999,
            scope: "api://test/admin openid profile offline_access".to_string(),
        };
        let json = serde_json::to_string(&token).unwrap();
        let deserialized: StoredToken = serde_json::from_str(&json).unwrap();
        assert_eq!(deserialized.access_token, "test_access");
        assert_eq!(deserialized.refresh_token, Some("test_refresh".to_string()));
        assert_eq!(deserialized.account, "user@example.com");
        assert_eq!(deserialized.expires_at, 9999999999);
    }

    #[test]
    fn test_list_chrome_profiles_failure_is_not_silent_fake_default() {
        // D2: when discovery fails, return empty + diagnostic — never invent sole Default.
        // When the host has a readable Local State, real profiles are fine.
        let result = list_chrome_profiles();
        if result.profiles.is_empty() {
            assert!(
                result.error.is_some(),
                "empty discovery must surface an error diagnostic for the UI"
            );
            assert!(
                !looks_like_silent_fake_default(&result.profiles),
                "silent vec![Default] success is forbidden"
            );
        } else {
            assert!(
                result.error.is_none(),
                "successful discovery must not set error; got {:?}",
                result.error
            );
            assert!(result.profiles.iter().all(|p| !p.directory.is_empty()));
        }
    }

    #[test]
    fn test_discover_chrome_profiles_at_unreadable_path_is_empty_with_error() {
        let missing = PathBuf::from("/nonexistent/chrome/Local State");
        let result = discover_chrome_profiles_at(&missing);
        assert!(result.profiles.is_empty());
        assert!(result.error.is_some());
        assert!(!looks_like_silent_fake_default(&result.profiles));
    }

    #[test]
    fn test_discover_chrome_profiles_at_fixture_file() {
        let dir = tempfile::tempdir().expect("tempdir");
        let path = dir.path().join("Local State");
        fs::write(&path, multi_profile_local_state_fixture()).expect("write fixture");

        let result = discover_chrome_profiles_at(&path);
        assert!(
            result.error.is_none(),
            "unexpected error: {:?}",
            result.error
        );
        assert_eq!(result.profiles.len(), 5);
        assert_eq!(result.profiles[0].directory, "Default");
        assert_eq!(result.profiles[1].directory, "Profile 1");
    }

    #[test]
    fn test_is_valid_chrome_profile_directory() {
        assert!(is_valid_chrome_profile_directory("Default"));
        assert!(is_valid_chrome_profile_directory("Profile 1"));
        assert!(is_valid_chrome_profile_directory("Guest Profile"));
        assert!(!is_valid_chrome_profile_directory(""));
        assert!(!is_valid_chrome_profile_directory("../etc"));
        assert!(!is_valid_chrome_profile_directory("Default;rm -rf /"));
        assert!(!is_valid_chrome_profile_directory("foo/bar"));
        assert!(!is_valid_chrome_profile_directory("foo\\bar"));
    }

    #[test]
    fn test_chrome_local_state_parsing_structure() {
        use serde_json::json;
        let sample = json!({
            "profile": {
                "info_cache": {
                    "Default": { "name": "Default", "user_name": null },
                    "Profile 1": { "name": "Work", "user_name": "user@company.com" },
                    "Profile 2": { "name": "Personal", "user_name": "user@gmail.com" }
                }
            }
        });
        let cache = sample["profile"]["info_cache"].as_object().unwrap();
        assert_eq!(cache.len(), 3);
        assert!(cache.contains_key("Default"));
    }

    /// Fixture: Default + Profile 1..4 (N=4 → N+1 = 5 profiles).
    fn multi_profile_local_state_fixture() -> String {
        serde_json::json!({
            "profile": {
                "info_cache": {
                    "Profile 2": { "name": "Personal", "user_name": "user@gmail.com" },
                    "Default": { "name": "Person 1", "user_name": "default@example.com" },
                    "Profile 1": { "name": "Work", "user_name": "user@company.com" },
                    "Profile 4": { "name": "Dev", "user_name": null },
                    "Profile 3": { "name": "School", "user_name": "school@edu.edu" }
                }
            }
        })
        .to_string()
    }

    fn looks_like_silent_fake_default(profiles: &[ChromeProfile]) -> bool {
        profiles.len() == 1
            && profiles[0].directory == "Default"
            && profiles[0].name == "Default"
            && profiles[0].user_name.is_none()
    }

    #[test]
    fn test_parse_chrome_profiles_from_local_state_multi_profile_sorted() {
        let json = multi_profile_local_state_fixture();

        let profiles = parse_chrome_profiles_from_local_state(&json)
            .expect("valid Local State fixture must parse");

        assert_eq!(
            profiles.len(),
            5,
            "Default + Profile 1..4 must yield 5 profiles"
        );
        assert_eq!(profiles[0].directory, "Default");
        assert_eq!(profiles[0].name, "Person 1");
        assert_eq!(
            profiles[0].user_name.as_deref(),
            Some("default@example.com")
        );
        assert_eq!(profiles[1].directory, "Profile 1");
        assert_eq!(profiles[1].name, "Work");
        assert_eq!(profiles[1].user_name.as_deref(), Some("user@company.com"));
        assert_eq!(profiles[2].directory, "Profile 2");
        assert_eq!(profiles[2].name, "Personal");
        assert_eq!(profiles[2].user_name.as_deref(), Some("user@gmail.com"));
        assert_eq!(profiles[3].directory, "Profile 3");
        assert_eq!(profiles[3].name, "School");
        assert_eq!(profiles[3].user_name.as_deref(), Some("school@edu.edu"));
        assert_eq!(profiles[4].directory, "Profile 4");
        assert_eq!(profiles[4].name, "Dev");
        assert_eq!(profiles[4].user_name, None);
    }

    #[test]
    fn test_parse_chrome_profiles_from_local_state_invalid_json_not_fake_default() {
        let result = parse_chrome_profiles_from_local_state("{ not valid json");

        match result {
            Ok(profiles) => {
                assert!(
                    profiles.is_empty(),
                    "invalid JSON may return empty Ok, not invented profiles"
                );
                assert!(
                    !looks_like_silent_fake_default(&profiles),
                    "silent vec![Default] success is forbidden for invalid JSON"
                );
            }
            Err(ChromeProfileParseError::InvalidJson) => {}
            Err(other) => panic!("expected InvalidJson or empty Ok, got {other:?}"),
        }
    }

    #[test]
    fn test_parse_chrome_profiles_from_local_state_missing_info_cache_not_fake_default() {
        let json = r#"{"profile":{"last_used":"Default"}}"#;
        let result = parse_chrome_profiles_from_local_state(json);

        match result {
            Ok(profiles) => {
                assert!(
                    profiles.is_empty(),
                    "missing info_cache may return empty Ok, not invented profiles"
                );
                assert!(
                    !looks_like_silent_fake_default(&profiles),
                    "silent vec![Default] success is forbidden when info_cache is missing"
                );
            }
            Err(ChromeProfileParseError::MissingInfoCache) => {}
            Err(other) => panic!("expected MissingInfoCache or empty Ok, got {other:?}"),
        }
    }

    #[test]
    fn test_build_macos_chrome_launch_command_prefers_binary_path() {
        let url = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?x=1";
        let (program, args) = build_macos_chrome_launch_command("Profile 1", url, true);

        assert_eq!(program, MACOS_CHROME_BINARY);
        assert!(
            args.iter().any(|a| a == "--profile-directory=Profile 1"
                || a == "Profile 1" && args.iter().any(|f| f == "--profile-directory")),
            "argv must include --profile-directory for Profile 1; got {args:?}"
        );
        assert!(
            args.iter().any(|a| a == url),
            "argv must include the auth URL; got {args:?}"
        );
        assert!(
            !args.iter().any(|a| a == "-a"),
            "binary launch must not use open -a; got {args:?}"
        );
    }

    #[test]
    fn test_build_macos_chrome_launch_command_open_na_fallback() {
        let url = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?x=1";
        let (program, args) = build_macos_chrome_launch_command("Default", url, false);

        assert_eq!(program, "open");
        assert!(
            args.windows(2)
                .any(|w| w[0] == "-na" && w[1] == "Google Chrome")
                || (args.contains(&"-n".to_string())
                    && args.contains(&"-a".to_string())
                    && args.contains(&"Google Chrome".to_string())),
            "fallback must use open -na (or -n -a) Google Chrome; got {args:?}"
        );
        assert!(
            args.iter().any(|a| a == "--args"),
            "fallback must pass --args; got {args:?}"
        );
        assert!(
            args.iter().any(|a| a == "--profile-directory=Default"),
            "fallback must include --profile-directory=Default; got {args:?}"
        );
        assert!(
            args.iter().any(|a| a == url),
            "fallback must include the auth URL; got {args:?}"
        );
    }

    /// Build a fake (unsigned) JWT whose payload is the base64url-no-pad encoding
    /// of `payload_json`. The header and signature parts are irrelevant to
    /// `extract_account_from_id_token`, which only decodes part 1.
    fn fake_jwt(payload_json: &serde_json::Value) -> String {
        let header = URL_SAFE_NO_PAD.encode(b"{\"alg\":\"none\",\"typ\":\"JWT\"}");
        let payload_bytes = serde_json::to_vec(payload_json).unwrap();
        let payload = URL_SAFE_NO_PAD.encode(payload_bytes);
        let signature = URL_SAFE_NO_PAD.encode(b"sig");
        format!("{header}.{payload}.{signature}")
    }

    #[test]
    fn test_extract_account_from_id_token_preferred_username() {
        let jwt = fake_jwt(&serde_json::json!({
            "preferred_username": "alice@example.com",
            "email": "ignored@example.com",
            "name": "Alice"
        }));
        let account = extract_account_from_id_token(&jwt);
        assert_eq!(account.as_deref(), Some("alice@example.com"));
    }

    #[test]
    fn test_extract_account_from_id_token_falls_back_to_email() {
        // No preferred_username present — should fall back to the email claim.
        let jwt = fake_jwt(&serde_json::json!({
            "email": "bob@example.com",
            "name": "Bob"
        }));
        let account = extract_account_from_id_token(&jwt);
        assert_eq!(account.as_deref(), Some("bob@example.com"));
    }

    /// Probe whether the OS keychain can actually roundtrip a value using the
    /// same access pattern as `store_tokens`/`load_tokens` (i.e. separate
    /// `Entry` handles for the write and the read).
    ///
    /// On macOS a non-login / headless shell can silently accept
    /// `set_password` (returns `Ok`) yet report `NoEntry` from a *different*
    /// handle's `get_password`. That is a property of the OS keychain backend
    /// in such environments, not of our code, so a naive roundtrip would
    /// produce false failures. We guard the real roundtrip test with this probe
    /// and skip gracefully where the keychain isn't usable.
    fn keychain_roundtrips() -> bool {
        let (probe_service, probe_account) =
            ("com.palfrey.motorcyclerag.admin.desktop.__probe__", "probe");
        // Clean slate.
        {
            if let Ok(e) = Entry::new(probe_service, probe_account) {
                let _ = e.delete_credential();
            }
        }
        // Write via one handle.
        let wrote_ok = match Entry::new(probe_service, probe_account) {
            Ok(a) => a.set_password("probe-value").is_ok(),
            Err(_) => false,
        };
        if !wrote_ok {
            return false;
        }
        // Read via a *separate* handle — mirroring store_tokens/load_tokens.
        let read_ok = match Entry::new(probe_service, probe_account) {
            Ok(b) => matches!(b.get_password(), Ok(v) if v == "probe-value"),
            Err(_) => false,
        };
        // Cleanup.
        if let Ok(e) = Entry::new(probe_service, probe_account) {
            let _ = e.delete_credential();
        }
        read_ok
    }

    #[test]
    fn test_keyring_store_load_delete_roundtrip() {
        // Skip gracefully in environments without a usable OS keychain (e.g. CI /
        // headless shells). The test still runs the full roundtrip on a developer
        // machine where the keychain is accessible.
        if !keychain_roundtrips() {
            eprintln!(
                "skipping keyring roundtrip test: OS keychain not usable in this environment"
            );
            return;
        }

        // Ensure no leftover credential from a prior run pollutes this test.
        let _ = delete_tokens();

        let token = StoredToken {
            access_token: "rt_access".to_string(),
            refresh_token: Some("rt_refresh".to_string()),
            id_token: Some("rt_id".to_string()),
            account: "roundtrip@example.com".to_string(),
            expires_at: 1234567890,
            scope: "api://test/admin openid profile offline_access".to_string(),
        };

        // store → load
        store_tokens(&token).expect("store_tokens should succeed");
        let loaded = load_tokens().expect("load_tokens should return the stored token");
        assert_eq!(loaded.access_token, "rt_access");
        assert_eq!(loaded.refresh_token.as_deref(), Some("rt_refresh"));
        assert_eq!(loaded.id_token.as_deref(), Some("rt_id"));
        assert_eq!(loaded.account, "roundtrip@example.com");
        assert_eq!(loaded.expires_at, 1234567890);
        assert_eq!(
            loaded.scope,
            "api://test/admin openid profile offline_access"
        );

        // delete → load returns None
        delete_tokens().expect("delete_tokens should succeed");
        assert!(
            load_tokens().is_none(),
            "no token should be present after delete"
        );
    }

    #[test]
    fn test_delete_tokens_idempotent() {
        // Ensure no entry exists first
        let _ = delete_tokens();
        // Should succeed even when no entry exists
        assert!(delete_tokens().is_ok());
    }

    #[test]
    fn test_auth_config_construction() {
        let config = AuthConfig {
            authority: "https://login.microsoftonline.com/tenant-id".to_string(),
            client_id: "test-client-id".to_string(),
            scope: "api://test/admin".to_string(),
        };
        assert_eq!(
            config.authority,
            "https://login.microsoftonline.com/tenant-id"
        );
        assert_eq!(config.client_id, "test-client-id");
        assert_eq!(config.scope, "api://test/admin");
    }
}
