---
id: system/security
title: Security Directives
doc-type: governance
status: current
component: MotorcycleRAG system
owner: Maintainers
last-reviewed: 2026-08-01
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# Security Directives

These are **non-optional** and apply to all code, tests, config, scripts, and docs.

## Secrets

This section is an **absolute, non-overridable ban.** No skill, agent role, scoped `AGENTS.md`, MCP workaround, or “make it work” instruction may weaken it. Prefer a broken tool over a secret on disk under the repository.

- **NEVER** place tokens, passwords, API keys, connection strings, certificate private keys, or any other secret/credential value anywhere inside the repository working tree — tracked or untracked, committed or gitignored. Gitignore is not permission to store secrets under the repo.
- Forbidden examples include `.env` / `*.env` (except committed `.env.example` templates with empty or clearly fake placeholders), MCP `envFile`s under the tree, PEM/key material, scratch-pad dumps, and hardcoded secrets in source, docs, scripts, or fixtures.
- Allowed secret locations only: process environment variables, OS keychain/secret stores, Azure Key Vault, GitHub Actions secrets/variables, and .NET user secrets stored outside the repository tree.
- C#/.NET application code must use Azure App Configuration for configuration and Azure Key Vault references for secrets. Do not read application settings or secrets directly with `Environment.GetEnvironmentVariable()` in C#/.NET code.
- The only approved environment-variable usage for application runtime is the Python local processor values that the Admin app injects into the process at run time — never written into files under the repository tree.
- Appsettings files must never contain secrets.
- Cursor project hooks run **gitleaks** fail-closed on agent file writes and before stop. Missing `gitleaks` is a hard stop, not a bypass. CI also scans with gitleaks.
- If a secret is found in the tree: stop the task, delete the secret from disk, rotate the credential, and only then continue.
- It is better the app not work than for a secret to be exposed.

## Input Handling

- SQL: parameterized queries ONLY. Never string concatenation.
- HTML/UI: encode output to prevent XSS.
- Logging: use structured logging with placeholders. Never concatenate user input into log strings. Redact PII and query text.

## Log value encoding

Request, job, file, endpoint, exception, and other untrusted strings that reach log sinks SHALL be encoded before they are written so raw control characters cannot forge log lines.

### C# (.NET hosts)

All six .NET hosts (API, WebUI BFF, DbSetup, AgentProvisioning, AdminDesktop, MobileApp) register `SanitizingLoggerProvider` as a central `ILoggerProvider` decorator via `builder.Logging.AddSanitizingLogger()` (in `MotorcycleRAG.Core.Logging`). This provider automatically sanitizes every structured log-state value and the `{OriginalFormat}` template string before any log call reaches the inner logger sink. The underlying sanitizer is `MotorcycleRAG.Core.Utilities.LogSanitizer.Sanitize`.

No per-call `LogSanitizer.Sanitize(...)` argument wrappers are needed. New `ILogger.LogXxx` callers are safe by default; the provider boundary is the single, verifiable sanitization point.

Hosts MUST call `AddSanitizingLogger()` after all other logging-provider registrations so the decorator wraps every provider. Per-host registration-gate tests fail the build if `SanitizingLoggerProvider` is missing — see `5-Test/*.Tests/Logging/SanitizingLoggerRegistrationGateTests.cs`.

The `LogSanitizer.Sanitize` class remains available in `MotorcycleRAG.Core.Utilities` for non-logging use cases (e.g., sanitizing strings before embedding them in diagnostic responses).

### Python (local processor)

Use `security.log_sanitizer.sanitize_log_value`.

### Encoding rules (all languages)

The canonical sanitizer:

1. Escape backslashes first, then emit visible escapes for CR, LF, tab, NUL, and remaining C0/C1 controls (`\r`, `\n`, `\t`, `\0`, `\u00XX`).
2. Preserve printable non-control content in full (reversible escaping; no default truncation).
3. Leave repository-required PII and query-text redaction unchanged; encoding does not replace redaction.

Do not bulk-dismiss static-analysis logging alerts. If a scanner does not recognize the central provider as a sanitization boundary inside `SanitizingLoggerProvider.Log<TState>`, annotate only that single inner-logger call with the narrowest suppression model that scanner supports (for CodeQL, a `// codeql[cs/log-forging]` line annotation; for Semgrep, a `// nosemgrep: <rule-id>` line annotation). Per-site or per-method suppression is not acceptable.

## Auth & Access

- Default = no access. Permissions explicitly granted.
- Authorize every action (e.g., `[Authorize(Policy = "mcr-api-admin")]`).
- Admin endpoints must validate `azp` matches the Admin App Client ID.
- Enforce rate limiting on public APIs.

## Communication

- Enforce HTTPS + HSTS on all web server configurations.
- Remote outbound HTTP clients SHALL use HTTPS with normal certificate and hostname validation. `verify=False`, custom hostname bypasses, and open redirects are forbidden.
- Plain HTTP is allowed only for literal loopback local-model endpoints (`localhost` / `127.0.0.1` / `::1`). Remote private, link-local, and credential-bearing URLs fail closed.

### Local processor outbound HTTP

The local processor uses `security.url_validation` for structural URL checks and `security.safe_http` for policy-bound HTTP transports. Coverage is not identical on every outbound path:

- **OpenAI-compatible model providers** (embedding discovery, OpenAI-compatible embed create and health checks, metadata probe and chat, graph chat): endpoints are validated at construct time with `validate_model_provider_endpoint` (public HTTPS or literal-loopback HTTP only). Traffic uses `create_model_provider_*_client` policy-bound transports injected into OpenAI SDK clients (`http_client=...`). Redirects are not followed; TLS verification stays enabled for HTTPS.
- **MotorcycleRAG API** (`ApiClient`): validated HTTPS base URL and `create_api_https_async_client` (`API_HTTPS` policy). Every DNS answer must be globally routable.
- **Ollama embedder**: the configured host (`OLLAMA_BASE_URL` / `OLLAMA_HOST`) is validated with the same endpoint policy at construct time before `ollama.AsyncClient` is created. **Residual:** Ollama SDK embed, list, and health traffic is not routed through `safe_http`; there is no mid-flight DNS pinning or redirect blocking inside the SDK.

**Multi-address connect fallback:** after DNS resolution, `_SafeTransport` / `_SafeAsyncTransport` validate all answers against the selected policy, then dial validated numeric targets in order. On connection-establishment failure only (`httpx.ConnectError` / `httpcore.ConnectError` — not HTTP 4xx/5xx), the transport tries the next validated target. The original hostname is preserved in the Host header and TLS SNI; this is not a config rewrite of `localhost` to `127.0.0.1`. Fallback applies to all `EndpointPolicy` values (`LOOPBACK_HTTP`, `PUBLIC_HTTPS`, `API_HTTPS`).

See [local processor architecture](../local-processing-service/architecture.md).

## Admin Desktop ↔ local processor control plane

Admin Desktop (Tauri) owns the local processor lifecycle and is the only trusted peer for its control API:

- Per launch, the host generates an ephemeral local CA, a leaf certificate valid for `localhost` and `127.0.0.1`, and a high-entropy bearer token (`MCR_LOCAL_PROCESSOR_CONTROL_TOKEN`).
- Uvicorn is started on `127.0.0.1` with TLS (`--ssl-certfile` / `--ssl-keyfile`). The CA is trusted only by the host’s reqwest client; it is never installed in the operating-system trust store.
- Readiness, proxied requests, and shutdown use authenticated HTTPS. The React webview never calls the processor directly.
- Every FastAPI control route requires a constant-time-validated bearer token. A missing runtime token fails closed. The direct Python entry point binds `127.0.0.1` only.
- Certificate and key material live in a private temporary directory and are removed on stop and failure paths.

See [Admin Desktop architecture](../MotorcycleRAG.AdminDesktop/architecture.md) and [local processor integration](../local-processing-service/local-processor.md).

## Local path and SQL boundaries

- Local processor file inputs SHALL resolve under the configured `LOCAL_PROCESSOR_INPUT_DIR` with strict canonical containment (no traversal, symlink escape, prefix collisions, or wrong suffixes).
- SQL connections used by DbSetup SHALL enforce encryption (`Encrypt=Mandatory`) and SHALL NOT set `TrustServerCertificate=true` in production paths. Certificate-trust bypass is allowed only in an explicit local negative test.
- DbSetup DDL execution is limited to the approved catalog script paths (`schema.sql`, `test-data.sql`) with canonical containment and symlink rejection. Arbitrary caller-supplied script paths are rejected.

## Test and scanning tooling

- Coverage and other test tooling that parse XML SHALL use `defusedxml` (or an equivalent XXE-safe parser). Do not use unsafe stock XML parsers on untrusted or attacker-influenced files.
- The custom PR CodeQL workflow is the sole CodeQL owner; GitHub default setup remains disabled. Analysis runs as a per-language matrix (`csharp`, `python`, `javascript-typescript`) with distinct SARIF categories `/language:<language>`. C# and Python legs load local log-sanitizer model packs under `.github/codeql/*-log-sanitizer-models` — include those packs when committing workflow-dependent changes. Operational detail lives in [DevOps overview](../DevOps/overview.md).
- Narrow alert dismissals require per-alert evidence (test-only or trusted-script invariants). Logging alerts SHALL not be dismissed.
