use std::collections::{HashMap, HashSet};
use std::path::{Path, PathBuf};
use std::sync::Mutex;

use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine as _};
use serde::Deserialize;
use sha2::{Digest, Sha256};
use tauri::path::BaseDirectory;
use tauri::{Manager, State};
use tauri_plugin_opener::OpenerExt;
use tauri_plugin_shell::process::CommandChild;
use tauri_plugin_shell::ShellExt;

/// Supervises the local Python processor child process and remembers its port.
#[derive(Default)]
struct ProcessorState {
    child: Mutex<Option<CommandChild>>,
    port: Mutex<u16>,
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct ProcessorStartConfig {
    #[serde(default = "default_processor_port")]
    port: u16,
    #[serde(default)]
    working_dir: String,
    #[serde(default = "default_embedding_provider_endpoint")]
    embedding_provider_endpoint: String,
    #[serde(default = "default_embedding_model")]
    embedding_model: String,
    #[serde(default)]
    upload_job_secret: Option<String>,
    #[serde(default = "default_api_base_url")]
    api_base_url: String,
}

const DEFAULT_PROCESSOR_PORT: u16 = 8100;

fn default_processor_port() -> u16 {
    DEFAULT_PROCESSOR_PORT
}

fn default_embedding_provider_endpoint() -> String {
    "http://localhost:11434".to_string()
}

fn default_embedding_model() -> String {
    "qwen3-embedding".to_string()
}

fn default_api_base_url() -> String {
    "https://localhost:7215".to_string()
}

#[derive(Clone, Copy)]
enum ResolutionMode {
    RepositoryDevelopment,
    Packaged,
}

impl ResolutionMode {
    fn as_str(self) -> &'static str {
        match self {
            Self::RepositoryDevelopment => "repository-development",
            Self::Packaged => "packaged",
        }
    }
}

struct CandidatePath {
    path: PathBuf,
    mode: ResolutionMode,
    source: &'static str,
}

struct ResolvedProcessorPath {
    path: PathBuf,
    mode: ResolutionMode,
    source: &'static str,
}

fn resolve_processor_from_candidates(
    configured_override: &str,
    repo_candidates: Vec<CandidatePath>,
    packaged_candidates: Vec<CandidatePath>,
    prefer_repo: bool,
) -> Result<ResolvedProcessorPath, String> {
    let mut attempts: Vec<String> = Vec::new();

    let override_path = configured_override.trim();
    if !override_path.is_empty() {
        let candidate = PathBuf::from(override_path);
        match validate_processor_layout(&candidate) {
            Ok(()) => {
                return Ok(ResolvedProcessorPath {
                    path: candidate,
                    mode: ResolutionMode::RepositoryDevelopment,
                    source: "configured_override",
                });
            }
            Err(reason) => attempts.push(format!(
                "configured_override={} ({reason})",
                candidate.display()
            )),
        }
    }

    let mut ordered = Vec::new();
    if prefer_repo {
        ordered.extend(repo_candidates);
        ordered.extend(packaged_candidates);
    } else {
        ordered.extend(packaged_candidates);
        ordered.extend(repo_candidates);
    }

    let mut repo_failures = 0usize;
    let mut packaged_failures = 0usize;
    for candidate in ordered {
        match validate_processor_layout(&candidate.path) {
            Ok(()) => {
                return Ok(ResolvedProcessorPath {
                    path: candidate.path,
                    mode: candidate.mode,
                    source: candidate.source,
                });
            }
            Err(reason) => {
                match candidate.mode {
                    ResolutionMode::RepositoryDevelopment => repo_failures += 1,
                    ResolutionMode::Packaged => packaged_failures += 1,
                }
                attempts.push(format!(
                    "{}={} ({reason})",
                    candidate.source,
                    candidate.path.display()
                ));
            }
        }
    }

    let classification = match (repo_failures > 0, packaged_failures > 0, prefer_repo) {
        (true, true, true) => "repository-assets-unavailable",
        (true, true, false) => "packaged-assets-unavailable",
        (true, false, _) => "repository-assets-unavailable",
        (false, true, _) => "packaged-assets-unavailable",
        (false, false, _) => "processor-path-unresolved",
    };

    let guidance = match classification {
        "packaged-assets-unavailable" => {
            "The packaged processor assets are missing or incomplete. Repair/reinstall the Admin Desktop installation."
        }
        "repository-assets-unavailable" => {
            "Repository processor assets were detected but are incomplete. Ensure 2-Application/local-processing-service/src/main.py exists in the checked-out repository."
        }
        _ => {
            "No valid processor assets were discovered automatically. Run from a valid repository checkout or use a packaged installation that includes local-processing-service assets."
        }
    };

    let inspected = if attempts.is_empty() {
        "none".to_string()
    } else {
        attempts.join("; ")
    };

    Err(format!(
        "Processor working directory auto-resolution failed ({classification}). {guidance} Checked candidates: {inspected}"
    ))
}

fn looks_like_repo_root(root: &Path) -> bool {
    root.join("MotorcycleRAG.sln").is_file() && root.join("AGENTS.md").is_file()
}

fn validate_processor_layout(path: &Path) -> Result<(), String> {
    if !path.exists() {
        return Err("path does not exist".into());
    }
    if !path.is_dir() {
        return Err("path is not a directory".into());
    }

    let src_dir = path.join("src");
    if !src_dir.is_dir() {
        return Err("missing src directory".into());
    }

    let main_py = src_dir.join("main.py");
    if !main_py.is_file() {
        return Err("missing src/main.py".into());
    }

    Ok(())
}

fn find_repo_processor_from_seed(seed: &Path) -> Option<PathBuf> {
    for ancestor in seed.ancestors() {
        if !looks_like_repo_root(ancestor) {
            continue;
        }

        let candidate = ancestor
            .join("2-Application")
            .join("local-processing-service");
        if validate_processor_layout(&candidate).is_ok() {
            return Some(candidate);
        }
    }
    None
}

fn push_unique_candidate(
    candidates: &mut Vec<CandidatePath>,
    seen: &mut HashSet<PathBuf>,
    path: PathBuf,
    mode: ResolutionMode,
    source: &'static str,
) {
    if seen.insert(path.clone()) {
        candidates.push(CandidatePath { path, mode, source });
    }
}

fn build_repo_candidates(app: &tauri::AppHandle) -> Vec<CandidatePath> {
    let mut candidates = Vec::new();
    let mut seen = HashSet::new();

    if let Ok(current_dir) = std::env::current_dir() {
        if let Some(candidate) = find_repo_processor_from_seed(&current_dir) {
            push_unique_candidate(
                &mut candidates,
                &mut seen,
                candidate,
                ResolutionMode::RepositoryDevelopment,
                "current_dir",
            );
        }
    }

    if let Ok(current_exe) = std::env::current_exe() {
        if let Some(parent) = current_exe.parent() {
            if let Some(candidate) = find_repo_processor_from_seed(parent) {
                push_unique_candidate(
                    &mut candidates,
                    &mut seen,
                    candidate,
                    ResolutionMode::RepositoryDevelopment,
                    "current_exe",
                );
            }
        }
    }

    let manifest_dir = Path::new(env!("CARGO_MANIFEST_DIR"));
    if let Some(candidate) = find_repo_processor_from_seed(manifest_dir) {
        push_unique_candidate(
            &mut candidates,
            &mut seen,
            candidate,
            ResolutionMode::RepositoryDevelopment,
            "cargo_manifest_dir",
        );
    }

    if let Ok(resource_dir) = app.path().resource_dir() {
        if let Some(candidate) = find_repo_processor_from_seed(&resource_dir) {
            push_unique_candidate(
                &mut candidates,
                &mut seen,
                candidate,
                ResolutionMode::RepositoryDevelopment,
                "resource_dir_repo_scan",
            );
        }
    }

    candidates
}

fn build_packaged_candidates(app: &tauri::AppHandle) -> Vec<CandidatePath> {
    let mut candidates = Vec::new();
    let mut seen = HashSet::new();

    if let Ok(path) = app
        .path()
        .resolve("local-processing-service", BaseDirectory::Resource)
    {
        push_unique_candidate(
            &mut candidates,
            &mut seen,
            path,
            ResolutionMode::Packaged,
            "resource:local-processing-service",
        );
    }

    if let Ok(path) = app.path().resolve("resources", BaseDirectory::Resource) {
        let candidate = path.join("local-processing-service");
        push_unique_candidate(
            &mut candidates,
            &mut seen,
            candidate,
            ResolutionMode::Packaged,
            "resource:resources/local-processing-service",
        );
    }

    candidates
}

fn resolve_processor_working_dir(
    app: &tauri::AppHandle,
    configured_override: &str,
) -> Result<ResolvedProcessorPath, String> {
    resolve_processor_from_candidates(
        configured_override,
        build_repo_candidates(app),
        build_packaged_candidates(app),
        cfg!(debug_assertions),
    )
}

#[tauri::command]
async fn resolve_processor_path(
    app: tauri::AppHandle,
    configured_override: String,
) -> Result<String, String> {
    resolve_processor_working_dir(&app, &configured_override)
        .map(|r| r.path.to_string_lossy().into_owned())
}

/// Spawn the local processor (2-Application/local-processing-service). Dev mode runs it
/// from source via uvicorn in the configured working directory; the env contract mirrors
/// the MAUI LocalProcessorService. A PyInstaller sidecar can replace the program later.
#[tauri::command]
async fn processor_start(
    app: tauri::AppHandle,
    state: State<'_, ProcessorState>,
    config: ProcessorStartConfig,
) -> Result<(), String> {
    if state
        .child
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())?
        .is_some()
    {
        return Ok(());
    }

    let resolved = resolve_processor_working_dir(&app, &config.working_dir)?;
    let src_dir = resolved.path.join("src");

    let port_value = if config.port == 0 {
        DEFAULT_PROCESSOR_PORT
    } else {
        config.port
    };
    let embedding_provider_endpoint = if config.embedding_provider_endpoint.trim().is_empty() {
        default_embedding_provider_endpoint()
    } else {
        config.embedding_provider_endpoint
    };
    let embedding_model = if config.embedding_model.trim().is_empty() {
        default_embedding_model()
    } else {
        config.embedding_model
    };
    let api_base_url = if config.api_base_url.trim().is_empty() {
        default_api_base_url()
    } else {
        config.api_base_url
    };

    let mut envs: HashMap<String, String> = HashMap::new();
    envs.insert("PORT".into(), port_value.to_string());
    envs.insert("PYTHONUNBUFFERED".into(), "1".into());
    envs.insert(
        "EMBEDDING_PROVIDER_ENDPOINT".into(),
        embedding_provider_endpoint,
    );
    envs.insert("EMBEDDING_MODEL".into(), embedding_model);
    envs.insert("MCR_API_BASE_URL".into(), api_base_url);
    if let Some(secret) = config.upload_job_secret {
        envs.insert("PYTHON_UPLOAD_JOB_SECRET".into(), secret);
    }

    let port = port_value.to_string();
    let command = app
        .shell()
        .command("python3")
        .args([
            "-m",
            "uvicorn",
            "main:app",
            "--host",
            "127.0.0.1",
            "--port",
            &port,
        ])
        .current_dir(src_dir)
        .envs(envs);

    let (_rx, child) = command.spawn().map_err(|e| {
        format!(
            "failed to start processor (mode={}, source={}): {e}",
            resolved.mode.as_str(),
            resolved.source
        )
    })?;
    *state
        .child
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())? = Some(child);
    *state
        .port
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())? = port_value;

    Ok(())
}

/// Gracefully drain the processor (POST /control/shutdown) then kill the child.
#[tauri::command]
async fn processor_stop(state: State<'_, ProcessorState>) -> Result<(), String> {
    let port = *state
        .port
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())?;
    if port != 0 {
        let url = format!("http://127.0.0.1:{port}/control/shutdown");
        let _ = reqwest::Client::new().post(url).send().await;
    }
    let child = state
        .child
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())?
        .take();
    if let Some(child) = child {
        let _ = child.kill();
    }
    Ok(())
}

#[tauri::command]
fn processor_running(state: State<'_, ProcessorState>) -> bool {
    state
        .child
        .lock()
        .map(|guard| guard.is_some())
        .unwrap_or(false)
}

/// Proxy an HTTP request to the local processor on 127.0.0.1. Routing requests through
/// Rust avoids browser CORS / mixed-content restrictions in the webview.
#[tauri::command]
async fn processor_request(
    state: State<'_, ProcessorState>,
    method: String,
    path: String,
    body: Option<serde_json::Value>,
    port: Option<u16>,
) -> Result<serde_json::Value, String> {
    let default_port = *state
        .port
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())?;
    let port = port.unwrap_or(default_port);
    if port == 0 {
        return Err("local processor port is not configured".into());
    }
    let url = format!("http://127.0.0.1:{port}{path}");
    let client = reqwest::Client::new();
    let req = match method.to_uppercase().as_str() {
        "GET" => client.get(url),
        "DELETE" => client.delete(url),
        "POST" => {
            let r = client.post(url);
            if let Some(b) = body {
                r.json(&b)
            } else {
                r
            }
        }
        "PUT" => {
            let r = client.put(url);
            if let Some(b) = body {
                r.json(&b)
            } else {
                r
            }
        }
        other => return Err(format!("unsupported method {other}")),
    };

    let resp = req.send().await.map_err(|e| e.to_string())?;
    let status = resp.status();
    let text = resp.text().await.map_err(|e| e.to_string())?;
    if !status.is_success() {
        return Err(format!("{status}: {text}"));
    }
    if text.trim().is_empty() {
        return Ok(serde_json::Value::Null);
    }
    serde_json::from_str(&text).map_err(|e| e.to_string())
}

// ── Auth ────────────────────────────────────────────────────────────────────

#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
struct AuthResult {
    access_token: String,
    account: String,
    expires_in: u64,
}

/// Full Entra auth-code + PKCE loopback flow.
/// 1. Generates PKCE verifier/challenge + state.
/// 2. Binds a random localhost port to catch the redirect.
/// 3. Opens the Entra authorize URL in the system browser.
/// 4. Accepts the redirect, writes a "sign-in complete" page, extracts the code.
/// 5. Exchanges the code for tokens at the token endpoint.
/// 6. Returns access_token + account name (from id_token) to the frontend.
#[tauri::command]
async fn auth_sign_in(
    app: tauri::AppHandle,
    authority: String,
    client_id: String,
    scope: String,
) -> Result<AuthResult, String> {
    use tokio::io::{AsyncReadExt, AsyncWriteExt};

    // PKCE ───────────────────────────────────────────────────────────────────
    let mut verifier_bytes = [0u8; 32];
    getrandom::getrandom(&mut verifier_bytes).map_err(|e| e.to_string())?;
    let code_verifier = URL_SAFE_NO_PAD.encode(verifier_bytes);

    let challenge_hash = Sha256::digest(code_verifier.as_bytes());
    let code_challenge = URL_SAFE_NO_PAD.encode(challenge_hash);

    let mut state_bytes = [0u8; 16];
    getrandom::getrandom(&mut state_bytes).map_err(|e| e.to_string())?;
    let state = URL_SAFE_NO_PAD.encode(state_bytes);

    // Loopback listener ──────────────────────────────────────────────────────
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0")
        .await
        .map_err(|e| format!("bind failed: {e}"))?;
    let port = listener.local_addr().map_err(|e| e.to_string())?.port();
    let redirect_uri = format!("http://localhost:{port}");

    // Auth URL ───────────────────────────────────────────────────────────────
    let full_scope = format!("{scope} openid profile");
    let auth_url = format!(
        "{authority}/oauth2/v2.0/authorize\
         ?client_id={client_id}\
         &response_type=code\
         &redirect_uri={}\
         &scope={}\
         &code_challenge={code_challenge}\
         &code_challenge_method=S256\
         &state={state}\
         &prompt=select_account",
        urlencoding::encode(&redirect_uri),
        urlencoding::encode(&full_scope),
    );

    app.opener()
        .open_url(&auth_url, None::<&str>)
        .map_err(|e| format!("open browser: {e}"))?;

    // Wait for redirect (5-minute timeout) ──────────────────────────────────
    let (code, returned_state) = tokio::time::timeout(std::time::Duration::from_secs(300), async {
        let (mut stream, _) = listener.accept().await?;
        let mut buf = vec![0u8; 4096];
        let n = stream.read(&mut buf).await?;
        let request = String::from_utf8_lossy(&buf[..n]);

        // Parse "GET /?code=...&state=... HTTP/1.1"
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

        let query = path.split_once('?').map(|(_, query)| query).unwrap_or("");
        let mut code = String::new();
        let mut st = String::new();
        for pair in query.split('&') {
            let mut kv = pair.splitn(2, '=');
            match (kv.next(), kv.next()) {
                (Some("code"), Some(v)) => {
                    code = urlencoding::decode(v).unwrap_or_default().into_owned();
                }
                (Some("state"), Some(v)) => {
                    st = urlencoding::decode(v).unwrap_or_default().into_owned();
                }
                _ => {}
            }
        }

        if code.is_empty() {
            let desc = query
                .split('&')
                .find(|p| p.starts_with("error_description="))
                .and_then(|p| p.split_once('=').map(|(_, value)| value))
                .map(|s| urlencoding::decode(s).unwrap_or_default().into_owned())
                .unwrap_or_else(|| "authentication failed".into());
            return Err(std::io::Error::other(desc));
        }

        Ok((code, st))
    })
    .await
    .map_err(|_| "sign-in timed out after 5 minutes".to_string())?
    .map_err(|e| e.to_string())?;

    if returned_state != state {
        return Err("state mismatch — possible CSRF attack".into());
    }

    // Token exchange ─────────────────────────────────────────────────────────
    let params = [
        ("grant_type", "authorization_code"),
        ("client_id", client_id.as_str()),
        ("code", code.as_str()),
        ("redirect_uri", redirect_uri.as_str()),
        ("code_verifier", code_verifier.as_str()),
        ("scope", full_scope.as_str()),
    ];

    let resp = reqwest::Client::new()
        .post(format!("{authority}/oauth2/v2.0/token"))
        .form(&params)
        .send()
        .await
        .map_err(|e| format!("token request: {e}"))?;

    let status = resp.status();
    let body: serde_json::Value = resp.json().await.map_err(|e| e.to_string())?;

    if !status.is_success() {
        let msg = body["error_description"]
            .as_str()
            .unwrap_or("token exchange failed");
        return Err(msg.to_string());
    }

    let access_token = body["access_token"]
        .as_str()
        .ok_or("missing access_token in response")?
        .to_string();
    let expires_in = body["expires_in"].as_u64().unwrap_or(3600);
    let account =
        extract_id_token_account(body["id_token"].as_str().unwrap_or("")).unwrap_or_default();

    Ok(AuthResult {
        access_token,
        account,
        expires_in,
    })
}

fn extract_id_token_account(id_token: &str) -> Option<String> {
    let payload = id_token.split('.').nth(1)?;
    let decoded = URL_SAFE_NO_PAD.decode(payload).ok()?;
    let json: serde_json::Value = serde_json::from_slice(&decoded).ok()?;
    json["preferred_username"]
        .as_str()
        .or_else(|| json["email"].as_str())
        .or_else(|| json["name"].as_str())
        .map(String::from)
}

// ── App entry ────────────────────────────────────────────────────────────────

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_store::Builder::default().build())
        .manage(ProcessorState::default())
        .invoke_handler(tauri::generate_handler![
            resolve_processor_path,
            processor_start,
            processor_stop,
            processor_running,
            processor_request,
            auth_sign_in,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use std::time::{SystemTime, UNIX_EPOCH};

    fn temp_path(label: &str) -> PathBuf {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("system clock before unix epoch")
            .as_nanos();
        std::env::temp_dir().join(format!(
            "motorcyclerag-admindesktop-{label}-{}-{nanos}",
            std::process::id()
        ))
    }

    fn create_processor_layout(root: &Path) {
        fs::create_dir_all(root.join("src")).expect("create src directory");
        fs::write(root.join("src").join("main.py"), "print('ok')\n").expect("create main.py");
    }

    #[test]
    fn validate_processor_layout_success_and_failure_cases() {
        let valid = temp_path("layout-valid");
        create_processor_layout(&valid);
        assert!(validate_processor_layout(&valid).is_ok());

        let missing_src = temp_path("layout-missing-src");
        fs::create_dir_all(&missing_src).expect("create missing_src root");
        assert!(validate_processor_layout(&missing_src)
            .expect_err("missing src should fail")
            .contains("missing src directory"));

        let missing_main = temp_path("layout-missing-main");
        fs::create_dir_all(missing_main.join("src")).expect("create missing_main src");
        assert!(validate_processor_layout(&missing_main)
            .expect_err("missing main.py should fail")
            .contains("missing src/main.py"));

        let missing_path = temp_path("layout-missing-path");
        assert!(validate_processor_layout(&missing_path)
            .expect_err("missing path should fail")
            .contains("path does not exist"));

        let file_instead_of_dir = temp_path("layout-file-path");
        fs::write(&file_instead_of_dir, "not a directory").expect("create file path");
        assert!(validate_processor_layout(&file_instead_of_dir)
            .expect_err("file path should fail")
            .contains("path is not a directory"));

        let _ = fs::remove_dir_all(&valid);
        let _ = fs::remove_dir_all(&missing_src);
        let _ = fs::remove_dir_all(&missing_main);
        let _ = fs::remove_file(&file_instead_of_dir);
    }

    #[test]
    fn looks_like_repo_root_requires_solution_and_agents_marker() {
        let repo_like = temp_path("repo-root");
        fs::create_dir_all(&repo_like).expect("create repo_like root");
        fs::write(repo_like.join("MotorcycleRAG.sln"), "").expect("create solution marker");
        fs::write(repo_like.join("AGENTS.md"), "").expect("create agents marker");
        assert!(looks_like_repo_root(&repo_like));

        let missing_agents = temp_path("repo-root-missing-agents");
        fs::create_dir_all(&missing_agents).expect("create missing_agents root");
        fs::write(missing_agents.join("MotorcycleRAG.sln"), "")
            .expect("create missing_agents solution marker");
        assert!(!looks_like_repo_root(&missing_agents));

        let _ = fs::remove_dir_all(&repo_like);
        let _ = fs::remove_dir_all(&missing_agents);
    }

    #[test]
    fn find_repo_processor_from_seed_walks_ancestors() {
        let repo_root = temp_path("ancestor-repo-root");
        fs::create_dir_all(&repo_root).expect("create repo root");
        fs::write(repo_root.join("MotorcycleRAG.sln"), "").expect("create sln marker");
        fs::write(repo_root.join("AGENTS.md"), "").expect("create agents marker");

        let processor_root = repo_root
            .join("2-Application")
            .join("local-processing-service");
        create_processor_layout(&processor_root);

        let seed = repo_root
            .join("1-Presentation")
            .join("MotorcycleRAG.AdminDesktop")
            .join("src-tauri");
        fs::create_dir_all(&seed).expect("create deep seed path");

        let found = find_repo_processor_from_seed(&seed).expect("expected processor ancestor hit");
        assert_eq!(found, processor_root);

        let _ = fs::remove_dir_all(&repo_root);
    }

    #[test]
    fn resolver_precedence_is_deterministic_between_debug_and_release_preference() {
        let base = temp_path("resolver-precedence");
        let repo_valid = base.join("repo-valid");
        let packaged_valid = base.join("packaged-valid");
        create_processor_layout(&repo_valid);
        create_processor_layout(&packaged_valid);

        let debug_preferred = resolve_processor_from_candidates(
            "",
            vec![CandidatePath {
                path: repo_valid.clone(),
                mode: ResolutionMode::RepositoryDevelopment,
                source: "repo-candidate",
            }],
            vec![CandidatePath {
                path: packaged_valid.clone(),
                mode: ResolutionMode::Packaged,
                source: "packaged-candidate",
            }],
            true,
        )
        .expect("expected resolver success in debug precedence");
        assert_eq!(debug_preferred.path, repo_valid);
        assert_eq!(debug_preferred.mode.as_str(), "repository-development");
        assert_eq!(debug_preferred.source, "repo-candidate");

        let release_preferred = resolve_processor_from_candidates(
            "",
            vec![CandidatePath {
                path: repo_valid,
                mode: ResolutionMode::RepositoryDevelopment,
                source: "repo-candidate",
            }],
            vec![CandidatePath {
                path: packaged_valid,
                mode: ResolutionMode::Packaged,
                source: "packaged-candidate",
            }],
            false,
        )
        .expect("expected resolver success in release precedence");
        assert_eq!(release_preferred.mode.as_str(), "packaged");
        assert_eq!(release_preferred.source, "packaged-candidate");

        let _ = fs::remove_dir_all(&base);
    }

    #[test]
    fn resolver_classifies_repository_assets_unavailable_when_only_repo_candidates_fail() {
        let base = temp_path("resolver-repo-classification");
        let repo_invalid = base.join("repo-invalid");
        fs::create_dir_all(&repo_invalid).expect("create repo invalid root");

        let err = match resolve_processor_from_candidates(
            "",
            vec![CandidatePath {
                path: repo_invalid,
                mode: ResolutionMode::RepositoryDevelopment,
                source: "repo-invalid",
            }],
            Vec::new(),
            true,
        ) {
            Ok(_) => panic!("expected classified repository failure"),
            Err(err) => err,
        };

        assert!(err.contains("repository-assets-unavailable"));

        let _ = fs::remove_dir_all(&base);
    }

    #[test]
    fn resolver_classification_prefers_requested_mode_when_both_candidate_types_fail() {
        let base = temp_path("resolver-mixed-failures");
        let repo_invalid = base.join("repo-invalid");
        let packaged_invalid = base.join("packaged-invalid");
        fs::create_dir_all(&repo_invalid).expect("create repo invalid root");
        fs::create_dir_all(&packaged_invalid).expect("create packaged invalid root");

        let repo_preferred_err = match resolve_processor_from_candidates(
            "",
            vec![CandidatePath {
                path: repo_invalid.clone(),
                mode: ResolutionMode::RepositoryDevelopment,
                source: "repo-invalid",
            }],
            vec![CandidatePath {
                path: packaged_invalid.clone(),
                mode: ResolutionMode::Packaged,
                source: "packaged-invalid",
            }],
            true,
        ) {
            Ok(_) => panic!("expected mixed-failure classification"),
            Err(err) => err,
        };
        assert!(repo_preferred_err.contains("repository-assets-unavailable"));

        let packaged_preferred_err = match resolve_processor_from_candidates(
            "",
            vec![CandidatePath {
                path: repo_invalid,
                mode: ResolutionMode::RepositoryDevelopment,
                source: "repo-invalid",
            }],
            vec![CandidatePath {
                path: packaged_invalid,
                mode: ResolutionMode::Packaged,
                source: "packaged-invalid",
            }],
            false,
        ) {
            Ok(_) => panic!("expected mixed-failure classification"),
            Err(err) => err,
        };
        assert!(packaged_preferred_err.contains("packaged-assets-unavailable"));

        let _ = fs::remove_dir_all(&base);
    }
}
