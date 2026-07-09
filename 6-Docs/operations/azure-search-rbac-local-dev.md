# Azure AI Search 403 on Chunk Indexing (Job 19)

**Component:** `MotorcycleRAG.API` — `SearchClientFactory` / `ChunkIndexingService`
**Audience:** Developers/operators of the deployed API at `motorag.api.palfery.com`
**Status:** Resolved 2026-07-09. Two distinct causes, fixed in sequence — see "Part 2" below for the one that actually stopped the 403s.

---

## Part 2 — missing Search Service Contributor (found after Part 1 shipped and the 403 persisted)

Confirmed via Application Insights (`AppDependencies` in workspace `mcr-rag-dev-cus-log97525f00`) that after the Part 1 credential-pinning fix deployed (revision `mcr-rag-dev-cus-api0689104e--0000202`, 100% traffic), the 403 continued on two specific calls:

- `GET /indexes('motorcycle-sport')?api-version=2025-09-01` — `SearchClientFactory.IndexExistsAsync` (`SearchClientFactory.cs:99`) calling `SearchIndexClient.GetIndexAsync`.
- `HEAD /` against the service root, repeating every ~60s — the `AzureSearchHealthCheck` ping.

The Part 1 fix was necessary but not sufficient: it correctly pinned the credential to the container app's system-assigned identity, and that identity's token was being sent — but the identity only held `Search Index Data Contributor` + `Search Index Data Reader`. Per Microsoft's [Azure AI Search RBAC permission table](https://learn.microsoft.com/azure/search/search-security-rbac#built-in-roles), reading an index's *definition* (as opposed to its documents) is an object-management operation gated behind `Search Service Contributor` (or Owner/Contributor) — the two data-plane roles don't cover it. So the 403 was a real, correctly-enforced authorization gap, not another identity-resolution bug.

**Fix applied:** grant `Search Service Contributor` (`7ca78c08-252a-4471-8644-bb5ff32d4ba0`) to the same system-assigned identity (`280335aa-2bf4-4a4a-99b1-f1a16b173ee5`), scoped to `mcr-rag-dev-wcus-search`. Codified in `7-Deployment/infrastructure/Program.cs` (`{namePrefix}-api-search-service-role`), right after the existing Search role assignments.

An initial out-of-band ARM deployment applied this directly to unblock job 19 immediately, but that manual assignment was deleted again (`az role assignment delete`) once it was confirmed Pulumi doesn't yet know about it — leaving it in place would have made the next `pulumi up` hit "role assignment already exists" and require a `pulumi import` to reconcile state. **The 403 is only actually resolved once `pulumi up` runs against this code change** and creates the role assignment through Pulumi-managed state; until then, `IndexExistsAsync` and the search health check will 403 again exactly as before.

## Part 1 — wrong identity in the credential chain

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

This diagnosis was originally built from static analysis of the Pulumi IaC and the working App Configuration credential pattern, without direct telemetry (the API's own Application Insights instrumentation — `AppExceptions`/`AppDependencies` in `mcr-rag-dev-cus-log97525f00` — turned out to be sufficient to confirm both this and the Part 2 root cause; no separate Search-side diagnostic logging was needed).

## Verification

1. Confirmed via `AppDependencies`: after Part 1 shipped, the credential correctly resolved to the system-assigned identity (`280335aa-2bf4-4a4a-99b1-f1a16b173ee5`) — but `GET /indexes(...)` and the health check `HEAD /` still 403'd, leading to Part 2.
2. **After running `pulumi up` to apply Part 2's `{namePrefix}-api-search-service-role`**, re-check `AppDependencies` for `Target == "mcr-rag-dev-wcus-search.search.windows.net"` and confirm `ResultCode` is no longer `403`. RBAC changes can take a few minutes to propagate — allow a short delay before re-testing.
3. Retry job 19 (or re-run the upload → index flow from the Admin app) to confirm `ChunkIndexingService.MergeOrUploadDocumentsAsync` succeeds end-to-end.
4. `az role assignment list --scope <searchService.Id> -o table` should show `Search Service Contributor` on the system-assigned identity's principal ID once the Pulumi deploy completes.

---

## Superseded analysis (kept for context — do not act on this)

An earlier version of this document assumed job 19 ran against a **locally-running** API process (`dotnet run` on a developer machine) rather than the deployed Container App, and concluded the 403 was caused by the developer's Azure CLI identity (`david_palfery@epam.com`) holding only `Search Index Data Reader`. That premise was **incorrect** — job 19 ran through `motorag.api.palfery.com` (confirmed above) — so that analysis, and its proposed local-CLI-role-grant fix, do not apply to this incident. It's preserved below only because the general reasoning (why a bare managed-identity client ID/tenant ID env var can't authenticate outside Azure, and why this repo's `environment-variables.md` policy blocks ad-hoc .NET env-var auth) remains accurate for genuine local-dev scenarios, should one come up separately.

- Managed identities have no client secret usable outside Azure; `ManagedIdentityCredential` requires IMDS, only present on Azure-hosted compute.
- `6-Docs/environment-variables.md:3-7` restricts environment-variable configuration to the Python local processor only — not .NET code.
- If a genuine local-dev-loop RBAC gap is ever found (developer running the API directly against Azure resources), the two working options are: grant the developer's CLI identity the needed role directly, or provision a dedicated service principal with a real secret for local use.
