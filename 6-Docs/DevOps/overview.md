---
id: devops/overview
title: Deployment Configuration
doc-type: reference
status: current
owner: Platform maintainers
last-reviewed: 2026-07-26
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# Deployment Configuration

This project uses **Pulumi** for Infrastructure-as-Code and **GitHub Actions** for the CI/CD pipeline.
Deploy-workflow credentials live in GitHub Actions secrets; application settings and secrets for .NET hosts are written by Pulumi into Azure App Configuration and Key Vault. **No secrets are stored in source control.**

## Application configuration model

.NET applications do **not** use `MCR_API_*`, `MCR_BFF_*`, `MCR_ADMIN_*`, or `MCR_MOBILE_*` environment variables for application settings or secrets. Those prefixes are not an approved configuration path for C# code.

| Surface | Who uses it | How values are supplied |
| --- | --- | --- |
| Azure App Configuration + Key Vault references | API, BFF, and other .NET hosts in deployed environments | Pulumi writes hierarchical `IConfiguration` keys (for example `AzureAd:TenantId`, `AzureAI:SearchServiceEndpoint`, `Sql:ConnectionString`) into App Configuration; secrets are Key Vault references resolved at load time |
| Bootstrap Container App env vars | API and BFF hosts only | Pulumi sets `AppConfig__Endpoint` (so the host can reach App Configuration) and `ConnectionStrings__ApplicationInsights` (so early startup telemetry can report App Configuration load failures). These are not a general settings channel |
| Local .NET development | Developers | Hierarchical keys via `appsettings*.json` and .NET user secrets (for example `AzureAd:TenantId`). In Development, App Configuration load is skipped |
| Process environment variables | Python local processor only | Admin Desktop sets approved values at launch. Canonical list: [environment variables reference](../reference/environment-variables.md) |

Do not add new `MCR_<APP>_*` environment-variable paths for .NET. If a .NET setting is needed, add an `IConfiguration` option and populate it from App Configuration / Key Vault (or local user secrets in Development). See also [security directives](../system/security.md).

---

## 1. GitHub Secrets

Create the following secrets in the repository or organisation **Settings → Secrets and variables → Actions**.

These secrets authenticate the deploy workflow and operate Pulumi / SQL migrations. They are **not** injected into the API or BFF as application configuration. Application settings and secrets are written by Pulumi into Azure App Configuration and Key Vault during `pulumi up`.

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
    
    P0 -->|infra changed| P2I[Phase 2<br/>iac-scan]
    P1B --> P2C[Phase 2<br/>codeql]
    P1B --> P2T[Phase 2<br/>trivy]
    P1B --> P2M[Phase 2<br/>semgrep]
    
    P2C --> P3I[Phase 3<br/>integration]
    P2T --> P3I
    P2M --> P3I
    P2C --> P3E[Phase 3<br/>e2e]
    P2T --> P3E
    P2M --> P3E
    P2C --> P3G[Phase 3<br/>skill-gate]
    P2T --> P3G
    P2M --> P3G
    
    P1B --> GATE[Gate<br/>pr-gate-summary]
    P1D --> GATE
    P2C --> GATE
    P2T --> GATE
    P2M --> GATE
    P2I --> GATE
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

- `build-test`: Restores .NET dependencies, installs Python coverage tools and Admin Desktop npm packages, runs the unified unit-coverage script (`run_unit_coverage.py` with `dotnet-unit`, `python-unit`, `admindesktop-unit` suites), uploads coverage artifacts, builds the solution (`dotnet build --configuration Release`), and uploads the build output for reuse by downstream jobs. The .NET unit tests execute at the **solution-filter level** — `run_unit_coverage.py` targets `MotorcycleRAG.UnitTests.slnf` (which excludes mobile, integration, E2E, and load projects) rather than per-project runs. This produces one `coverage.cobertura.xml` per test project; the runner collects all of them as a `coveragePaths` list in `suite-result.json`. Before coverage aggregation, a failed suite emits a prominent **UNIT TEST EXECUTION** summary with its exit code, failed .NET test name and assertion message from the TRX result, and the results directory. The summary explicitly states that the subsequent coverage result is independent; a coverage PASS never overrides a test failure. The **multi-coverage aggregator** (`aggregate_coverage.py`) reads the `coveragePaths` list (falling back to the legacy `coveragePath` for backward compatibility), parses every Cobertura file, and merges them using max per-line coverage, deduplicating classes so that a source file covered by multiple test projects is counted only once. The aggregation prints a prominent policy result in the job log: every in-scope source file and class must meet the configured 85% line-coverage threshold; failures include counts, the worst 25 files and classes, and the full-report path. Collection follows [`coverlet.runsettings`](../../coverlet.runsettings): explicitly listed generated and migration files plus `Obsolete`/`GeneratedCode`-attributed code are excluded, while compiler-generated members remain in scope. Runs only when `code` or `infra` changed.

  **Coverage exclusions.** Pure data-carrier assemblies may be excluded from the per-file/per-class 85% line-coverage gate when they contain no business invariants. `MotorcycleRAG.Contracts.Models` — the shared, data-only DTO project under `3-Domain/` — is excluded: `<Exclude>[MotorcycleRAG.Contracts.Models]*</Exclude>` in `coverlet.runsettings` removes it from coverlet collection, and `"/MotorcycleRAG.Contracts.Models/"` in `coverage-config.json` `coverageExclusions.pathContains` keeps the aggregator in agreement (the exclusion fragment cannot match the sibling interfaces-only `MotorcycleRAG.Contracts` assembly, which remains fully gated). The exclusion is additive only; every other in-scope assembly is still measured. Excluding a project from the _metric_ does not remove it from the build/test pipeline: behavior-bearing members (factory methods, computed properties, validation logic, custom converters) are still directly unit-tested in `5-Test/MotorcycleRAG.Contracts.Tests/` and asserted by `dotnet test` — only the coverage number is no longer a gate input for that project. _Policy:_ pure data-carrier DTO projects may be excluded from the line gate; behavior-bearing members within them must still be directly unit-tested.

- `docs-quality`: Runs markdownlint, validates documentation catalog/structure, checks internal links with lychee (offline), and scans docs changes for secrets with gitleaks. Runs only when `docs` changed.

#### Phase 2 — Security gate (needs build-test)

- `codeql`: Runs a **per-language matrix** (`csharp`, `python`, `javascript-typescript`) with `security-extended` queries. Each matrix leg initializes and uploads under its own SARIF category `/language:<language>` so Python and JavaScript findings are no longer attributed to the legacy `/language:csharp` bucket. The C# leg restores and builds the .NET solution before analysis. Runs only when `code` or `infra` changed. Repository CodeQL default setup must remain disabled because this is an advanced CodeQL configuration.

  **Log-sanitizer model packs:** For `csharp` and `python`, analysis uses the CodeQL CLI with `--model-packs` loading unpublished local packs under `.github/codeql/csharp-log-sanitizer-models` and `.github/codeql/python-log-sanitizer-models` (barrier models for `LogSanitizer.Sanitize` / `sanitize_log_value`). The `javascript-typescript` leg uses `github/codeql-action/analyze` without those packs. **Commit hygiene:** any change that depends on the PR CodeQL job must include those pack directories when they are new or modified; omitting them breaks the csharp/python analyze steps.
- `trivy`: Downloads the pinned, SHA-256-verified Trivy CLI and fails the gate for high or critical dependency, misconfiguration, secret, or license findings. Uploads SARIF to GitHub Security. Runs only when `code` or `infra` changed.
- `semgrep`: Installs the pinned Semgrep Community Edition CLI and fails the gate for `ERROR` security-rule findings. Telemetry is disabled and SARIF is uploaded to GitHub Security. Runs only when `code` or `infra` changed.
- `iac-scan`: Runs Checkov against Dockerfiles and GitHub Actions workflows. Results are uploaded as SARIF to GitHub Security. Runs only when `infra` changed. Soft-fail mode (advisory) — findings never fail the gate.

#### Phase 3 — Deep validation (needs security gate pass)

- `integration`: Runs integration tests (non-Azure, `Category!=AzureIntegration`) on the pre-built output from Phase 1.
- `e2e`: Runs end-to-end tests with a MockServer container for external service stubs, using pre-built output.
- `skill-gate`: Builds the Kyber-Weave CLI and validates, lints, and scans all skill directories (`.agents/skills`, `.claude/skills`, `.kilo/skills`) with SARIF upload. Currently uses `continue-on-error: true`.
- `agent-gate`: Matrix over the six harnesses (`codex`, `cursor`, `claude`, `github`, `opencode`, `kilo`). Each leg runs `agent validate . --harness <name>` and `agent scan . --harness <name>` against the project root (harness trees discovered as `.harnessname/agents`), uploading SARIF under a unique category `kyber-weave-agent-<harness>`. Currently uses `continue-on-error: true`.
- `agent-sync`: Runs `agent sync-check .` once across all discovered harnesses (role parity and instruction drift). Currently uses `continue-on-error: true`.
- `skillspector-gate`: Advisory NVIDIA SkillSpector static scan (`--no-llm`) of `.agents/skills`, merged SARIF upload under category `skillspector-skills`. Uses `continue-on-error: true`; does not fail the PR gate.

**Gate summary** (`pr-gate-summary`)

- Single required check that depends on all Phase 1–3 jobs.
- Evaluates results, generates a markdown table, and posts/updates a comment on the PR with the pass/fail status.
- Branch protection should require `pr-gate / PR Gate Summary` as the sole mandatory check (see [GitHub branch protection](../operations/github-branch-protection.md)).

### 4.2 Nightly Tests & Security Scans (`nightly.yml`)

A consolidated scheduled-workflow pipeline that replaces the scheduled functionality previously spread across `comprehensive-testing.yml`, `snyk.yml`, and `codeql.yml`.

| Schedule | Jobs | Purpose |
| --- | --- | --- |
| Daily 02:00 UTC | Test suite (unit, integration, E2E, Azure integration, load, performance, Kyber-Weave skill gate) | Full regression validation |
| Daily 03:00 UTC | Trivy container rebuild/scan for API, UI, and Local Processor images | Comprehensive security posture |

**Test jobs (2 AM trigger):**

- `unit-coverage-linux`: Full unit test matrix on Linux (dotnet, Python, Admin Desktop) running against `MotorcycleRAG.UnitTests.slnf` for .NET tests (9 test projects, including `MotorcycleRAG.DbSetup.Tests`, consolidated from the former per-suite `dotnet-unit`, `domain-unit`, `bff-unit`, and `persistence-unit` suites).
- `unit-tests`: Aggregation gate that downloads the Linux coverage artifact, selects the `dotnet-unit`, `python-unit`, and `admindesktop-unit` suites, runs `aggregate_coverage.py` to produce a merged Cobertura report, and uploads to Codecov. It prints the same 85%-per-file-and-class policy outcome and an actionable failure summary in the job log before failing the gate.
- `integration-tests`: Non-Azure integration tests against pre-built output.
- `end-to-end-tests`: E2E tests with MockServer, building fresh.
- `azure-integration-tests`: Tests against real Azure services (requires `environment: testing`). Only runs on schedule or when `run_integration_tests` input is `true`.
- `load-tests`: NBomber-based load tests (3 min duration, 20 concurrent users in CI). Only runs on schedule or when `run_load_tests` input is `true`.
- `performance-analysis`: Generates a performance report from E2E and load test results.
- `skill-gate`: Same Kyber-Weave skill validation as the PR gate, but runs after unit tests (not blocking deployment).
- `agent-gate` / `agent-sync`: Same Kyber-Weave agent validate/scan matrix and sync-check as the PR gate.
- `skillspector-gate`: Same advisory SkillSpector static scan as the PR gate.

**IaC security scan (runs on all triggers, 2 AM + 3 AM):**

- `iac-scan`: Runs Checkov against Dockerfiles and GitHub Actions workflows for comprehensive scanning regardless of changed files. Results are uploaded as SARIF to GitHub Security. Soft-fail mode (advisory) — findings logged but do not fail the run.

**Trivy container scan** (`trivy-container-scan`, 3 AM or `workflow_dispatch`):

- Rebuilds `motorcycle-rag-api` and `motorcycle-rag-ui` from `7-Deployment/Dockerfile.api` / `Dockerfile.ui`, and the local-processor image from its service Dockerfile.
- **API and UI** Trivy SARIF severity includes **MEDIUM,HIGH,CRITICAL** (categories `nightly-trivy-container-api` / `nightly-trivy-container-ui`) so OS-package MEDIUM alerts can close after pinned package upgrades land.
- **Local processor** remains **HIGH,CRITICAL** only (`nightly-trivy-container-processor`).
- Fail threshold is unchanged: SARIF upload only (no exit-code fail on findings). Gate fail policy for dependency Trivy in `pr-gate.yml` is separate and still HIGH/CRITICAL.

**API/UI base image package pins:** `Dockerfile.api` and `Dockerfile.ui` explicitly install fixed Ubuntu noble versions of `tar`, `gzip`, and `perl-base` after `apt-get upgrade` so rebuilt images ship the CVE-fixed packages. Registry images refresh only through the deploy path (see §4.3); nightly rebuild/scan alone does not push to ACR.

**Summary** (`test-summary`): Depends on all test and Trivy container-scan jobs, generates a consolidated markdown report.

### 4.3 Build & Deploy (`deploy.yml`)

Unchanged. Runs on push to `main` or `develop`:

1. Builds the React UI on Node.js 22.x (`NODE_VERSION: "22.x"` in `deploy.yml`, matching SPA `engines.node` ≥22.22.0) and copies assets to the BFF's `wwwroot`.
2. Azure login with OIDC (service principal).
3. CrossGuard policy scan: Builds the TypeScript policy pack (`7-Deployment/scanning/policy-packs/azure/`) and runs `pulumi preview --policy-pack` as an advisory scan. Scan results are informational and never block deployment (`continue-on-error: true`).
4. `pulumi up` against the `dev` stack: creates/updates the Azure Resource Group, Container Registry, Container App Environment, and all supporting resources.
5. Reads Pulumi outputs (ACR server, resource group, app names, Key Vault URI, Foundry endpoint, model deployments).
6. Runs database schema migrations via `sqlcmd` against Azure SQL (see [Database Schema Deployment](database-schema.md) for the detailed strategy and idempotency patterns).
7. ACR login, Docker build & push for API and UI images (tagged with `latest` and commit SHA).
8. Updates Container App revisions to pull fresh images.
9. Provisions custom domain managed certificates for `motorag.api.palfery.com` and `motorag.palfery.com` (CNAME validation, polling up to 20 minutes).
10. Provisions Foundry agents via `AgentProvisioning` CLI and writes agent reference IDs to Key Vault.

### 4.4 Claude PR Assistant (`claude.yml`)

Unchanged. On-demand @mention workflow triggered by `issue_comment`, `pull_request_review_comment`, `issues`, or `pull_request_review` events containing `@claude`. Runs the `anthropics/claude-code-action@beta` with the `ANTHROPIC_API_KEY` secret.

### 4.5 Deleted / Replaced Workflows

The following individual workflows were replaced by the unified `pr-gate.yml` and `nightly.yml`:

| Workflow | Replaced By |
| --- | --- |
| `codeql.yml` | `pr-gate.yml` (Phase 2 `codeql` job) + `nightly.yml` (weekly CodeQL fallback schedule) |
| `comprehensive-testing.yml` | `pr-gate.yml` (Phase 1 `build-test`, Phase 3 `integration`, `e2e`) + `nightly.yml` (full nightly matrix) |
| `docs.yml` | `pr-gate.yml` (Phase 1 `docs-quality` job) |
| `snyk.yml` | Retired. Container scanning is now `nightly.yml` (3 AM `trivy-container-scan`); dependency and code scanning are covered by Trivy, CodeQL, and Semgrep in `pr-gate.yml`. |

---

## 5. Application configuration (deployed and local)

### Deployed .NET hosts (API, BFF)

Pulumi is the source of truth for deployed application configuration:

1. Writes non-secret hierarchical keys into Azure App Configuration (for example `AzureAI:SearchServiceEndpoint`, labelled `AzureAd:*` entries for `api` / `bff`).
2. Stores secrets in Key Vault and registers App Configuration Key Vault references (for example `Sql:ConnectionString`, `AzureAd:ClientSecret` for the BFF).
3. Injects only bootstrap Container App environment variables: `AppConfig__Endpoint` and `ConnectionStrings__ApplicationInsights`.

At runtime the API and BFF call `AddAzureAppConfigurationWithKeyVault` / `AddBffAzureAppConfiguration`, load keys (including Key Vault references) with managed identity, and bind them to `IConfiguration` / options. Do not add parallel `MCR_*` environment-variable mappings for the same settings.

### Local .NET development

Use hierarchical `IConfiguration` keys via user secrets (and `appsettings*.json` for non-secrets). Example:

```powershell
dotnet user-secrets set "AzureAd:TenantId" "your-tenant-id" --project 1-Presentation/MotorcycleRAG.API
dotnet user-secrets set "AzureAd:ClientId" "your-api-client-id" --project 1-Presentation/MotorcycleRAG.API
```

In Development, Azure App Configuration load is skipped; local sources supply configuration. See [API onboarding](../MotorcycleRAG.API/onboarding.md). Admin Desktop persists its own settings locally (Tauri store), not via .NET user secrets.

### Python local processor

The only approved application environment-variable surface. Admin Desktop sets values at process launch. Canonical names and purposes: [environment variables reference](../reference/environment-variables.md).

### Secrets hygiene

- Never commit secrets to source control or store them in checked-in configuration files.
- Do not use Container App / App Service application settings as a general .NET configuration channel.
- Prefer Key Vault + App Configuration Key Vault references for production secrets.

## 6. Rotating Secrets

- **Deploy workflow credentials** (Azure SP, Pulumi backend, SQL admin): update the corresponding GitHub Actions secrets and re-run the deploy workflow.
- **Application secrets** (SQL connection strings, BFF client secret, agent references, and similar): update Key Vault (and any Pulumi-managed secret resources) so App Configuration Key Vault references resolve to the new values. Restart or refresh application hosts as required so they pick up the rotated material. No `MCR_*` environment-variable updates are involved for .NET apps.
