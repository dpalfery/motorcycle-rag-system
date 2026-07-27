---
id: webui-bff/onboarding
title: MotorcycleRAG Web UI BFF Developer Onboarding
doc-type: onboarding
status: current
component: MotorcycleRAG Web UI BFF
source-root: 1-Presentation/MotorcycleRag.WebUI.BFF
owner: Web UI maintainers
last-reviewed: 2026-07-21
code-refs:
  - AuthController
  - HostHeaderValidationMiddleware
  - DataProtectionHealthCheck
api-endpoints:
  - GET /auth/me
decided-by: []
supersedes: []
---

# MotorcycleRAG Web UI BFF Developer Onboarding

The BFF and the SPA are developed together. [Web UI onboarding](../MotorcycleRag.WebUI/onboarding.md) is the canonical end-to-end setup path; this document covers what is specific to the BFF host and does not repeat it.

## Prerequisites

- .NET 10 SDK.
- HTTPS development certificate trusted for ASP.NET Core.
- Entra External ID application values, an API reverse-proxy destination, and the browser origins the BFF should accept.

## Configuration

Configuration comes from Azure App Configuration with Key Vault references, per the repository configuration policy. Never place secrets in `appsettings*.json`.

| Setting | Purpose |
| --- | --- |
| `AllowedHosts` | Host-header allowlist. `*` allows all. An empty value is a misconfiguration and the host fails to start. |
| `Cors:AllowedOrigins` | Browser origins for the `AllowFrontend` policy. Development adds the local Vite origins automatically. |
| `ReverseProxy` | YARP route and cluster configuration for the `/api/*` proxy. |
| Data-protection blob settings | Key persistence outside Development. See [data protection](data-protection.md). |

## Run locally

1. Build the SPA first so the BFF has assets to serve — see [Web UI onboarding](../MotorcycleRag.WebUI/onboarding.md).
2. Start the host:

```bash
dotnet run --project 1-Presentation/MotorcycleRag.WebUI.BFF
```

1. Open the BFF origin — not the Vite origin — for anything involving authentication.

## Verify

Work through the BFF origin in the browser, then confirm each of:

- `GET /auth/login` redirects to the identity provider and returns to the requested path.
- `GET /auth/me` reports `sessionAuthenticated: true` and `approvalStatus: "Approved"` for an approved account.
- An API call from the SPA reaches `/api/*` **on the BFF origin**, and the browser request carries no `Authorization` header — the BFF attaches the bearer token server-side.
- `POST /auth/logout` clears the session and `/auth/me` returns to `approvalStatus: "SignedOut"`.
- `GET /health` is healthy, including the data-protection probe when blob-backed keys are enabled.

## Debugging

- **Access tokens must never appear in browser storage or client-side code.** If one does, the proxy transform is being bypassed.
- `GET /auth/me` always returns `200`. Read `approvalStatus` from the body; do not infer state from the status code. `Unknown` means the BFF could not reach a verdict, which is a different fault from `ApprovalRequired`.
- A start-up failure mentioning the host allowlist means `AllowedHosts` parsed to empty. That is deliberate fail-fast behaviour in `HostHeaderValidationMiddleware`, not a bug to route around.
- Session loss after a restart or scale-out points at data-protection key persistence, not at authentication. Go to [troubleshooting](../operations/webui-bff-data-protection-troubleshooting.md).

## Tests

```bash
dotnet test 5-Test/MotorcycleRag.WebUI.BFF.Tests/MotorcycleRag.WebUI.BFF.Tests.csproj
```

## Non-standard procedures

- Do not work around a proxy or CORS problem by pointing the SPA at the API cross-origin. The single-origin model is the security boundary.
- The data-protection health check takes an injected `IDataProtectionBlobProbe`. Do not construct SDK clients or credentials inside a health check.
