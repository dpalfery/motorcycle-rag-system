use std::collections::{HashMap, HashSet};
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::sync::Mutex;

use serde::Deserialize;
use tauri::path::BaseDirectory;
use tauri::{Manager, State};

mod auth;
pub mod local_ingestion_queue;
mod processor_transport;
use auth::AuthConfig;
use local_ingestion_queue::{
    queue_local_ingestion_work_item_to_watch_folder, LocalIngestionWorkItemRequest,
    LocalIngestionWorkItemResult,
};
use processor_transport::ProcessorTransport;

/// Supervises the local Python processor child process and remembers its port.
#[derive(Default)]
struct ProcessorState {
    child: Mutex<Option<Child>>,
    port: Mutex<u16>,
    transport: Mutex<Option<ProcessorTransport>>,
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
    tokenizer_model_path: String,
    #[serde(default)]
    upload_job_secret: Option<String>,
    #[serde(default = "default_graph_extraction_endpoint")]
    graph_extraction_endpoint: String,
    graph_extraction_model: String,
    #[serde(default = "default_api_base_url")]
    api_base_url: String,
    #[serde(default = "default_azure_storage_account_url")]
    azure_storage_account_url: String,
}

const DEFAULT_PROCESSOR_PORT: u16 = 8100;

fn default_processor_port() -> u16 {
    DEFAULT_PROCESSOR_PORT
}

fn default_embedding_provider_endpoint() -> String {
    "http://localhost:1234/v1".to_string()
}

fn default_embedding_model() -> String {
    "qwen3-embedding".to_string()
}

fn default_api_base_url() -> String {
    "https://motorag.api.palfery.com".to_string()
}

fn default_azure_storage_account_url() -> String {
    "https://mcrragdevst0125c2ea3c.blob.core.windows.net/".to_string()
}

fn default_graph_extraction_endpoint() -> String {
    "http://localhost:1234/v1".to_string()
}

fn local_ingestion_watch_folder(app: &tauri::AppHandle) -> Result<PathBuf, String> {
    app.path()
        .app_data_dir()
        .map(|path| path.join("local-ingestion-watch"))
        .map_err(|e| format!("failed to resolve app data directory: {e}"))
}

#[tauri::command]
fn pick_local_ingestion_file() -> Result<Option<String>, String> {
    let picked = rfd::FileDialog::new()
        .set_title("Select a file for local processing")
        .add_filter("Supported files", &["pdf", "csv"])
        .pick_file();

    Ok(picked.map(|path| path.to_string_lossy().into_owned()))
}

#[tauri::command]
fn queue_local_ingestion_work_item(
    app: tauri::AppHandle,
    request: LocalIngestionWorkItemRequest,
) -> Result<LocalIngestionWorkItemResult, String> {
    let watch_folder = local_ingestion_watch_folder(&app)?;
    queue_local_ingestion_work_item_to_watch_folder(&watch_folder, request)
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

struct PythonLaunch {
    program: String,
    args: Vec<String>,
    working_dir: PathBuf,
}

fn processor_venv_python(processor_root: &Path) -> Option<PathBuf> {
    #[cfg(windows)]
    let venv_python = processor_root
        .join(".venv")
        .join("Scripts")
        .join("python.exe");
    #[cfg(not(windows))]
    let venv_python = processor_root.join(".venv").join("bin").join("python");

    venv_python.is_file().then_some(venv_python)
}

fn resolve_python_launch(
    processor_root: &Path,
    port: u16,
    transport: &ProcessorTransport,
) -> PythonLaunch {
    let src_dir = processor_root.join("src");
    let host = "127.0.0.1";
    let port_value = port.to_string();
    let mut uvicorn_args = vec![
        "main:app".to_string(),
        "--host".to_string(),
        host.to_string(),
        "--port".to_string(),
        port_value,
    ];
    uvicorn_args.extend(transport.uvicorn_tls_args());

    if let Some(venv_python) = processor_venv_python(processor_root) {
        let mut args = vec!["-m".to_string(), "uvicorn".to_string()];
        args.append(&mut uvicorn_args);
        return PythonLaunch {
            program: venv_python.to_string_lossy().into_owned(),
            args,
            working_dir: src_dir,
        };
    }

    if processor_root.join("pyproject.toml").is_file() {
        let mut args = vec!["run".to_string(), "uvicorn".to_string()];
        args.append(&mut uvicorn_args);
        return PythonLaunch {
            program: "poetry".to_string(),
            args,
            working_dir: processor_root.to_path_buf(),
        };
    }

    let mut args = vec!["-m".to_string(), "uvicorn".to_string()];
    args.append(&mut uvicorn_args);
    PythonLaunch {
        program: "python3".to_string(),
        args,
        working_dir: src_dir,
    }
}

async fn processor_port_is_in_use(port: u16) -> bool {
    if port == 0 {
        return false;
    }

    tokio::net::TcpStream::connect(("127.0.0.1", port))
        .await
        .is_ok()
}

async fn wait_for_processor_listening(
    transport: &ProcessorTransport,
    port: u16,
    child: &mut Child,
    timeout_secs: u64,
) -> Result<(), String> {
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(timeout_secs.max(1));

    while std::time::Instant::now() < deadline {
        if transport.is_ready(port).await {
            return Ok(());
        }
        if let Some(reason) = child_exit_description(child) {
            return Err(reason);
        }
        tokio::time::sleep(std::time::Duration::from_millis(500)).await;
    }

    if let Some(reason) = child_exit_description(child) {
        return Err(reason);
    }

    Err(format!(
        "local processor did not start listening on port {port} within {timeout_secs}s"
    ))
}

fn child_exit_description(child: &mut Child) -> Option<String> {
    child
        .try_wait()
        .ok()
        .flatten()
        .map(|status| format!("processor process exited early with {status}"))
}

fn clear_child_process(child: &mut Option<Child>) {
    if let Some(mut existing) = child.take() {
        let _ = existing.kill();
        let _ = existing.wait();
    }
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
    let port_value = if config.port == 0 {
        DEFAULT_PROCESSOR_PORT
    } else {
        config.port
    };

    if processor_port_is_in_use(port_value).await {
        *state
            .port
            .lock()
            .map_err(|_| "processor state lock poisoned".to_string())? = port_value;
        return Err(format!(
            "local processor is already listening on port {port_value}; stop it before starting with new settings"
        ));
    }

    if config.graph_extraction_model.trim().is_empty() {
        return Err(
            "graphExtractionModel is required in Settings; set the LM Studio chat model before starting the processor"
                .to_string(),
        );
    }

    {
        let mut child_guard = state
            .child
            .lock()
            .map_err(|_| "processor state lock poisoned".to_string())?;
        clear_child_process(&mut child_guard);
    }
    if let Some(previous_transport) = state
        .transport
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())?
        .take()
    {
        previous_transport.cleanup()?;
    }

    let resolved = resolve_processor_working_dir(&app, &config.working_dir)?;
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
    let local_input_dir = local_ingestion_watch_folder(&app)?
        .to_string_lossy()
        .into_owned();
    envs.insert("PORT".into(), port_value.to_string());
    envs.insert("PYTHONUNBUFFERED".into(), "1".into());
    envs.insert("WATCH_FOLDER".into(), local_input_dir.clone());
    envs.insert("LOCAL_PROCESSOR_INPUT_DIR".into(), local_input_dir);
    envs.insert(
        "EMBEDDING_PROVIDER_ENDPOINT".into(),
        embedding_provider_endpoint,
    );
    envs.insert("EMBEDDING_MODEL".into(), embedding_model);
    if !config.tokenizer_model_path.trim().is_empty() {
        envs.insert("TOKENIZER_MODEL_PATH".into(), config.tokenizer_model_path);
    }
    envs.insert("MCR_API_BASE_URL".into(), api_base_url);
    let azure_storage_account_url = if config.azure_storage_account_url.trim().is_empty() {
        default_azure_storage_account_url()
    } else {
        config.azure_storage_account_url
    };
    envs.insert(
        "AZURE_STORAGE_ACCOUNT_URL".into(),
        azure_storage_account_url,
    );
    if let Some(secret) = config.upload_job_secret {
        envs.insert("PYTHON_UPLOAD_JOB_SECRET".into(), secret);
    }
    envs.insert(
        "GRAPH_EXTRACTION_ENDPOINT".into(),
        config.graph_extraction_endpoint,
    );
    envs.insert(
        "GRAPH_EXTRACTION_MODEL".into(),
        config.graph_extraction_model,
    );

    let transport = ProcessorTransport::create()?;
    let (control_token_name, control_token) = transport.control_token_env();
    envs.insert(control_token_name.into(), control_token.to_string());

    let launch = resolve_python_launch(&resolved.path, port_value, &transport);
    let mut command = Command::new(&launch.program);
    command
        .args(&launch.args)
        .current_dir(&launch.working_dir)
        .envs(envs)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null());

    let mut child = match command.spawn() {
        Ok(child) => child,
        Err(error) => {
            let start_error = format!(
                "failed to start processor using {} (mode={}, source={}): {error}",
                launch.program,
                resolved.mode.as_str(),
                resolved.source
            );
            return match transport.cleanup() {
                Ok(()) => Err(start_error),
                Err(cleanup_error) => Err(format!("{start_error}; {cleanup_error}")),
            };
        }
    };

    if let Err(err) = wait_for_processor_listening(&transport, port_value, &mut child, 45).await {
        let _ = child.kill();
        let _ = child.wait();
        return match transport.cleanup() {
            Ok(()) => Err(err),
            Err(cleanup_error) => Err(format!("{err}; {cleanup_error}")),
        };
    }

    *state
        .child
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())? = Some(child);
    *state
        .port
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())? = port_value;
    *state
        .transport
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())? = Some(transport);

    Ok(())
}

/// Force-stop any process listening on the given TCP port (orphaned uvicorn, etc.).
fn force_kill_listeners_on_port(port: u16) -> Result<(), String> {
    #[cfg(unix)]
    {
        let port_arg = format!("tcp:{port}");
        let output = Command::new("lsof")
            .args(["-ti", &port_arg])
            .output()
            .map_err(|e| format!("failed to run lsof for port {port}: {e}"))?;

        let pids = String::from_utf8_lossy(&output.stdout);
        let mut killed = false;
        for pid in pids.lines().map(str::trim).filter(|line| !line.is_empty()) {
            let status = Command::new("kill")
                .args(["-9", pid])
                .status()
                .map_err(|e| format!("failed to kill pid {pid} on port {port}: {e}"))?;
            if status.success() {
                killed = true;
            }
        }

        if killed {
            return Ok(());
        }
    }

    #[cfg(windows)]
    {
        let _ = port;
        return Err("force stop by port is not implemented on Windows".into());
    }

    #[cfg(unix)]
    Ok(())
}

async fn wait_for_processor_stopped(port: u16, timeout_secs: u64) -> bool {
    let deadline = std::time::Instant::now() + std::time::Duration::from_secs(timeout_secs.max(1));

    while std::time::Instant::now() < deadline {
        if !processor_port_is_in_use(port).await {
            return true;
        }
        tokio::time::sleep(std::time::Duration::from_millis(250)).await;
    }

    !processor_port_is_in_use(port).await
}

/// Gracefully drain the processor (POST /control/shutdown) then kill the child.
#[tauri::command]
async fn processor_stop(state: State<'_, ProcessorState>, port: Option<u16>) -> Result<(), String> {
    let mut errors = Vec::new();

    // Recover a poisoned port lock so stop can continue into TLS cleanup / child termination.
    // into_inner() yields the guarded value; the poison error is retained and returned below.
    let stored_port = match state.port.lock() {
        Ok(guard) => *guard,
        Err(poisoned) => {
            errors.push("processor state lock poisoned".to_string());
            *poisoned.into_inner()
        }
    };
    if stored_port == 0 {
        return Err("local processor is not running".to_string());
    }
    if port.is_some_and(|requested| requested != stored_port) {
        return Err(
            "requested processor port does not match the active local processor".to_string(),
        );
    }
    let port_to_stop = stored_port;

    // Recover a poisoned transport lock so ephemeral TLS material can still be cleaned up.
    // Missing transport is collected as an error — never early-return before child termination.
    let transport = match state.transport.lock() {
        Ok(mut guard) => guard.take(),
        Err(poisoned) => {
            errors.push("processor state lock poisoned".to_string());
            poisoned.into_inner().take()
        }
    };

    if let Some(ref transport) = transport {
        if let Err(error) = transport.session().shutdown(port_to_stop).await {
            errors.push(error);
        }
    } else {
        errors.push("local processor transport is not configured".to_string());
    }

    // Recover a poisoned child lock so the hosted process is still taken and terminated.
    match state.child.lock() {
        Ok(mut child_guard) => {
            if let Some(mut child) = child_guard.take() {
                let _ = child.kill();
                let _ = child.wait();
            }
        }
        Err(poisoned) => {
            errors.push("processor state lock poisoned".to_string());
            let mut child_guard = poisoned.into_inner();
            if let Some(mut child) = child_guard.take() {
                let _ = child.kill();
                let _ = child.wait();
            }
        }
    }

    if !wait_for_processor_stopped(port_to_stop, 8).await {
        if let Err(error) = force_kill_listeners_on_port(port_to_stop) {
            errors.push(error);
        }
        if !wait_for_processor_stopped(port_to_stop, 3).await {
            errors.push(format!(
                "local processor is still listening on port {port_to_stop} after stop was requested"
            ));
        }
    }

    match state.port.lock() {
        Ok(mut stored_port) => *stored_port = 0,
        Err(poisoned) => {
            *poisoned.into_inner() = 0;
            errors.push("processor state lock poisoned".to_string());
        }
    }

    // The transport owns the per-launch control token and private TLS material. It must be
    // consumed even when termination fails so no credentials or certificate files survive a
    // failed stop attempt.
    if let Some(transport) = transport {
        if let Err(error) = transport.cleanup() {
            errors.push(error);
        }
    }

    if errors.is_empty() {
        Ok(())
    } else {
        Err(errors.join("; "))
    }
}

#[tauri::command]
fn processor_running(state: State<'_, ProcessorState>) -> bool {
    state
        .child
        .lock()
        .map(|guard| guard.is_some())
        .unwrap_or(false)
}

#[tauri::command]
async fn processor_is_listening(
    port: Option<u16>,
    state: State<'_, ProcessorState>,
) -> Result<bool, String> {
    let configured_port = *state
        .port
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())?;
    if configured_port == 0 {
        return Ok(false);
    }
    if port.is_some_and(|requested| requested != configured_port) {
        return Err(
            "requested processor port does not match the active local processor".to_string(),
        );
    }
    let session = state
        .transport
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())?
        .as_ref()
        .ok_or_else(|| "local processor transport is not configured".to_string())?
        .session();
    Ok(session.is_ready(configured_port).await)
}

/// Proxy an authenticated HTTPS request to the active local processor. Routing requests through
/// Rust avoids browser CORS / mixed-content restrictions in the webview.
#[tauri::command]
async fn processor_request(
    state: State<'_, ProcessorState>,
    method: String,
    path: String,
    body: Option<serde_json::Value>,
    port: Option<u16>,
) -> Result<serde_json::Value, String> {
    let configured_port = *state
        .port
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())?;
    if configured_port == 0 {
        return Err("local processor port is not configured".into());
    }
    if port.is_some_and(|requested| requested != configured_port) {
        return Err(
            "requested processor port does not match the active local processor".to_string(),
        );
    }
    let session = state
        .transport
        .lock()
        .map_err(|_| "processor state lock poisoned".to_string())?
        .as_ref()
        .ok_or_else(|| "local processor transport is not configured".to_string())?
        .session();
    let req = match method.to_uppercase().as_str() {
        "GET" | "DELETE" => session.request(&method, configured_port, &path)?,
        "POST" => {
            let r = session.request(&method, configured_port, &path)?;
            if let Some(b) = body {
                r.json(&b)
            } else {
                r
            }
        }
        "PUT" => {
            let r = session.request(&method, configured_port, &path)?;
            if let Some(b) = body {
                r.json(&b)
            } else {
                r
            }
        }
        _ => return Err("unsupported processor request method".to_string()),
    };

    let resp = req.send().await.map_err(|e| e.to_string())?;
    let status = resp.status();
    let text = resp.text().await.map_err(|e| e.to_string())?;
    if !(status.is_success()
        || method.eq_ignore_ascii_case("GET")
            && path.starts_with("/health")
            && status == reqwest::StatusCode::SERVICE_UNAVAILABLE)
    {
        return Err(format!("{status}: {text}"));
    }
    if text.trim().is_empty() {
        return Ok(serde_json::Value::Null);
    }
    serde_json::from_str(&text).map_err(|e| e.to_string())
}

// ── Auth ────────────────────────────────────────────────────────────────────

/// Read auth config (authority, client_id, scope) from the Tauri config store.
///
/// The TS frontend persists the full operator config as a nested object under
/// the `appConfig` key (see `src/lib/config.ts`, `CONFIG_KEY = "appConfig"`).
/// This reads that nested object and falls back to defaults if the store, the
/// key, or any individual field is missing.
fn read_auth_config(app: &tauri::AppHandle) -> (String, String, String) {
    use tauri_plugin_store::StoreExt;

    let store = app.store("config.json").ok();
    let config = store.as_ref().and_then(|s| s.get("appConfig"));

    let get_str = |key: &str, default: &str| -> String {
        config
            .as_ref()
            .and_then(|c| c.get(key))
            .and_then(|v| v.as_str())
            .unwrap_or(default)
            .to_string()
    };

    let authority = get_str(
        "authAuthority",
        "https://login.microsoftonline.com/0f8f8a52-f135-43af-af88-e0b54ca9ff91",
    );
    let client_id = get_str("authClientId", "a86e8458-4482-4bb6-808a-28d65b2668ef");
    let scope = get_str("authScope", "api://motorcyclerag-api/admin");

    (authority, client_id, scope)
}

/// Sign-in command wrapper. Reads auth config from the Tauri config store
/// (matching `auth_refresh_token` / `auth_restore_session`) and delegates to
/// `auth::sign_in`.
///
/// When `profileDirectory` is `Some`, Chrome is launched with that profile from
/// inside `auth::sign_in`. When `None`, the default browser is opened.
///
/// IPC contract:
///   invoke("auth_sign_in", { profileDirectory: string | null })
///     -> AuthSession { accessToken, account, expiresAt }
#[tauri::command]
async fn auth_sign_in(
    app: tauri::AppHandle,
    profile_directory: Option<String>,
) -> Result<auth::AuthSession, String> {
    let (authority, client_id, scope) = read_auth_config(&app);
    let config = AuthConfig {
        authority,
        client_id,
        scope,
    };
    auth::sign_in(&config, profile_directory).await
}

/// List Chrome user profiles discovered on disk, for the sign-in profile picker.
///
/// Returns `{ profiles, error }`. On I/O/parse failure `profiles` is `[]` and
/// `error` is a diagnostic string — never a silent sole fake Default (D2).
#[tauri::command]
fn auth_list_chrome_profiles() -> auth::ChromeProfilesResult {
    auth::list_chrome_profiles()
}

/// Refresh-token command. Reads auth config from the Tauri store, then exchanges
/// the stored refresh token for a new access token via Entra.
#[tauri::command]
async fn auth_refresh_token(app: tauri::AppHandle) -> Result<auth::AuthSession, String> {
    let (authority, client_id, scope) = read_auth_config(&app);
    let config = AuthConfig {
        authority,
        client_id,
        scope,
    };
    auth::refresh(&config).await
}

/// Restore a previously-persisted session from the OS keyring, refreshing the
/// access token if it is expired but a refresh token is available. Returns
/// `Ok(None)` when no valid session can be restored.
#[tauri::command]
async fn auth_restore_session(app: tauri::AppHandle) -> Result<Option<auth::AuthSession>, String> {
    let (authority, client_id, scope) = read_auth_config(&app);
    let config = AuthConfig {
        authority,
        client_id,
        scope,
    };
    auth::restore(&config).await
}

/// Sign-out command. Deletes the persisted session tokens from the OS keyring.
#[tauri::command]
fn auth_sign_out() -> Result<(), String> {
    auth::sign_out()
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
            pick_local_ingestion_file,
            resolve_processor_path,
            processor_start,
            processor_stop,
            processor_running,
            processor_is_listening,
            processor_request,
            queue_local_ingestion_work_item,
            auth_sign_in,
            auth_sign_out,
            auth_refresh_token,
            auth_restore_session,
            auth_list_chrome_profiles,
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
    fn resolve_python_launch_prefers_project_venv() {
        let base = temp_path("python-launch-venv");
        create_processor_layout(&base);
        let venv_bin = base.join(".venv").join("bin");
        fs::create_dir_all(&venv_bin).expect("create venv bin directory");
        let venv_python = venv_bin.join("python");
        fs::write(&venv_python, "#!/bin/sh\n").expect("create venv python stub");

        let transport = ProcessorTransport::create().expect("create transport");
        let launch = resolve_python_launch(&base, 8100, &transport);
        assert_eq!(launch.program, venv_python.to_string_lossy());
        assert_eq!(launch.args[0..3], ["-m", "uvicorn", "main:app"]);
        assert!(launch.args.contains(&"--ssl-keyfile".to_string()));
        assert!(launch.args.contains(&"--ssl-certfile".to_string()));
        assert_eq!(launch.working_dir, base.join("src"));

        let _ = fs::remove_dir_all(&base);
        transport.cleanup().expect("cleanup transport");
    }

    #[test]
    fn resolve_python_launch_falls_back_to_python3_without_venv() {
        let base = temp_path("python-launch-system");
        create_processor_layout(&base);

        let transport = ProcessorTransport::create().expect("create transport");
        let launch = resolve_python_launch(&base, 8100, &transport);
        assert_eq!(launch.program, "python3");
        assert_eq!(launch.args[0..3], ["-m", "uvicorn", "main:app"]);
        assert!(launch.args.contains(&"--ssl-keyfile".to_string()));
        assert!(launch.args.contains(&"--ssl-certfile".to_string()));
        assert_eq!(launch.working_dir, base.join("src"));

        let _ = fs::remove_dir_all(&base);
        transport.cleanup().expect("cleanup transport");
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
