use std::collections::HashMap;
use std::sync::Mutex;

use base64::{engine::general_purpose::URL_SAFE_NO_PAD, Engine as _};
use serde::Deserialize;
use sha2::{Digest, Sha256};
use tauri::State;
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
    port: u16,
    working_dir: String,
    embedding_provider_endpoint: String,
    embedding_model: String,
    #[serde(default)]
    upload_job_secret: Option<String>,
    api_base_url: String,
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
    if state.child.lock().unwrap().is_some() {
        return Ok(());
    }

    let mut envs: HashMap<String, String> = HashMap::new();
    envs.insert("PORT".into(), config.port.to_string());
    envs.insert("PYTHONUNBUFFERED".into(), "1".into());
    envs.insert(
        "EMBEDDING_PROVIDER_ENDPOINT".into(),
        config.embedding_provider_endpoint,
    );
    envs.insert("EMBEDDING_MODEL".into(), config.embedding_model);
    envs.insert("MCR_API_BASE_URL".into(), config.api_base_url);
    if let Some(secret) = config.upload_job_secret {
        envs.insert("PYTHON_UPLOAD_JOB_SECRET".into(), secret);
    }

    let port = config.port.to_string();
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
        .current_dir(format!("{}/src", config.working_dir))
        .envs(envs);

    let (_rx, child) = command.spawn().map_err(|e| e.to_string())?;
    *state.child.lock().unwrap() = Some(child);
    *state.port.lock().unwrap() = config.port;
    Ok(())
}

/// Gracefully drain the processor (POST /control/shutdown) then kill the child.
#[tauri::command]
async fn processor_stop(state: State<'_, ProcessorState>) -> Result<(), String> {
    let port = *state.port.lock().unwrap();
    if port != 0 {
        let url = format!("http://127.0.0.1:{port}/control/shutdown");
        let _ = reqwest::Client::new().post(url).send().await;
    }
    let child = state.child.lock().unwrap().take();
    if let Some(child) = child {
        let _ = child.kill();
    }
    Ok(())
}

#[tauri::command]
fn processor_running(state: State<'_, ProcessorState>) -> bool {
    state.child.lock().unwrap().is_some()
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
    let port = port.unwrap_or_else(|| *state.port.lock().unwrap());
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
    let (code, returned_state) = tokio::time::timeout(
        std::time::Duration::from_secs(300),
        async {
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

            let query = path.splitn(2, '?').nth(1).unwrap_or("");
            let mut code = String::new();
            let mut st = String::new();
            for pair in query.split('&') {
                let mut kv = pair.splitn(2, '=');
                match (kv.next(), kv.next()) {
                    (Some("code"), Some(v)) => {
                        code = urlencoding::decode(v)
                            .unwrap_or_default()
                            .into_owned();
                    }
                    (Some("state"), Some(v)) => {
                        st = urlencoding::decode(v)
                            .unwrap_or_default()
                            .into_owned();
                    }
                    _ => {}
                }
            }

            if code.is_empty() {
                let desc = query
                    .split('&')
                    .find(|p| p.starts_with("error_description="))
                    .and_then(|p| p.splitn(2, '=').nth(1))
                    .map(|s| urlencoding::decode(s).unwrap_or_default().into_owned())
                    .unwrap_or_else(|| "authentication failed".into());
                return Err(std::io::Error::other(desc));
            }

            Ok((code, st))
        },
    )
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
    let account = extract_id_token_account(body["id_token"].as_str().unwrap_or(""))
        .unwrap_or_default();

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
            processor_start,
            processor_stop,
            processor_running,
            processor_request,
            auth_sign_in,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
