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

## Auth & Access

- Default = no access. Permissions explicitly granted.
- Authorize every action (e.g., `[Authorize(Policy = "mcr-api-admin")]`).
- Admin endpoints must validate `azp` matches the Admin App Client ID.
- Enforce rate limiting on public APIs.

## Communication

- Enforce HTTPS + HSTS on all web server configurations.
