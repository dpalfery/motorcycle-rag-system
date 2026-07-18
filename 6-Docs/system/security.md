# Security Directives

These are **non-optional** and apply to all code, tests, config, scripts, and docs.

## Secrets

- **NEVER** hardcode secrets, connection strings, tokens, or passwords in any file — ever.
- C#/.NET application code must use Azure App Configuration for configuration and Azure Key Vault references for secrets. Do not read application settings or secrets directly with `Environment.GetEnvironmentVariable()` in C#/.NET code.
- The only approved environment-variable usage is the Python local processor runtime values that the Admin app sets at run time.
- No `.env` files. Appsettings files must never contain secrets.
- It is better the app not work than for a secret to be exposed.

## Input Handling

- SQL: parameterized queries ONLY. Never string concatenation.
- HTML/UI: encode output to prevent XSS.
- Logging: use structured logging with placeholders. Never concatenate user input into log strings. Redact PII and query text.

## Log value encoding

Request, job, file, endpoint, exception, and other untrusted strings that reach log sinks SHALL be encoded before they are written so raw control characters cannot forge log lines.

- **C#:** `MotorcycleRAG.Core.Utilities.LogSanitizer.Sanitize`.
- **Python (local processor):** `security.log_sanitizer.sanitize_log_value`.

Both helpers:

1. Escape backslashes first, then emit visible escapes for CR, LF, tab, NUL, and remaining C0/C1 controls (`\r`, `\n`, `\t`, `\0`, `\u00XX`).
2. Preserve printable non-control content in full (reversible escaping; no default truncation).
3. Leave repository-required PII and query-text redaction unchanged; encoding does not replace redaction.

Do not bulk-dismiss static-analysis logging alerts. If a scanner does not recognize an approved encoder, add the narrowest supported sanitizer model; do not suppress the rule.

## Auth & Access

- Default = no access. Permissions explicitly granted.
- Authorize every action (e.g., `[Authorize(Policy = "mcr-api-admin")]`).
- Admin endpoints must validate `azp` matches the Admin App Client ID.
- Enforce rate limiting on public APIs.

## Communication

- Enforce HTTPS + HSTS on all web server configurations.
- Remote outbound HTTP clients SHALL use HTTPS with normal certificate and hostname validation. `verify=False`, custom hostname bypasses, and open redirects are forbidden.
- Plain HTTP is allowed only for literal loopback local-model endpoints (`localhost` / `127.0.0.1` / `::1`). Remote private, link-local, and credential-bearing URLs fail closed.
- In the local processor, outbound calls go through `security.url_validation` and `security.safe_http` policy-bound transports.

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
- The custom PR CodeQL workflow is the sole CodeQL owner; GitHub default setup remains disabled. Language-specific CodeQL matrix categories are introduced only after the legacy analysis category has closed the original alerts on the default branch. Operational detail lives in [DevOps overview](../DevOps/overview.md).
- Narrow alert dismissals require per-alert evidence (test-only or trusted-script invariants). Logging alerts SHALL not be dismissed.
