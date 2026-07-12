# Fix Deployment Secrets Table for Issue #109

**Status:** Draft
**Date:** 2026-07-11
**Goal:** Make the "Infrastructure & Deployment Secrets" table in `6-Docs/deployment/overview.md` exactly match the secrets actually consumed by `.github/workflows/deploy.yml`.

---

## 1. Problem / Motivation

The secrets table under `### Infrastructure & Deployment Secrets` in `6-Docs/deployment/overview.md` (§1 "GitHub Secrets") is wrong in two directions:

1. It documents `PULUMI_ACCESS_TOKEN` ("Personal access token for the Pulumi SaaS backend"), but that secret is **never referenced** anywhere in the workflow. The project uses a **self-hosted Azure Blob Storage Pulumi backend**, not Pulumi SaaS, so the token is unused.
2. It omits six secrets that the workflow **does** consume:
   - `PULUMI_BACKEND_URL`
   - `PULUMI_CONFIG_PASSPHRASE`
   - `AZURE_STORAGE_ACCOUNT_PULUMI`
   - `AZURE_STORAGE_KEY_PULUMI`
   - `SQL_ADMIN_LOGIN`
   - `SQL_ADMIN_PASSWORD`

This causes new contributors to create the wrong secret (`PULUMI_ACCESS_TOKEN`) and never create the secrets the pipeline actually needs, breaking deployments.

## 2. Approved decisions

- **D1:** Scope is the single secrets table and its one-line intro under `### Infrastructure & Deployment Secrets`. No workflow/source changes.
- **D2:** `PULUMI_ACCESS_TOKEN` row is **removed** (confirmed unused — see §3).
- **D3:** Six missing secrets are **added** with purpose descriptions verified against `deploy.yml` line references.
- **D4:** The existing two-column table format (`| Secret | Purpose |`) is preserved. No new columns.
- **D5:** The intro sentence (currently "These are used by Pulumi for deploying Azure resources:") is corrected, because two of the added secrets (`SQL_ADMIN_*`) are consumed by the migration step, not Pulumi.
- **D6:** Rows are ordered logically by function: Azure service principal → self-hosted Pulumi backend → SQL migrations.

## 3. Investigation findings

### Authoritative secret inventory from `deploy.yml`

Extracted via `grep -oE '\$\{\{ secrets\.[A-Z_]+ \}\}' deploy.yml | sort -u` — **10 unique secrets**:

| Secret | Where in deploy.yml | Purpose (verified) |
| --- | --- | --- |
| `AZURE_CLIENT_ID` | L57, L76, L118, L276 | `azure/login` + `ARM_CLIENT_ID` for Pulumi |
| `AZURE_CLIENT_SECRET` | L61, L77, L119, L280 | `azure/login` + `ARM_CLIENT_SECRET` for Pulumi |
| `AZURE_TENANT_ID` | L58, L78, L120, L277 | `azure/login` + `ARM_TENANT_ID` for Pulumi |
| `AZURE_SUBSCRIPTION_ID` | L59, L79, L121, L278 | `azure/login` + `ARM_SUBSCRIPTION_ID` for Pulumi |
| `PULUMI_BACKEND_URL` | L72, L114 | Self-hosted backend URL (Azure Blob) for `pulumi up` + `pulumi stack output` |
| `PULUMI_CONFIG_PASSPHRASE` | L73, L115 | Encrypts secrets in Pulumi state/config |
| `AZURE_STORAGE_ACCOUNT_PULUMI` | L74, L116 | Storage account hosting Pulumi backend (→ `AZURE_STORAGE_ACCOUNT` env) |
| `AZURE_STORAGE_KEY_PULUMI` | L75, L117 | Access key for Pulumi backend storage account (→ `AZURE_STORAGE_KEY` env) |
| `SQL_ADMIN_LOGIN` | L146, L152 | SQL admin login for the `sqlcmd` schema-migration step |
| `SQL_ADMIN_PASSWORD` | L147, L153 | SQL admin password for the `sqlcmd` schema-migration step |

### `PULUMI_ACCESS_TOKEN` is confirmed unused

- `grep PULUMI_ACCESS_TOKEN` across `.github/workflows/` → **no matches**.
- Repo-wide grep → only match is `6-Docs/deployment/overview.md` line 31 (the stale doc row). (This grep was performed before this plan document was written; the plan itself now intentionally contains `PULUMI_ACCESS_TOKEN` in descriptive prose.)
- The self-hosted backend model is confirmed by the `PULUMI_BACKEND_URL` + `AZURE_STORAGE_ACCOUNT`/`AZURE_STORAGE_KEY` env injection in the Pulumi steps (L72–75, L114–117). There is no Pulumi SaaS login, hence no access token.

### Scope of consistency updates

- `grep` for all seven secret names across the **entire** `6-Docs/` tree returned exactly one hit: the stale `PULUMI_ACCESS_TOKEN` row in `overview.md`. No other documentation references these secrets.
- `6-Docs/deployment/README.md` and `database-setup.md` do not mention secrets.
- `environment-variables.md` lives at `6-Docs/reference/environment-variables.md` and covers application runtime `MCR_*` variables only — it does not list deployment secrets and needs **no change**.
- No `AGENTS.md` exists under `6-Docs/deployment/`; `7-Deployment/infrastructure/AGENTS.md` does not enumerate secrets.

**Conclusion: exactly one file requires editing.**

### Previous table state (pre-fix, overview.md lines 22–31 verbatim)

```markdown
### Infrastructure & Deployment Secrets
These are used by Pulumi for deploying Azure resources:

| Secret | Purpose |
| --- | --- |
| `AZURE_CLIENT_ID` | Service-principal client ID used by Pulumi's Azure provider and the `azure/login` action. |
| `AZURE_CLIENT_SECRET` | Service-principal client secret. |
| `AZURE_TENANT_ID` | Azure Active Directory tenant ID. |
| `AZURE_SUBSCRIPTION_ID` | Subscription that will contain the resources. |
| `PULUMI_ACCESS_TOKEN` | Personal access token for the Pulumi SaaS backend (https://app.pulumi.com). |
```

## 4. Task list

| # | Phase | Component | Description | Skills |
|---|-------|-----------|-------------|--------|
| 1 | Edit | `6-Docs/deployment/overview.md` | Replace the intro sentence + table under `### Infrastructure & Deployment Secrets` (lines 23–31) with the corrected content from §5 below. Remove `PULUMI_ACCESS_TOKEN`; add the six missing secrets. Preserve the two-column format and surrounding structure (Application Runtime Secrets table, headings, links). | app-docs-standard |
| 2 | Verify | doc | Confirm the table now lists exactly the 10 secrets from §3; confirm no other file needs change; confirm Markdown lints clean. | (none) |

## 5. Precise edit: proposed replacement content

Replace overview.md lines 23–31 (the intro sentence + the five-row table) with:

```markdown
These are used by the GitHub Actions deploy workflow for Azure authentication, Pulumi state management via a self-hosted Azure Blob backend, and database schema migrations:

| Secret | Purpose |
| --- | --- |
| `AZURE_CLIENT_ID` | Service-principal client ID used by the `azure/login` action and Pulumi's Azure provider (`ARM_CLIENT_ID`). |
| `AZURE_CLIENT_SECRET` | Service-principal client secret (`ARM_CLIENT_SECRET`). |
| `AZURE_TENANT_ID` | Microsoft Entra ID tenant ID (`ARM_TENANT_ID`). |
| `AZURE_SUBSCRIPTION_ID` | Subscription that will contain the deployed resources (`ARM_SUBSCRIPTION_ID`). |
| `PULUMI_BACKEND_URL` | URL of the self-hosted Pulumi backend (Azure Blob Storage, e.g. `azblob://<container>`). The project does **not** use the Pulumi SaaS backend. |
| `PULUMI_CONFIG_PASSPHRASE` | Passphrase that encrypts secrets stored in Pulumi state and configuration. |
| `AZURE_STORAGE_ACCOUNT_PULUMI` | Name of the Azure Storage account hosting the Pulumi backend; injected as the `AZURE_STORAGE_ACCOUNT` env var in the workflow. |
| `AZURE_STORAGE_KEY_PULUMI` | Access key for the storage account hosting the Pulumi backend; injected as the `AZURE_STORAGE_KEY` env var in the workflow. |
| `SQL_ADMIN_LOGIN` | SQL Server administrator login used by the workflow's schema-migration step (`sqlcmd`). |
| `SQL_ADMIN_PASSWORD` | SQL Server administrator password used by the workflow's schema-migration step. |
```

Notes for the implementer:
- Keep the `### Infrastructure & Deployment Secrets` heading (line 22) unchanged.
- Keep everything from line 32 onward unchanged.
- The four existing `AZURE_*` purpose strings are lightly tightened to note the `ARM_*` mapping; this is optional — if you prefer a minimal diff you may keep the original wording for those four rows verbatim and only swap the `PULUMI_ACCESS_TOKEN` row for the six new rows. Either is acceptable as long as `PULUMI_ACCESS_TOKEN` is gone and all six new rows are present.

## 6. Sequencing / dependency graph

Task 1 (edit) → Task 2 (verify). Single-file change; no parallelization needed.

## 7. Residual decisions / risks

- **§4 "CI/CD Flow" has separate, pre-existing inaccuracies** (claims Pulumi runs a preview step and builds/pushes Docker images; the workflow actually runs only `pulumi up` and does Docker build/push in dedicated steps). These are **out of scope** for issue #109; do not touch them in this change to keep the PR focused. Flag for a follow-up issue if desired.
- **Do not invent values.** Every purpose description must reference what the secret *is for*, never an example/placeholder value. The documentation standard forbids credentials in docs.
- **Secret-name vs env-var mapping** for the two `*_PULUMI` secrets is intentional (suffixed to avoid collision) — the purpose text must make clear they map to `AZURE_STORAGE_ACCOUNT` / `AZURE_STORAGE_KEY` so readers aren't confused by the mismatch.
- **Link integrity:** no links are added or removed by this edit, so internal-link validation should be unaffected.

## 8. Required skills

- `app-docs-standard` — conformance to `6-Docs/documentation-standard.md` (table format, no credentials, facts verified from source).

## 9. Verification harness

1. **Completeness check:** After the edit, the "Infrastructure & Deployment Secrets" table MUST contain exactly these 10 rows and no others: `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `PULUMI_BACKEND_URL`, `PULUMI_CONFIG_PASSPHRASE`, `AZURE_STORAGE_ACCOUNT_PULUMI`, `AZURE_STORAGE_KEY_PULUMI`, `SQL_ADMIN_LOGIN`, `SQL_ADMIN_PASSWORD`.
2. **Removal check:** `grep PULUMI_ACCESS_TOKEN` over `.github/workflows/` and `6-Docs/deployment/` returns zero matches. (Exclude this plan document under `6-Docs/plans/` and historical content under `6-Docs/archive/`, which intentionally contain the token name in prose.)
3. **Source-of-truth check:** `grep -oE '\$\{\{ secrets\.[A-Z_]+ \}\}' .github/workflows/deploy.yml | sort -u` returns exactly the same 10 names as the table.
4. **Lint/validation:** Markdown linting and internal-link validation pass (per documentation-standard §Validation).
5. **No-secrets check:** No real secret values, tokens, or connection strings appear anywhere in the edit.
6. **Review:** code-reviewer reviews the single-file diff for accuracy and doc-standard conformance.
