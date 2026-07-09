# Azure AI Search RBAC — Local Dev 403 on Chunk Indexing

**Component:** `MotorcycleRAG.API` — `SearchClientFactory` / `ChunkIndexingService`
**Audience:** Developers running the API locally against `mcr-rag-dev-wcus-search`
**Status:** Investigated 2026-07-09. Root cause confirmed. **Fix not yet applied** — requires an explicit, authorized action (RBAC mutation) that this change intentionally does not perform. See "Next step" below.

---

## Symptom

Chunk indexing (`ChunkIndexingService.MergeOrUploadDocumentsAsync`) fails with **403 Forbidden** when the API runs locally, even though the Python local processor already uploaded `chunks.jsonl` to blob storage successfully.

## Root cause (confirmed)

`SearchClientFactory` (`4-Persistence/MotorcycleRAG.Persistence/Azure/Search/SearchClientFactory.cs:49`) authenticates with `DefaultAzureCredential`. In local development this resolves to the developer's **Azure CLI identity** (`david_palfery@epam.com`), which currently holds only **Search Index Data Reader** on `mcr-rag-dev-wcus-search` — read-only. The app's own identity, the user-assigned managed identity `mcr-rag-dev-cus-api0689104e`, already has **Search Index Data Contributor**, but that identity is only usable when the API runs *as* the Container App in Azure.

## Why "point `DefaultAzureCredential` at the app's managed identity locally" does not work

A proposal was raised to fix this by setting `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` for the local API process so `DefaultAzureCredential` resolves to `mcr-rag-dev-cus-api0689104e` instead of the developer's CLI identity. This was investigated and **rejected as non-functional**, for two independent reasons:

1. **The identity is a real Azure User-Assigned Managed Identity, not a service principal.** Confirmed in `7-Deployment/infrastructure/Program.cs:266` (`Pulumi.AzureNative.ManagedIdentity.UserAssignedIdentity`), assigned to the Container Apps via `SystemAssigned_UserAssigned` (`Program.cs:308-310`). Managed identities have **no client secret** — there is nothing to hand to `ClientSecretCredential`/`EnvironmentCredential` outside Azure.
   - `DefaultAzureCredential`'s `EnvironmentCredential` only activates when `AZURE_CLIENT_ID` + `AZURE_TENANT_ID` are accompanied by `AZURE_CLIENT_SECRET` (or a cert path). With only client ID + tenant ID set, it is skipped.
   - `ManagedIdentityCredential` only works via the Azure Instance Metadata Service (IMDS), which exists solely on Azure-hosted compute (the Container App, a VM, App Service, etc.) — not a local dev machine. It fails locally regardless of which client ID is set.
   - Net effect: `DefaultAzureCredential` falls through the chain to `AzureCliCredential` anyway, landing back on `david_palfery@epam.com` — **the 403 would persist**, just with a more confusing setup to debug.

2. **It would violate this repo's environment-variable policy.** `6-Docs/environment-variables.md:3-7` states environment variables are *not* a general configuration mechanism here, and explicitly: *"Do not add `MCR_API_*`... environment-variable paths for .NET code... The only approved environment-variable surface is the Python local processor."* Wiring ad-hoc `AZURE_CLIENT_ID`/`AZURE_TENANT_ID` into the API's local launch profile runs against that documented rule and would need an explicit policy exception, not a quiet workaround.

## Options that do work

**Option A — Grant the developer's CLI identity write access (recommended, matches how `DefaultAzureCredential` actually resolves locally):**

```bash
az role assignment create \
  --assignee david_palfery@epam.com \
  --role "Search Index Data Contributor" \
  --scope /subscriptions/5df33f46-892f-4dc1-9d0c-701464efd7e5/resourceGroups/mcr-rag-dev-cus-rg49bcb82b/providers/Microsoft.Search/searchServices/mcr-rag-dev-wcus-search
```

Zero code changes. Role propagation is typically 1–2 minutes; retry the upload/job afterward.

**Option B — Dedicated local-dev service principal:** create a *separate* Entra app registration (not the Container App's managed identity — a real app registration with a client secret), grant it `Search Index Data Contributor`, and use it locally via `ClientSecretCredential`. This keeps the personal identity read-only but requires new-app-registration + secret-rotation overhead, and — per the policy above — would need an explicit, approved mechanism for surfacing the secret to the .NET process (not a plain `AZURE_CLIENT_SECRET` env var by default).

## What this change intentionally does NOT do

- **No `launchSettings.json`/env var changes were made** for the .NET API — the managed-identity-via-env-var approach doesn't fix the 403 (see above) and conflicts with `environment-variables.md`.
- **No `az role assignment create` was executed.** Granting a role on `mcr-rag-dev-wcus-search` is a write to shared Azure RBAC state on a real subscription; it requires explicit authorization before being run, which was not obtained in this session (see `6-Docs/agent-instructions/azure-environment.md` subscription-allowlist guard rail).

## Next step required

A human needs to explicitly authorize **Option A** (run the `az role assignment create` above) or **Option B** (provision a dedicated service principal). This document records the investigation so that decision can be made without re-deriving the root cause.
