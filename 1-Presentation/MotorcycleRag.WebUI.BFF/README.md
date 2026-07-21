# MotorcycleRAG Web UI BFF

Backend for Frontend for the MotorcycleRAG web experience. It is the browser's only
origin: it terminates interactive sign-in, holds the session in an encrypted cookie,
serves the React SPA, and reverse-proxies `/api/*` to the platform API with a bearer
token attached server-side. No access token reaches browser JavaScript.

**Technology:** ASP.NET Core (.NET 10) with YARP, OpenID Connect against Entra External
ID, and blob-backed data-protection key persistence.

**Entry point:** `Program.cs`, composing configuration and the request pipeline defined
in `Extensions/WebApplicationExtensions.cs`.

**Boundary:** Presentation layer. It composes configuration, security, and proxy policy
only — no business rules, no data access.

```bash
dotnet run --project 1-Presentation/MotorcycleRag.WebUI.BFF
```

## Documentation

- [Architecture](../../6-Docs/MotorcycleRag.WebUI.BFF/architecture.md)
- [Onboarding](../../6-Docs/MotorcycleRag.WebUI.BFF/onboarding.md)
- [Requirements](../../6-Docs/MotorcycleRag.WebUI.BFF/requirements.md)
- [Data protection key persistence](../../6-Docs/MotorcycleRag.WebUI.BFF/data-protection.md)
- Operations: [runbook](../../6-Docs/operations/webui-bff-data-protection-operations.md) · [disaster recovery](../../6-Docs/operations/webui-bff-data-protection-disaster-recovery.md) · [troubleshooting](../../6-Docs/operations/webui-bff-data-protection-troubleshooting.md)
