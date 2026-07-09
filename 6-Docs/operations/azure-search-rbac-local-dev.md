# Azure AI Search 403 on Chunk Indexing (Job 19)

**Component:** `MotorcycleRAG.API` — `SearchClientFactory` / `ChunkIndexingService`
**Audience:** Developers/operators of the deployed API at `motorag.api.palfery.com`
**Status:** Root cause revised 2026-07-09 after confirming the actual request path; fix applied in code (see "Fix applied").

---

## Symptom

Chunk indexing (`ChunkIndexingService.MergeOrUploadDocumentsAsync`) fails with **403 Forbidden**. Confirmed request path for job 19: the Admin Desktop app uploads `chunks.jsonl` to blob storage via the **deployed API running in Azure Container Apps** (`https://motorag.api.palfery.com`, the Admin app's default `apiBaseUrl` — `1-Presentation/MotorcycleRAG.AdminDesktop/src/lib/config.ts:33`), then calls that same API to execute the Search index push. The Python local processor and a locally-run `dotnet run` API are **not** part of this path.

## Root cause

The API Container App (`apiApp`, `7-Deployment/infrastructure/Program.cs:305-330`) is provisioned with a **combined identity** — `Type = SystemAssigned_UserAssigned` (`Program.cs:308-310`):

- a **system-assigned** identity, which holds `Search Index Data Contributor` + `Reader` (`Program.cs:889-899`, scoped to `searchService.Id` — the current serverless `mcr-rag-dev-wcus-search`);
- a separate **user-assigned** identity (`containerAppIdentity`, `Program.cs:266`), which holds only `AcrPull` (`Program.cs:926-931`) for pulling container images — **no Search RBAC at all**.

`SearchClientFactory` (`SearchClientFactory.cs:49`, before this fix) and the `SearchIndexClient` DI registration (`ServiceCollectionExtensions.cs:95`, before this fix) both authenticated with a bare `new DefaultAzureCredential()`. On a resource carrying two managed identities simultaneously, `DefaultAzureCredential`'s internal `ManagedIdentityCredential` leg is not guaranteed to resolve to the system-assigned identity — if it resolves to the user-assigned `containerAppIdentity` instead, that identity has zero Search permissions, producing exactly the observed 403.

This is corroborated by contrast with `AppConfigurationExtensions.cs:25`, which authenticates against App Configuration/Key Vault using an **unqualified `ManagedIdentityCredential(new ManagedIdentityCredentialOptions())`** (not `DefaultAzureCredential`) — and that path works reliably in the same deployment. Search's client construction was the outlier.

## Fix applied

Added `SearchCredential` (`4-Persistence/MotorcycleRAG.Persistence/Azure/Search/SearchCredential.cs`), used by both Search call sites, that pins the credential chain instead of using a bare `DefaultAzureCredential`:

```csharp
new ChainedTokenCredential(
    new ManagedIdentityCredential(new ManagedIdentityCredentialOptions()),  // matches AppConfigurationExtensions.cs's proven pattern
    new AzureCliCredential());                                             // local dev fallback (no managed identity off-Azure)
```

- `SearchClientFactory.cs` — `_credential` now built via `SearchCredential.Create()`.
- `ServiceCollectionExtensions.cs` — the `SearchIndexClient` singleton now uses `SearchCredential.Create()`.

This also narrows the credential-probe surface from `DefaultAzureCredential`'s full ~9-credential chain down to 2, which should help the "cold-auth hang source" already flagged in `SearchClientFactory`'s own comments and in `6-Docs/plans/2026-07-07-chunk-upload-fix-and-serverless-search.md` (§1, root-cause #7).

**Caveat — not fully confirmed against live telemetry:** this diagnosis is built from static analysis of the Pulumi IaC and the working App Configuration credential pattern; the Search service does not yet have diagnostic logging wired to Log Analytics (that's tracked separately as T1/T10 in the serverless-migration plan), so the specific caller identity for job 19's failing request could not be directly confirmed from logs. If job 19 still fails after this deploys, that diagnostic gap should be closed next so the actual principal can be read directly instead of inferred.

## Verification

After this ships and a new revision of the API Container App is deployed:
1. Retry job 19 (or re-run the upload → index flow from the Admin app).
2. If it still 403s, check whether `az search service show`/`az role assignment list --scope <searchService.Id>` still shows the role only on the system-assigned principal, and consider adding Search diagnostic logs (plan T1/T10) to capture the actual caller identity on the next failure.

---

## Superseded analysis (kept for context — do not act on this)

An earlier version of this document assumed job 19 ran against a **locally-running** API process (`dotnet run` on a developer machine) rather than the deployed Container App, and concluded the 403 was caused by the developer's Azure CLI identity (`david_palfery@epam.com`) holding only `Search Index Data Reader`. That premise was **incorrect** — job 19 ran through `motorag.api.palfery.com` (confirmed above) — so that analysis, and its proposed local-CLI-role-grant fix, do not apply to this incident. It's preserved below only because the general reasoning (why a bare managed-identity client ID/tenant ID env var can't authenticate outside Azure, and why this repo's `environment-variables.md` policy blocks ad-hoc .NET env-var auth) remains accurate for genuine local-dev scenarios, should one come up separately.

- Managed identities have no client secret usable outside Azure; `ManagedIdentityCredential` requires IMDS, only present on Azure-hosted compute.
- `6-Docs/environment-variables.md:3-7` restricts environment-variable configuration to the Python local processor only — not .NET code.
- If a genuine local-dev-loop RBAC gap is ever found (developer running the API directly against Azure resources), the two working options are: grant the developer's CLI identity the needed role directly, or provision a dedicated service principal with a real secret for local use.
