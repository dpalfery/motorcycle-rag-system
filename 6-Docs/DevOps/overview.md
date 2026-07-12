# Deployment Configuration

This project uses **Pulumi** for Infrastructure-as-Code and **GitHub Actions** for the CI/CD pipeline.
All sensitive values are supplied at runtime through **repository secrets / variables** – **no secrets are stored in source control**.

## Environment Variables Naming Convention

**IMPORTANT**: All environment variables in this project follow a strict naming convention. See [`environment-variables.md`](environment-variables.md) for the **canonical reference** on all environment variable names across all applications (API, BFF, desktop admin client / admin desktop project, Mobile).

### Quick Reference

- **Pattern**: `MCR_<APP>_<VARIABLE>` (e.g., `MCR_API_AZURE_AD_TENANT_ID`)
- **Apps**: `API`, `BFF`, `ADMIN`, `MOBILE`
- **Usage**: Set these via GitHub Secrets, User Secrets (development), or Azure Key Vault (production)

Refer to [`environment-variables.md`](environment-variables.md) for complete variable listings, validation requirements, and setup instructions.

---

## 1. GitHub Secrets

Create the following secrets in the repository or organisation **Settings → Secrets and variables → Actions**:

### Infrastructure & Deployment Secrets

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

### Application Runtime Secrets

These are injected into the deployed applications at runtime. See [`environment-variables.md`](environment-variables.md) for complete details.

| Secret | MCR Variable | Purpose |
| --- | --- | --- |
| `MCR_API_AZURE_OPENAI_API_KEY` | `MCR_API_AZURE_OPENAI_API_KEY` | Primary/secondary key for the Azure OpenAI resource. |
| `MCR_API_AZURE_SEARCH_API_KEY` | `MCR_API_AZURE_SEARCH_API_KEY` | Admin/query key for the Azure AI Search service. |
| `MCR_API_AZURE_DOCUMENT_INTELLIGENCE_API_KEY` | `MCR_API_AZURE_DOCUMENT_INTELLIGENCE_API_KEY` | Key for the Azure Document Intelligence resource. |
| `MCR_API_AZURE_AD_TENANT_ID` | `MCR_API_AZURE_AD_TENANT_ID` | Azure AD tenant ID for API authentication. |
| `MCR_API_AZURE_AD_CLIENT_ID` | `MCR_API_AZURE_AD_CLIENT_ID` | API app registration client ID. |
| `MCR_API_SQL_CONNECTION_STRING` | `MCR_API_SQL_CONNECTION_STRING` | SQL Server connection string (Azure AD auth required). |
| `MCR_API_APPINSIGHTS_CONNECTION_STRING` | `MCR_API_APPINSIGHTS_CONNECTION_STRING` | Application Insights connection string. |
| `MCR_BFF_CLIENT_SECRET` | `MCR_BFF_CLIENT_SECRET` | BFF app registration client secret. |

> 📝 Additional services (Cosmos DB, Storage, etc.) can be added. See [`environment-variables.md`](environment-variables.md) to define new variables following the `MCR_<APP>_*` pattern.

---

## 2. GitHub Repository Variables (non-secret)

| Variable | Example | Purpose |
| --- | --- | --- |
| `AZURE_OPENAI_ENDPOINT` | `https://my-openai.openai.azure.com` | Base URL for the Azure OpenAI resource. |
| `AZURE_SEARCH_ENDPOINT` | `https://my-search.search.windows.net` | Base URL for the Azure AI Search service. |
| `DOCUMENT_INTELLIGENCE_ENDPOINT` | `https://my-doc-intel.cognitiveservices.azure.com` | Endpoint for Document Intelligence. |

These values are **publicly safe** (they reveal resource names but not keys) and therefore stored as _repository variables_ instead of secrets.

---

## 3. Local Pulumi Config

For local development you can configure the same values with Pulumi CLI:

```powershell
pulumi config set azureOpenAIEndpoint "https://..."   # non-secret
pulumi config set azureOpenAIKey "..." --secret
# ...etc.
```

The GitHub Actions workflow automatically injects all required config values via environment variables, so you do **not** need any `Pulumi.<stack>.yaml` files in the repo.

---

## 4. CI/CD Flow

The repository uses four GitHub Actions workflows. Three were consolidated into a unified PR pipeline and a nightly pipeline; the remaining two unchanged workflows handle deployment and on-demand assistance.

### 4.1 PR Gate (`pr-gate.yml`)

The unified pull-request and push pipeline replaces the former `codeql.yml`, `comprehensive-testing.yml`, `docs.yml`, and `snyk.yml` workflows. It runs on every push or PR to `main` or `develop`, on a weekly CodeQL-fallback schedule, and on `workflow_dispatch`.

```mermaid
flowchart LR
    A[Push / PR<br/>main|develop] --> P0[Phase 0<br/>changes]
    
    P0 -->|code changed| P1B[Phase 1<br/>build-test]
    P0 -->|docs changed| P1D[Phase 1<br/>docs-quality]
    
    P1B --> P2C[Phase 2<br/>codeql]
    P1B --> P2T[Phase 2<br/>trivy]
    P1B --> P2M[Phase 2<br/>semgrep]
    
    P2C --> P3I[Phase 3<br/>integration]
    P2T --> P3I
    P2M --> P3I
    P2C --> P3E[Phase 3<br/>e2e]
    P2T --> P3E
    P2M --> P3E
    P2C --> P3U[Phase 3<br/>unit-mobile]
    P2T --> P3U
    P2M --> P3U
    P2C --> P3G[Phase 3<br/>skill-gate]
    P2T --> P3G
    P2M --> P3G
    
    P1B --> GATE[Gate<br/>pr-gate-summary]
    P1D --> GATE
    P2C --> GATE
    P2T --> GATE
    P2M --> GATE
    P3I --> GATE
    P3E --> GATE
    P3U --> GATE
    P3G --> GATE
```

**Phase 0 — Change classification** (`changes` job)

- Uses `dorny/paths-filter` to detect which categories of files changed: `code` (.cs, .csproj, .py, .ts/.tsx, package.json, pyproject.toml), `docs` (.md, AGENTS.md, markdownlint config), or `infra` (.github/, 7-Deployment/).
- On schedule or workflow_dispatch, all flags are set to `true` (no diff available).
- Outputs drive path-filtered execution downstream so unchanged subsystems are skipped.

#### Phase 1 — Build and docs (parallel, path-filtered)

- `build-test`: Restores .NET dependencies, installs Python coverage tools and Admin Desktop npm packages, runs the unified unit-coverage script (`run_unit_coverage.py` with `dotnet-unit`, `bff-unit`, `python-unit`, `admindesktop-unit` suites), uploads coverage artifacts, builds the solution (`dotnet build --configuration Release`), and uploads the build output for reuse by downstream jobs. Runs only when `code` or `infra` changed.
- `docs-quality`: Runs markdownlint, validates documentation catalog/structure, checks internal links with lychee (offline), and scans docs changes for secrets with gitleaks. Runs only when `docs` changed.

#### Phase 2 — Security gate (needs build-test)

- `codeql`: Initializes CodeQL with `security-extended` queries for C#, restores + builds, and runs the CodeQL analysis. Runs only when `code` or `infra` changed.
- `trivy`: Downloads the pinned, SHA-256-verified Trivy CLI and fails the gate for high or critical dependency, misconfiguration, secret, or license findings. Uploads SARIF to GitHub Security. Runs only when `code` or `infra` changed.
- `semgrep`: Installs the pinned Semgrep Community Edition CLI and fails the gate for `ERROR` security-rule findings. Telemetry is disabled and SARIF is uploaded to GitHub Security. Runs only when `code` or `infra` changed.

#### Phase 3 — Deep validation (needs security gate pass)

- `unit-mobile`: Runs mobile-app unit tests on `macos-latest` with the MAUI workload installed.
- `integration`: Runs integration tests (non-Azure, `Category!=AzureIntegration`) on the pre-built output from Phase 1.
- `e2e`: Runs end-to-end tests with a MockServer container for external service stubs, using pre-built output.
- `skill-gate`: Builds the SkillForge CLI and validates, lints, and scans all skill directories (`.agents/skills`, `.claude/skills`, `.kilo/skills`) with SARIF upload. Currently uses `continue-on-error: true`.

**Gate summary** (`pr-gate-summary`)

- Single required check that depends on all Phase 1–3 jobs.
- Evaluates results, generates a markdown table, and posts/updates a comment on the PR with the pass/fail status.
- Branch protection should require `pr-gate / PR Gate Summary` as the sole mandatory check (see [branch protection update](../agent-notes/branch-protection-update.md)).

### 4.2 Nightly Tests & Security Scans (`nightly.yml`)

A consolidated scheduled-workflow pipeline that replaces the scheduled functionality previously spread across `comprehensive-testing.yml`, `snyk.yml`, and `codeql.yml`.

| Schedule | Jobs | Purpose |
| --- | --- | --- |
| Daily 02:00 UTC | Test suite (unit, integration, E2E, Azure integration, load, performance, SkillForge) | Full regression validation |
| Daily 03:00 UTC | Snyk scans (SCA+SAST, container scans for API, UI, Local Processor images) | Comprehensive security posture |

**Test jobs (2 AM trigger):**

- `unit-coverage-linux`: Full unit test matrix on Linux (dotnet, BFF, Python, Admin Desktop).
- `unit-mobile`: Mobile unit tests on macOS with MAUI workload.
- `unit-tests`: Aggregation gate that downloads both Linux and Mobile artifacts, runs `aggregate_coverage.py` to produce a merged Cobertura report, and uploads to Codecov.
- `integration-tests`: Non-Azure integration tests against pre-built output.
- `end-to-end-tests`: E2E tests with MockServer, building fresh.
- `azure-integration-tests`: Tests against real Azure services (requires `environment: testing`). Only runs on schedule or when `run_integration_tests` input is `true`.
- `load-tests`: NBomber-based load tests (3 min duration, 20 concurrent users in CI). Only runs on schedule or when `run_load_tests` input is `true`.
- `performance-analysis`: Generates a performance report from E2E and load test results.
- `skill-gate`: Same SkillForge validation as the PR gate, but runs after unit tests (not blocking deployment).

**Snyk security jobs (3 AM trigger):**

- `snyk-sca-sast`: Full Snyk SCA + SAST scan with SARIF upload to GitHub Security.
- `snyk-container-api`: Builds the API Docker image and runs `snyk container test` with SARIF upload.
- `snyk-container-ui`: Builds the UI Docker image and runs `snyk container test` with SARIF upload.
- `snyk-container-processor`: Builds the Local Processor Docker image and runs `snyk container test` with SARIF upload.

**Summary** (`test-summary`): Depends on all test and Snyk jobs, generates a consolidated markdown report.

### 4.3 Build & Deploy (`deploy.yml`)

Unchanged. Runs on push to `main` or `develop`:

1. Builds the React UI and copies assets to the BFF's `wwwroot`.
2. Azure login with OIDC (service principal).
3. `pulumi up` against the `dev` stack: creates/updates the Azure Resource Group, Container Registry, Container App Environment, and all supporting resources.
4. Reads Pulumi outputs (ACR server, resource group, app names, Key Vault URI, Foundry endpoint, model deployments).
5. Runs database schema migrations via `sqlcmd` against Azure SQL.
6. ACR login, Docker build & push for API and UI images (tagged with `latest` and commit SHA).
7. Updates Container App revisions to pull fresh images.
8. Provisions custom domain managed certificates for `motorag.api.palfery.com` and `motorag.palfery.com` (CNAME validation, polling up to 20 minutes).
9. Provisions Foundry agents via `AgentProvisioning` CLI and writes agent reference IDs to Key Vault.

### 4.4 Claude PR Assistant (`claude.yml`)

Unchanged. On-demand @mention workflow triggered by `issue_comment`, `pull_request_review_comment`, `issues`, or `pull_request_review` events containing `@claude`. Runs the `anthropics/claude-code-action@beta` with the `ANTHROPIC_API_KEY` secret.

### 4.5 Deleted / Replaced Workflows

The following individual workflows were replaced by the unified `pr-gate.yml` and `nightly.yml`:

| Workflow | Replaced By |
| --- | --- |
| `codeql.yml` | `pr-gate.yml` (Phase 2 `codeql` job) + `nightly.yml` (weekly CodeQL fallback schedule) |
| `comprehensive-testing.yml` | `pr-gate.yml` (Phase 1 `build-test`, Phase 3 `integration`, `e2e`) + `nightly.yml` (full nightly matrix) |
| `docs.yml` | `pr-gate.yml` (Phase 1 `docs-quality` job) |
| `snyk.yml` | `pr-gate.yml` (Phase 2 `snyk` job) + `nightly.yml` (3 AM container + SCA+SAST scans) |

---

## 5. Application Environment Variables

**ALL environment variables must follow the `MCR_<APP>_<VARIABLE>` naming convention.**

See [`environment-variables.md`](environment-variables.md) for the **complete, authoritative reference** including:

- All variable names by application (API, BFF, Admin, Mobile)
- Detailed descriptions and examples
- Type classification (Secret vs. Non-Secret)
- Validation requirements
- Setup instructions for development and production

### Security & Setup Instructions

All secrets must be stored securely:

**Development**: Use User Secrets

```powershell
dotnet user-secrets set "MCR_API_AZURE_AD_TENANT_ID" "your-tenant-id" --project 1-Presentation/MotorcycleRAG.API
dotnet user-secrets set "MCR_ADMIN_CLIENT_ID" "your-id" --project 1-Presentation/MotorcycleRAG.AdminDesktop
# ... etc
```

The current `MotorcycleRAG.sln` solution focuses on core backend, shared library, test, and infrastructure projects. The desktop admin project is a separate presentation application in this repo and is not currently listed in that solution file.

**Production**: Use Azure Key Vault or Azure App Configuration

- Set environment variables via Azure App Service "Application Settings"
- Or use Azure Key Vault with Azure App Configuration integration
- Never commit secrets to source control or store in configuration files

## 6. Rotating Secrets

Secrets can be rotated at any time by updating them in GitHub → **Settings → Secrets** and re-running the workflow. No code changes are required.
