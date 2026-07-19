use std::fs::OpenOptions;
use std::io::Write;
use std::path::{Path, PathBuf};
use std::time::Duration;

use base64::Engine;
use rcgen::{
    BasicConstraints, CertificateParams, ExtendedKeyUsagePurpose, IsCa, KeyPair, KeyUsagePurpose,
};
use reqwest::{Client, Method, RequestBuilder, StatusCode, Url};
use tempfile::TempDir;

const CONTROL_TOKEN_ENV: &str = "MCR_LOCAL_PROCESSOR_CONTROL_TOKEN";
const LOCAL_PROCESSOR_HOST: &str = "127.0.0.1";

/// Ephemeral, single-launch TLS and authentication material for the local processor.
///
/// The CA is deliberately trusted only by `client`; it is never installed in the OS trust store.
pub struct ProcessorTransport {
    session: ProcessorSession,
    temp_dir: TempDir,
    certificate_path: PathBuf,
    key_path: PathBuf,
}

#[derive(Clone)]
pub struct ProcessorSession {
    client: Client,
    control_token: String,
}

impl ProcessorTransport {
    pub fn create() -> Result<Self, String> {
        let temp_dir = tempfile::Builder::new()
            .prefix("motorcyclerag-processor-")
            .tempdir()
            .map_err(|error| {
                format!("failed to create private processor certificate directory: {error}")
            })?;

        let mut token_bytes = [0_u8; 32];
        getrandom::getrandom(&mut token_bytes)
            .map_err(|error| format!("failed to generate processor control token: {error}"))?;
        let control_token = base64::engine::general_purpose::URL_SAFE_NO_PAD.encode(token_bytes);

        let mut ca_params = CertificateParams::new(Vec::<String>::new())
            .map_err(|error| format!("failed to create processor CA parameters: {error}"))?;
        ca_params.is_ca = IsCa::Ca(BasicConstraints::Unconstrained);
        ca_params.key_usages = vec![
            KeyUsagePurpose::KeyCertSign,
            KeyUsagePurpose::DigitalSignature,
            KeyUsagePurpose::CrlSign,
        ];
        let ca_key = KeyPair::generate()
            .map_err(|error| format!("failed to generate processor CA key: {error}"))?;
        let ca = ca_params
            .self_signed(&ca_key)
            .map_err(|error| format!("failed to generate processor CA certificate: {error}"))?;

        let mut leaf_params = CertificateParams::new(vec![
            "localhost".to_string(),
            LOCAL_PROCESSOR_HOST.to_string(),
        ])
        .map_err(|error| {
            format!("failed to create processor leaf certificate parameters: {error}")
        })?;
        leaf_params.extended_key_usages = vec![ExtendedKeyUsagePurpose::ServerAuth];
        let leaf_key = KeyPair::generate()
            .map_err(|error| format!("failed to generate processor leaf key: {error}"))?;
        let leaf = leaf_params
            .signed_by(&leaf_key, &ca, &ca_key)
            .map_err(|error| format!("failed to sign processor leaf certificate: {error}"))?;

        let certificate_path = temp_dir.path().join("processor-cert.pem");
        let key_path = temp_dir.path().join("processor-key.pem");
        write_private_file(&certificate_path, leaf.pem().as_bytes())?;
        write_private_file(&key_path, leaf_key.serialize_pem().as_bytes())?;

        let ca_certificate = reqwest::Certificate::from_pem(ca.pem().as_bytes())
            .map_err(|error| format!("failed to load processor CA certificate: {error}"))?;
        let client = Client::builder()
            .use_rustls_tls()
            .https_only(true)
            .redirect(reqwest::redirect::Policy::none())
            .tls_built_in_root_certs(false)
            .add_root_certificate(ca_certificate)
            .connect_timeout(Duration::from_secs(5))
            .build()
            .map_err(|error| format!("failed to construct processor HTTPS client: {error}"))?;

        Ok(Self {
            session: ProcessorSession {
                client,
                control_token,
            },
            temp_dir,
            certificate_path,
            key_path,
        })
    }

    pub fn control_token_env(&self) -> (&'static str, &str) {
        (CONTROL_TOKEN_ENV, &self.session.control_token)
    }

    pub fn uvicorn_tls_args(&self) -> Vec<String> {
        vec![
            "--ssl-keyfile".to_string(),
            self.key_path.to_string_lossy().into_owned(),
            "--ssl-certfile".to_string(),
            self.certificate_path.to_string_lossy().into_owned(),
        ]
    }

    pub async fn is_ready(&self, port: u16) -> bool {
        self.session.is_ready(port).await
    }

    pub fn session(&self) -> ProcessorSession {
        self.session.clone()
    }

    /// Removes the certificate/key directory synchronously after the child has stopped.
    pub fn cleanup(self) -> Result<(), String> {
        self.temp_dir
            .close()
            .map_err(|error| format!("failed to remove processor certificate directory: {error}"))
    }
}

impl ProcessorSession {
    pub async fn is_ready(&self, port: u16) -> bool {
        match self.request("GET", port, "/health") {
            Ok(request) => match request.send().await {
                Ok(response) => {
                    response.status().is_success()
                        || response.status() == StatusCode::SERVICE_UNAVAILABLE
                }
                Err(_) => false,
            },
            Err(_) => false,
        }
    }

    pub async fn shutdown(&self, port: u16) -> Result<(), String> {
        let response = self
            .request("POST", port, "/control/shutdown")?
            .timeout(Duration::from_secs(5))
            .send()
            .await
            .map_err(|error| format!("failed to request processor shutdown: {error}"))?;
        if response.status().is_success() {
            Ok(())
        } else {
            Err(format!(
                "processor shutdown request failed with HTTP {}",
                response.status()
            ))
        }
    }

    pub fn request(&self, method: &str, port: u16, path: &str) -> Result<RequestBuilder, String> {
        let method = parse_method(method)?;
        let endpoint = local_processor_endpoint(port, path)?;
        Ok(self
            .client
            .request(method, endpoint)
            .bearer_auth(&self.control_token))
    }
}

fn write_private_file(path: &Path, contents: &[u8]) -> Result<(), String> {
    let mut options = OpenOptions::new();
    options.write(true).create_new(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.mode(0o600);
    }

    let mut file = options
        .open(path)
        .map_err(|error| format!("failed to create private processor certificate file: {error}"))?;
    file.write_all(contents)
        .map_err(|error| format!("failed to write processor certificate file: {error}"))?;
    file.sync_all()
        .map_err(|error| format!("failed to flush processor certificate file: {error}"))?;
    Ok(())
}

fn parse_method(method: &str) -> Result<Method, String> {
    match method.to_ascii_uppercase().as_str() {
        "GET" => Ok(Method::GET),
        "POST" => Ok(Method::POST),
        "PUT" => Ok(Method::PUT),
        "DELETE" => Ok(Method::DELETE),
        _ => Err("unsupported processor request method".to_string()),
    }
}

/// Builds the only authority that the WebView may reach through this command.
pub fn local_processor_endpoint(port: u16, path: &str) -> Result<Url, String> {
    if port == 0 {
        return Err("local processor port is not configured".to_string());
    }
    if !path.starts_with('/')
        || path.starts_with("//")
        || path.contains('\\')
        || path.contains('#')
        || path.chars().any(char::is_control)
    {
        return Err(
            "processor path must be an origin-relative path without an authority or fragment"
                .to_string(),
        );
    }

    let endpoint = Url::parse(&format!("https://{LOCAL_PROCESSOR_HOST}:{port}{path}"))
        .map_err(|error| format!("invalid processor path: {error}"))?;
    if endpoint.scheme() != "https"
        || endpoint.host_str() != Some(LOCAL_PROCESSOR_HOST)
        || endpoint.port() != Some(port)
        || !endpoint.username().is_empty()
        || endpoint.password().is_some()
        || endpoint.fragment().is_some()
    {
        return Err("processor path changed the required local HTTPS authority".to_string());
    }
    Ok(endpoint)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::child_stderr_capture::{append_processor_stderr_detail, ChildStderrCapture};
    use crate::redact_processor_stderr::redact_processor_stderr;
    use std::fs;
    use std::io::{BufRead, BufReader};
    use std::net::{Ipv4Addr, SocketAddr, TcpListener, TcpStream};
    #[cfg(unix)]
    use std::os::unix::fs::PermissionsExt;
    use std::path::{Path, PathBuf};
    use std::process::{Child, Command, Stdio};
    #[cfg(unix)]
    use tauri::Manager;

    struct RealProcessorBridge {
        child: Child,
        transport: Option<ProcessorTransport>,
        input_dir: TempDir,
    }

    impl RealProcessorBridge {
        fn transport(&self) -> &ProcessorTransport {
            self.transport
                .as_ref()
                .expect("transport remains available")
        }
    }

    impl Drop for RealProcessorBridge {
        fn drop(&mut self) {
            let _ = self.child.kill();
            let _ = self.child.wait();
            if let Some(transport) = self.transport.take() {
                let _ = transport.cleanup();
            }
        }
    }

    const TLS_AUTH_SERVER: &str = r#"
import http.server
import ssl
import sys

certificate_path = sys.argv[1]
key_path = sys.argv[2]
expected_authorization = sys.argv[3]
shutdown_status = int(sys.argv[4])

class Handler(http.server.BaseHTTPRequestHandler):
    def _respond(self, status, body):
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.headers.get("Authorization") != expected_authorization:
            self._respond(401, b'{"detail":"unauthorized"}')
            return
        self._respond(200, b'{"status":"ok"}')

    def do_POST(self):
        if self.headers.get("Authorization") != expected_authorization:
            self._respond(401, b'{"detail":"unauthorized"}')
            return
        if self.path == "/control/shutdown":
            self._respond(shutdown_status, b'{"status":"shutdown"}')
            return
        self._respond(404, b'{"detail":"not found"}')

    def log_message(self, format, *args):
        pass

server = http.server.ThreadingHTTPServer(("0.0.0.0", 0), Handler)
context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
context.load_cert_chain(certificate_path, key_path)
server.socket = context.wrap_socket(server.socket, server_side=True)
print(f"READY {server.server_address[1]}", flush=True)
server.serve_forever()
"#;

    struct TlsAuthServer {
        child: Child,
        port: u16,
    }

    #[cfg(unix)]
    const LIFECYCLE_TEST_PROCESSOR: &str = r#"#!/usr/bin/env python3
import http.server
import os
import ssl
import sys

def option(name):
    return sys.argv[sys.argv.index(name) + 1]

port = int(option("--port"))
certificate_path = option("--ssl-certfile")
key_path = option("--ssl-keyfile")
expected_authorization = "Bearer " + os.environ["MCR_LOCAL_PROCESSOR_CONTROL_TOKEN"]

class Handler(http.server.BaseHTTPRequestHandler):
    def _respond(self, status, body):
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def _authorized(self):
        return self.headers.get("Authorization") == expected_authorization

    def do_GET(self):
        if not self._authorized():
            self._respond(401, b'{"detail":"unauthorized"}')
            return
        if self.path == "/health":
            self._respond(200, b'{"status":"ok"}')
            return
        self._respond(404, b'{"detail":"not found"}')

    def do_POST(self):
        if not self._authorized():
            self._respond(401, b'{"detail":"unauthorized"}')
            return
        if self.path == "/control/shutdown":
            self._respond(500, b'{"detail":"forced shutdown failure"}')
            return
        self._respond(404, b'{"detail":"not found"}')

    def log_message(self, format, *args):
        pass

server = http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler)
context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
context.load_cert_chain(certificate_path, key_path)
server.socket = context.wrap_socket(server.socket, server_side=True)
server.serve_forever()
"#;

    impl TlsAuthServer {
        fn start(transport: &ProcessorTransport, shutdown_status: u16) -> Self {
            let tls_args = transport.uvicorn_tls_args();
            let key_path = tls_args
                .get(1)
                .expect("TLS key path should be supplied to uvicorn");
            let certificate_path = tls_args
                .get(3)
                .expect("TLS certificate path should be supplied to uvicorn");
            let (_, control_token) = transport.control_token_env();

            Self::start_with_tls_material(
                Path::new(certificate_path),
                Path::new(key_path),
                &format!("Bearer {control_token}"),
                shutdown_status,
            )
        }

        fn start_with_tls_material(
            certificate_path: &Path,
            key_path: &Path,
            expected_authorization: &str,
            shutdown_status: u16,
        ) -> Self {
            let mut child = Command::new("python3")
                .args([
                    "-c",
                    TLS_AUTH_SERVER,
                    certificate_path
                        .to_str()
                        .expect("certificate path is UTF-8"),
                    key_path.to_str().expect("key path is UTF-8"),
                    expected_authorization,
                    &shutdown_status.to_string(),
                ])
                .stdin(Stdio::null())
                .stdout(Stdio::piped())
                .stderr(Stdio::piped())
                .spawn()
                .expect("python3 must be available to host the local TLS test processor");

            let stdout = child.stdout.take().expect("TLS server stdout");
            let mut ready_line = String::new();
            BufReader::new(stdout)
                .read_line(&mut ready_line)
                .expect("TLS server readiness line");
            let port = ready_line
                .strip_prefix("READY ")
                .and_then(|value| value.trim().parse::<u16>().ok())
                .expect("TLS server must report the listening port");

            Self { child, port }
        }

        fn force_stop(&mut self) {
            self.child.kill().expect("force-stop TLS test processor");
            self.child
                .wait()
                .expect("reap force-stopped TLS test processor");
        }
    }

    impl Drop for TlsAuthServer {
        fn drop(&mut self) {
            if self.child.try_wait().ok().flatten().is_none() {
                let _ = self.child.kill();
                let _ = self.child.wait();
            }
        }
    }

    fn available_loopback_port() -> u16 {
        TcpListener::bind((Ipv4Addr::LOCALHOST, 0))
            .expect("reserve a loopback port")
            .local_addr()
            .expect("reserved port address")
            .port()
    }

    #[cfg(unix)]
    fn create_lifecycle_test_processor_layout(root: &Path) {
        let source_directory = root.join("src");
        let python_directory = root.join(".venv").join("bin");
        fs::create_dir_all(&source_directory).expect("create lifecycle test source directory");
        fs::create_dir_all(&python_directory).expect("create lifecycle test Python directory");
        fs::write(
            source_directory.join("main.py"),
            "# lifecycle test server\n",
        )
        .expect("create lifecycle test main module");

        let python = python_directory.join("python");
        fs::write(&python, LIFECYCLE_TEST_PROCESSOR).expect("create lifecycle test Python shim");
        let mut permissions = fs::metadata(&python)
            .expect("read lifecycle test Python shim metadata")
            .permissions();
        permissions.set_mode(0o700);
        fs::set_permissions(&python, permissions)
            .expect("make lifecycle test Python shim executable");
    }

    #[cfg(unix)]
    fn lifecycle_test_app() -> tauri::App<tauri::test::MockRuntime> {
        tauri::test::mock_builder()
            .manage(crate::ProcessorState::default())
            .build(tauri::test::mock_context(tauri::test::noop_assets()))
            .expect("build Tauri lifecycle test app")
    }

    #[cfg(unix)]
    const LIFECYCLE_FAILING_STDERR_PROCESSOR: &str = r#"#!/usr/bin/env python3
import os
import sys

mode = os.environ.get("LIFECYCLE_STDERR_MODE", "discovery")
if mode == "discovery":
    print(
        "Embedding discovery failed: connection refused while probing LM Studio",
        file=sys.stderr,
    )
elif mode == "secrets":
    token = os.environ.get("MCR_LOCAL_PROCESSOR_CONTROL_TOKEN", "")
    upload = os.environ.get("PYTHON_UPLOAD_JOB_SECRET", "")
    print(f"MCR_LOCAL_PROCESSOR_CONTROL_TOKEN={token}", file=sys.stderr)
    print(f"Authorization: Bearer {token}", file=sys.stderr)
    print(f"PYTHON_UPLOAD_JOB_SECRET={upload}", file=sys.stderr)
elif mode == "empty":
    pass
else:
    print(f"unknown LIFECYCLE_STDERR_MODE={mode}", file=sys.stderr)
sys.exit(1)
"#;

    #[cfg(unix)]
    fn create_lifecycle_failing_processor_layout(root: &Path) {
        let source_directory = root.join("src");
        let python_directory = root.join(".venv").join("bin");
        fs::create_dir_all(&source_directory).expect("create failing lifecycle source directory");
        fs::create_dir_all(&python_directory).expect("create failing lifecycle Python directory");
        fs::write(
            source_directory.join("main.py"),
            "# failing lifecycle test shim\n",
        )
        .expect("create failing lifecycle main module");

        let python = python_directory.join("python");
        fs::write(&python, LIFECYCLE_FAILING_STDERR_PROCESSOR)
            .expect("create failing lifecycle Python shim");
        let mut permissions = fs::metadata(&python)
            .expect("read failing lifecycle Python shim metadata")
            .permissions();
        permissions.set_mode(0o700);
        fs::set_permissions(&python, permissions)
            .expect("make failing lifecycle Python shim executable");
    }

    /// Mirrors `processor_start`'s piped-stderr capture / redact / append path for lifecycle tests.
    #[cfg(unix)]
    async fn wait_for_lifecycle_processor_with_stderr(
        transport: &ProcessorTransport,
        port: u16,
        child: &mut Child,
        timeout_secs: u64,
        upload_job_secret: Option<&str>,
    ) -> Result<(), String> {
        let stderr_capture = ChildStderrCapture::start(child.stderr.take());
        if let Err(err) =
            crate::wait_for_processor_listening(transport, port, child, timeout_secs).await
        {
            let _ = child.kill();
            let _ = child.wait();
            let stderr_raw = stderr_capture.finish();
            let (_, control_token) = transport.control_token_env();
            let mut secrets: Vec<&str> = vec![control_token];
            if let Some(secret) = upload_job_secret.filter(|value| !value.is_empty()) {
                secrets.push(secret);
            }
            let stderr_safe = redact_processor_stderr(&stderr_raw, &secrets);
            return Err(append_processor_stderr_detail(err, &stderr_safe));
        }

        stderr_capture.detach();
        Ok(())
    }

    #[cfg(unix)]
    async fn start_lifecycle_test_processor(
        app: &tauri::App<tauri::test::MockRuntime>,
        port: u16,
        working_dir: &Path,
    ) -> Result<(), String> {
        let transport = ProcessorTransport::create()?;
        let (control_token_name, control_token) = transport.control_token_env();
        let launch = crate::resolve_python_launch(working_dir, port, &transport);
        let mut child = Command::new(&launch.program)
            .args(&launch.args)
            .current_dir(&launch.working_dir)
            .env(control_token_name, control_token)
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::piped())
            .spawn()
            .map_err(|error| format!("failed to start lifecycle test processor: {error}"))?;

        wait_for_lifecycle_processor_with_stderr(&transport, port, &mut child, 5, None).await?;

        let state = app.state::<crate::ProcessorState>();
        *state
            .child
            .lock()
            .map_err(|_| "processor state lock poisoned".to_string())? = Some(child);
        *state
            .port
            .lock()
            .map_err(|_| "processor state lock poisoned".to_string())? = port;
        *state
            .transport
            .lock()
            .map_err(|_| "processor state lock poisoned".to_string())? = Some(transport);

        Ok(())
    }

    #[cfg(unix)]
    async fn start_failing_lifecycle_processor(
        port: u16,
        working_dir: &Path,
        stderr_mode: &str,
        upload_job_secret: Option<&str>,
    ) -> Result<(), String> {
        let transport = ProcessorTransport::create()?;
        let (control_token_name, control_token) = transport.control_token_env();
        let launch = crate::resolve_python_launch(working_dir, port, &transport);
        let mut command = Command::new(&launch.program);
        command
            .args(&launch.args)
            .current_dir(&launch.working_dir)
            .env(control_token_name, control_token)
            .env("LIFECYCLE_STDERR_MODE", stderr_mode)
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::piped());
        if let Some(secret) = upload_job_secret {
            command.env("PYTHON_UPLOAD_JOB_SECRET", secret);
        }

        let mut child = command
            .spawn()
            .map_err(|error| format!("failed to start failing lifecycle processor: {error}"))?;

        let result = wait_for_lifecycle_processor_with_stderr(
            &transport,
            port,
            &mut child,
            5,
            upload_job_secret,
        )
        .await;
        let _ = transport.cleanup();
        result
    }

    fn processor_root() -> PathBuf {
        PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .ancestors()
            .nth(3)
            .expect("repository root")
            .join("2-Application/local-processing-service")
    }

    fn start_real_processor(port: u16) -> RealProcessorBridge {
        let transport = ProcessorTransport::create().expect("create processor transport");
        let processor_root = processor_root();
        assert!(processor_root.join("src/main.py").is_file());
        let launch = crate::resolve_python_launch(&processor_root, port, &transport);
        let host_index = launch
            .args
            .iter()
            .position(|argument| argument == "--host")
            .expect("Rust launch includes an explicit processor host");
        assert_eq!(
            launch.args.get(host_index + 1).map(String::as_str),
            Some(LOCAL_PROCESSOR_HOST),
            "the real FastAPI process must bind only to loopback"
        );
        let input_dir = tempfile::tempdir().expect("create processor input directory");
        let (token_name, token) = transport.control_token_env();

        let child = Command::new(&launch.program)
            .args(&launch.args)
            .current_dir(&launch.working_dir)
            .env(token_name, token)
            .env("WATCH_FOLDER_DISABLED", "1")
            .env("LOCAL_PROCESSOR_INPUT_DIR", input_dir.path())
            .env("EMBEDDING_PROVIDER_ENDPOINT", "")
            .env("EMBEDDING_BACKEND", "ollama")
            .env("MCR_API_BASE_URL", "https://motorag.api.palfery.com")
            .env("PYTHON_UPLOAD_JOB_SECRET", "")
            .env("GRAPH_EXTRACTION_ENDPOINT", "http://127.0.0.1:1234/v1")
            .env("GRAPH_EXTRACTION_MODEL", "test-graph-model")
            .env(
                "AZURE_STORAGE_CONNECTION_STRING",
                "UseDevelopmentStorage=true",
            )
            .env_remove("AZURE_STORAGE_ACCOUNT_URL")
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn()
            .expect("launch real FastAPI processor");

        RealProcessorBridge {
            child,
            transport: Some(transport),
            input_dir,
        }
    }

    #[test]
    fn endpoint_rejects_non_local_scheme_and_authority_forms() {
        for path in [
            "http://127.0.0.1:8100/health",
            "https://example.test/health",
            "//example.test/health",
            "/\\example.test/health",
            "/health#fragment",
        ] {
            assert!(local_processor_endpoint(8100, path).is_err(), "{path}");
        }
    }

    #[test]
    fn endpoint_is_https_and_fixed_to_loopback() {
        let endpoint = local_processor_endpoint(8100, "/health?verbose=true").expect("endpoint");
        assert_eq!(endpoint.scheme(), "https");
        assert_eq!(endpoint.host_str(), Some(LOCAL_PROCESSOR_HOST));
        assert_eq!(endpoint.port(), Some(8100));
    }

    #[test]
    fn transport_uses_a_unique_bearer_token_and_private_files() {
        let first = ProcessorTransport::create().expect("first transport");
        let second = ProcessorTransport::create().expect("second transport");
        assert_ne!(first.session.control_token, second.session.control_token);
        assert!(first.certificate_path.is_file());
        assert!(first.key_path.is_file());
        let request = first
            .session()
            .request("GET", 8100, "/health")
            .expect("authenticated request")
            .build()
            .expect("request build");
        assert_eq!(request.url().scheme(), "https");
        assert_eq!(request.url().host_str(), Some(LOCAL_PROCESSOR_HOST));
        assert_eq!(
            request
                .headers()
                .get(reqwest::header::AUTHORIZATION)
                .expect("authorization header")
                .to_str()
                .expect("authorization header value"),
            format!("Bearer {}", first.session.control_token)
        );
        first.cleanup().expect("first cleanup");
        second.cleanup().expect("second cleanup");
    }

    #[test]
    fn cleanup_removes_private_certificate_directory() {
        let transport = ProcessorTransport::create().expect("transport");
        let directory = transport.temp_dir.path().to_path_buf();
        transport.cleanup().expect("cleanup");
        assert!(!directory.exists());
    }

    #[test]
    fn local_tls_rejects_a_hostname_not_present_in_the_leaf_certificate_san() {
        let certificate_dir = tempfile::tempdir().expect("create TLS test directory");
        let ca_key = KeyPair::generate().expect("generate CA key");
        let mut ca_params = CertificateParams::new(Vec::<String>::new()).expect("CA parameters");
        ca_params.is_ca = IsCa::Ca(BasicConstraints::Unconstrained);
        ca_params.key_usages = vec![KeyUsagePurpose::KeyCertSign];
        let ca = ca_params.self_signed(&ca_key).expect("self-sign CA");

        let leaf_key = KeyPair::generate().expect("generate leaf key");
        let mut leaf_params = CertificateParams::new(vec![LOCAL_PROCESSOR_HOST.to_string()])
            .expect("leaf parameters");
        leaf_params.extended_key_usages = vec![ExtendedKeyUsagePurpose::ServerAuth];
        let leaf = leaf_params
            .signed_by(&leaf_key, &ca, &ca_key)
            .expect("sign leaf certificate");
        let certificate_path = certificate_dir.path().join("leaf.pem");
        let key_path = certificate_dir.path().join("leaf-key.pem");
        std::fs::write(&certificate_path, leaf.pem()).expect("write leaf certificate");
        std::fs::write(&key_path, leaf_key.serialize_pem()).expect("write leaf key");

        let mut server = TlsAuthServer::start_with_tls_material(
            &certificate_path,
            &key_path,
            "Bearer test",
            200,
        );
        let loopback = SocketAddr::from((Ipv4Addr::LOCALHOST, server.port));

        TcpStream::connect_timeout(&loopback, Duration::from_secs(1))
            .expect("test TLS server should be reachable on loopback");

        let ca_certificate =
            reqwest::Certificate::from_pem(ca.pem().as_bytes()).expect("load generated CA");
        let client = Client::builder()
            .use_rustls_tls()
            .tls_built_in_root_certs(false)
            .add_root_certificate(ca_certificate)
            .resolve("not-in-leaf-san.test", loopback)
            .build()
            .expect("build test TLS client");

        let result = tauri::async_runtime::block_on(async {
            client
                .get(format!(
                    "https://not-in-leaf-san.test:{}/health",
                    server.port
                ))
                .bearer_auth("test")
                .send()
                .await
        });

        assert!(
            result.is_err(),
            "certificate SAN validation must reject a reachable hostname outside the leaf SAN"
        );

        server.force_stop();
    }

    #[cfg(unix)]
    #[test]
    fn processor_start_when_child_exits_with_discovery_stderr_includes_that_text() {
        let root = tempfile::tempdir().expect("create failing lifecycle processor root");
        create_lifecycle_failing_processor_layout(root.path());
        let port = available_loopback_port();

        let error = tauri::async_runtime::block_on(start_failing_lifecycle_processor(
            port,
            root.path(),
            "discovery",
            None,
        ))
        .expect_err("failing shim must surface early exit");

        assert!(
            error.contains("processor process exited early"),
            "error must still report early exit: {error}"
        );
        assert!(
            error.contains("Embedding discovery failed"),
            "error must include child stderr text, not only exit status: {error}"
        );
        assert!(
            !error.contains("No stderr was captured"),
            "stderr must have been captured: {error}"
        );
    }

    #[cfg(unix)]
    #[test]
    fn processor_start_when_stderr_contains_control_token_and_upload_secret_redacts_them() {
        let root = tempfile::tempdir().expect("create failing lifecycle processor root");
        create_lifecycle_failing_processor_layout(root.path());
        let port = available_loopback_port();
        let upload_secret = "upload-job-secret-value-for-redaction-test";

        let error = tauri::async_runtime::block_on(start_failing_lifecycle_processor(
            port,
            root.path(),
            "secrets",
            Some(upload_secret),
        ))
        .expect_err("failing shim must surface early exit");

        assert!(
            error.contains("[REDACTED]"),
            "secrets must be redacted in the returned error: {error}"
        );
        assert!(
            !error.contains(upload_secret),
            "upload secret must not leak in the returned error: {error}"
        );
        assert!(
            !error.contains("Bearer ") || error.contains("Bearer [REDACTED]"),
            "Bearer token values must be redacted: {error}"
        );
        // Labeled assignment redaction must work even when the exact token is unknown to callers
        // that only inspect the returned error string (no secrets list in the assertion path).
        assert!(
            error.contains("MCR_LOCAL_PROCESSOR_CONTROL_TOKEN=[REDACTED]")
                || error.contains("MCR_LOCAL_PROCESSOR_CONTROL_TOKEN:[REDACTED]"),
            "labeled control token must be redacted without relying on the raw value: {error}"
        );
    }

    #[cfg(unix)]
    #[test]
    fn processor_start_when_stderr_is_empty_points_at_local_processor_log() {
        let root = tempfile::tempdir().expect("create failing lifecycle processor root");
        create_lifecycle_failing_processor_layout(root.path());
        let port = available_loopback_port();

        let error = tauri::async_runtime::block_on(start_failing_lifecycle_processor(
            port,
            root.path(),
            "empty",
            None,
        ))
        .expect_err("failing shim must surface early exit");

        assert!(
            error.contains("No stderr was captured"),
            "empty stderr must add the log-path pointer: {error}"
        );
        assert!(
            error.contains("logs/local-processor.log"),
            "empty stderr guidance must mention the processor log path: {error}"
        );
    }

    #[cfg(unix)]
    #[test]
    fn processor_start_when_piped_stderr_is_used_happy_path_still_succeeds() {
        let root = tempfile::tempdir().expect("create lifecycle test processor root");
        create_lifecycle_test_processor_layout(root.path());
        let app = lifecycle_test_app();
        let port = available_loopback_port();

        tauri::async_runtime::block_on(start_lifecycle_test_processor(&app, port, root.path()))
            .expect("piped stderr + detach must not hang a healthy lifecycle processor");

        assert!(crate::processor_running(app.state()));
        let health = tauri::async_runtime::block_on(crate::processor_request(
            app.state(),
            "GET".to_string(),
            "/health".to_string(),
            None,
            Some(port),
        ))
        .expect("healthy lifecycle processor must answer authenticated health");
        assert_eq!(health["status"], "ok");

        let _ = tauri::async_runtime::block_on(crate::processor_stop(app.state(), Some(port)));
    }

    #[cfg(unix)]
    #[test]
    fn processor_commands_when_shutdown_returns_http_500_force_termination_clean_tls_and_retain_error(
    ) {
        let root = tempfile::tempdir().expect("create lifecycle test processor root");
        create_lifecycle_test_processor_layout(root.path());
        let app = lifecycle_test_app();
        let port = available_loopback_port();

        tauri::async_runtime::block_on(start_lifecycle_test_processor(&app, port, root.path()))
            .expect("the lifecycle start helper must launch the controlled TLS processor");

        let health = tauri::async_runtime::block_on(crate::processor_request(
            app.state(),
            "GET".to_string(),
            "/health".to_string(),
            None,
            Some(port),
        ))
        .expect("processor_request must proxy an authenticated health request");
        assert_eq!(health["status"], "ok");

        let certificate_directory = app
            .state::<crate::ProcessorState>()
            .transport
            .lock()
            .expect("processor transport lock")
            .as_ref()
            .expect("processor transport")
            .temp_dir
            .path()
            .to_path_buf();

        let stop_error =
            tauri::async_runtime::block_on(crate::processor_stop(app.state(), Some(port)))
                .expect_err("shutdown HTTP failure must be returned after forced termination");

        assert!(stop_error.contains("processor shutdown request failed with HTTP 500"));
        assert!(!crate::processor_running(app.state()));
        assert!(
            !tauri::async_runtime::block_on(crate::processor_port_is_in_use(port)),
            "processor_stop must force-terminate the child that remained listening after shutdown failed"
        );
        assert!(
            !certificate_directory.exists(),
            "processor_stop must remove the ephemeral TLS certificate directory even after shutdown fails"
        );
    }

    #[cfg(unix)]
    #[test]
    fn processor_stop_when_transport_lock_is_poisoned_cleans_tls_before_returning_lock_error() {
        let app = lifecycle_test_app();
        let port = available_loopback_port();
        let transport = ProcessorTransport::create().expect("create processor transport");
        let certificate_directory = transport.temp_dir.path().to_path_buf();
        let state = app.state::<crate::ProcessorState>();

        *state.port.lock().expect("processor port lock") = port;
        *state.transport.lock().expect("processor transport lock") = Some(transport);

        std::thread::scope(|scope| {
            let transport_lock = &state.transport;
            let result = scope
                .spawn(move || {
                    let _guard = transport_lock.lock().expect("processor transport lock");
                    panic!("poison processor transport lock");
                })
                .join();
            assert!(result.is_err(), "the test must poison the transport lock");
        });

        let stop_error =
            tauri::async_runtime::block_on(crate::processor_stop(app.state(), Some(port)))
                .expect_err("a poisoned transport lock must be reported");

        assert!(stop_error.contains("processor state lock poisoned"));
        assert!(
            !certificate_directory.exists(),
            "RED contract: TLS material must be cleaned even when the transport lock is poisoned"
        );
    }

    /// Ensures a linger PID is force-killed if a RED assertion panics before stop cleans it up.
    #[cfg(unix)]
    struct KillPidOnDrop(Option<u32>);

    #[cfg(unix)]
    impl Drop for KillPidOnDrop {
        fn drop(&mut self) {
            if let Some(pid) = self.0.take() {
                let _ = Command::new("kill").args(["-9", &pid.to_string()]).status();
            }
        }
    }

    #[cfg(unix)]
    fn spawn_linger_child() -> Child {
        Command::new("sleep")
            .arg("60")
            .stdin(Stdio::null())
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .spawn()
            .expect("spawn linger child for processor_stop poison tests")
    }

    #[cfg(unix)]
    fn unix_process_is_alive(pid: u32) -> bool {
        Command::new("kill")
            .args(["-0", &pid.to_string()])
            .status()
            .map(|status| status.success())
            .unwrap_or(false)
    }

    #[cfg(unix)]
    fn poison_mutex<T: Send>(mutex: &std::sync::Mutex<T>) {
        std::thread::scope(|scope| {
            let result = scope
                .spawn(|| {
                    let _guard = mutex.lock().expect("lock before poison");
                    panic!("poison processor state lock");
                })
                .join();
            assert!(result.is_err(), "the test must poison the mutex");
        });
    }

    #[cfg(unix)]
    #[test]
    fn processor_stop_when_port_lock_is_poisoned_cleans_tls_before_returning_lock_error() {
        let app = lifecycle_test_app();
        let port = available_loopback_port();
        let transport = ProcessorTransport::create().expect("create processor transport");
        let certificate_directory = transport.temp_dir.path().to_path_buf();
        let state = app.state::<crate::ProcessorState>();

        *state.port.lock().expect("processor port lock") = port;
        *state.transport.lock().expect("processor transport lock") = Some(transport);

        poison_mutex(&state.port);

        let stop_error =
            tauri::async_runtime::block_on(crate::processor_stop(app.state(), Some(port)))
                .expect_err("a poisoned port lock must be reported");

        assert!(
            stop_error.contains("processor state lock poisoned"),
            "stop must surface the port lock poison: {stop_error}"
        );
        assert!(
            !certificate_directory.exists(),
            "RED contract: TLS material must be cleaned even when the port lock is poisoned"
        );
    }

    #[cfg(unix)]
    #[test]
    fn processor_stop_when_child_lock_is_poisoned_recovers_into_inner_and_kills_child() {
        let app = lifecycle_test_app();
        let port = available_loopback_port();
        let transport = ProcessorTransport::create().expect("create processor transport");
        let certificate_directory = transport.temp_dir.path().to_path_buf();
        let state = app.state::<crate::ProcessorState>();

        let child = spawn_linger_child();
        let child_pid = child.id();
        let mut kill_guard = KillPidOnDrop(Some(child_pid));

        *state.port.lock().expect("processor port lock") = port;
        *state.transport.lock().expect("processor transport lock") = Some(transport);
        *state.child.lock().expect("processor child lock") = Some(child);

        poison_mutex(&state.child);

        let stop_error =
            tauri::async_runtime::block_on(crate::processor_stop(app.state(), Some(port)))
                .expect_err("a poisoned child lock must be reported");

        assert!(
            stop_error.contains("processor state lock poisoned"),
            "stop must surface the child lock poison: {stop_error}"
        );
        assert!(
            !certificate_directory.exists(),
            "TLS material must still be cleaned when the child lock is poisoned"
        );

        let remaining = state
            .child
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner());
        assert!(
            remaining.is_none(),
            "RED contract: stop must into_inner a poisoned child lock and take the child handle"
        );
        assert!(
            !unix_process_is_alive(child_pid),
            "RED contract: stop must terminate the child recovered from a poisoned lock"
        );

        kill_guard.0 = None;
    }

    #[cfg(unix)]
    #[test]
    fn processor_stop_when_transport_missing_still_terminates_child() {
        let app = lifecycle_test_app();
        let port = available_loopback_port();
        let state = app.state::<crate::ProcessorState>();

        let child = spawn_linger_child();
        let child_pid = child.id();
        let mut kill_guard = KillPidOnDrop(Some(child_pid));

        *state.port.lock().expect("processor port lock") = port;
        // Intentionally leave transport as None while a child handle is present.
        *state.child.lock().expect("processor child lock") = Some(child);

        let stop_result =
            tauri::async_runtime::block_on(crate::processor_stop(app.state(), Some(port)));
        assert!(
            stop_result.is_err(),
            "missing transport should still be reported after cleanup attempts"
        );
        let stop_error = stop_result.expect_err("missing transport error");
        assert!(
            stop_error.contains("local processor transport is not configured"),
            "stop must report missing transport: {stop_error}"
        );

        let remaining = state.child.lock().expect("processor child lock");
        assert!(
            remaining.is_none(),
            "RED contract: missing transport must not skip child termination via early ?"
        );
        assert!(
            !unix_process_is_alive(child_pid),
            "RED contract: stop must terminate a child even when transport is not configured"
        );

        kill_guard.0 = None;
    }

    #[test]
    fn real_local_bridge_uses_authenticated_https_and_rejects_untrusted_access() {
        let port = available_loopback_port();
        let mut bridge = start_real_processor(port);
        let session = bridge.transport().session();
        let transport = bridge
            .transport
            .as_ref()
            .expect("transport remains available");
        let child = &mut bridge.child;

        tauri::async_runtime::block_on(crate::wait_for_processor_listening(
            transport, port, child, 20,
        ))
        .expect("authenticated Rust readiness check reaches the real FastAPI service");

        let health = tauri::async_runtime::block_on(
            session
                .request("GET", port, "/health")
                .expect("build authenticated health request")
                .send(),
        )
        .expect("authenticated HTTPS health request succeeds");
        assert!(health.status().is_success() || health.status() == StatusCode::SERVICE_UNAVAILABLE);

        let missing_token = tauri::async_runtime::block_on(
            session
                .client
                .get(local_processor_endpoint(port, "/health").expect("health endpoint"))
                .send(),
        )
        .expect("trusted TLS connection reaches FastAPI");
        assert_eq!(missing_token.status(), StatusCode::UNAUTHORIZED);

        let wrong_token = tauri::async_runtime::block_on(
            session
                .client
                .get(local_processor_endpoint(port, "/health").expect("health endpoint"))
                .bearer_auth("wrong-token")
                .send(),
        )
        .expect("trusted TLS connection reaches FastAPI");
        assert_eq!(wrong_token.status(), StatusCode::UNAUTHORIZED);

        let plain_http = tauri::async_runtime::block_on(
            reqwest::Client::new()
                .get(format!("http://{LOCAL_PROCESSOR_HOST}:{port}/health"))
                .send(),
        );
        assert!(
            plain_http.is_err(),
            "the TLS listener must reject direct HTTP"
        );

        let unknown_ca = tauri::async_runtime::block_on(
            reqwest::Client::new()
                .get(format!("https://{LOCAL_PROCESSOR_HOST}:{port}/health"))
                .send(),
        );
        assert!(
            unknown_ca.is_err(),
            "the ephemeral CA must not be OS-trusted"
        );

        assert!(
            session
                .request("GET", port, "https://example.test/health")
                .is_err(),
            "the Rust bridge must reject external-interface authorities"
        );

        tauri::async_runtime::block_on(session.shutdown(port))
            .expect("authenticated HTTPS shutdown succeeds");
        assert!(bridge.input_dir.path().exists());
    }
}
