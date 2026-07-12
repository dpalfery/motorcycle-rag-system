# Security Scanning

This directory contains IaC security scanning tools for the MotorcycleRAG infrastructure.

## Tools

### Pulumi CrossGuard
- Policy pack: `policy-packs/azure/`
- Enforcement: advisory (logs findings, does not block)
- Integration: `deploy.yml` runs `pulumi preview --policy-pack`

### Checkov
- Config: `.checkov.yaml`
- Frameworks: `dockerfile`, `github_actions`
- Enforcement: soft-fail (advisory) — findings logged but do not block CI
- Integration: `pr-gate.yml` `iac-scan` job

## Checkov Baseline Findings

Baseline captured on 2026-07-12 using Checkov 3.3.8.
Soft-fail mode: all findings are advisory and do not block CI.

### Dockerfile Findings (4 failures across 2 Dockerfiles)

| Check ID | File | Description | Severity | Recommended Resolution |
|----------|------|-------------|----------|----------------------|
| CKV_DOCKER_2 | `Dockerfile.api`, `Dockerfile.ui` | No `HEALTHCHECK` instruction | MEDIUM | **Skip with justification.** The Azure Container App platform provides its own health probes (`livenessProbe`/`startupProbe`) at the orchestrator level. Adding a Dockerfile HEALTHCHECK is redundant and would add `curl` or `wget` to the production image (increasing attack surface). |
| CKV_DOCKER_3 | `Dockerfile.api`, `Dockerfile.ui` | No explicit `USER` instruction | MEDIUM | **Skip with justification.** The `mcr.microsoft.com/dotnet/aspnet:10.0` base image already runs as a non-root user (`app` UID 1654). Adding an explicit `USER` instruction is cosmetic — Checkov cannot detect the base image's default user. |

### GitHub Actions Findings (1 failure across 1 workflow)

| Check ID | File | Description | Severity | Recommended Resolution |
|----------|------|-------------|----------|----------------------|
| CKV_GHA_7 | `nightly.yml` | `workflow_dispatch` inputs are non-empty (loads test toggles) | LOW | **Skip with justification.** The inputs (`run_load_tests`, `run_integration_tests`) are boolean toggles with safe defaults (`false`). They do not affect build outputs or source compilation — they only gate test execution. The check is overly broad for this use case. |

### Resolved Findings

These findings were previously open and have since been fixed:

| Check ID | File | Description | Severity | Resolution |
|----------|------|-------------|----------|------------|
| CKV2_GHA_1 | `claude.yml` | Top-level `permissions` block was absent (defaults to `write-all`) | HIGH | **Fixed.** Added restrictive top-level `permissions: { contents: read, pull-requests: read, issues: read }`. Verified 2026-07-12. |

### Passed Checks (summary)

**Dockerfile framework** — 80 passed checks covering:
- No port 22 exposure (`CKV_DOCKER_1`)
- No `apt` usage (`CKV_DOCKER_9`)
- Absolute `WORKDIR` paths (`CKV_DOCKER_10`)
- Unique `FROM` aliases in multi-stage builds (`CKV_DOCKER_11`)
- No `sudo` usage (`CKV2_DOCKER_1`)
- No disabled SSL validation (`CKV2_DOCKER_2` through `CKV2_DOCKER_16`)
- No untrusted package options (`CKV2_DOCKER_7` through `CKV2_DOCKER_11`)

**GitHub Actions framework** — 542 passed checks covering:
- No `ACTIONS_ALLOW_UNSECURE_COMMANDS` (`CKV_GHA_1`)
- No shell injection in `run` commands (`CKV_GHA_2`)
- No suspicious `curl` with secrets (`CKV_GHA_3`)
- No suspicious `netcat` usage (`CKV_GHA_4`)
- Artifact builds have cosign attestation (`CKV_GHA_5`, `CKV_GHA_6`)
- Top-level permissions not set to `write-all` (`CKV2_GHA_1`) — passes for `pr-gate.yml`, `deploy.yml`, `nightly.yml`, `claude.yml` (which have explicit scoped permissions)

## CrossGuard Baseline Findings

The CrossGuard policy pack (`policy-packs/azure/`) implements 13 advisory rules across 7 Azure resource families. All rules start at `enforcementLevel: advisory` — findings are logged but do not block deployment.

### Lock-in-good rules (all PASS)

These rules enforce security properties that are already correctly configured in Program.cs:

| Rule | Resource | Control | Status |
|------|----------|---------|--------|
| `storage-no-public-blob` | StorageAccount | CIS Azure 3.5 / CKV_AZURE_59 | ✅ PASS |
| `storage-minimum-tls-12` | StorageAccount | CIS Azure 3.8 / CKV_AZURE_109 | ✅ PASS |
| `keyvault-rbac-authorization` | KeyVault | CIS Azure 8.4 / CKV_AZURE_41 | ✅ PASS |
| `keyvault-soft-delete-enabled` | KeyVault | CIS Azure 8.34 / CKV_AZURE_42 | ✅ PASS |
| `acr-admin-user-disabled` | ContainerRegistry | CIS Azure 7.1 / CKV_AZURE_136 | ✅ PASS |
| `containerapp-managed-identity` | ContainerApp | CKV_AZURE_240 | ✅ PASS |

### Dev-debt findings (advisory, documented accepted risks)

These rules flag configurations that are acceptable in the dev environment but should be hardened for staging/production:

| Rule | Resource | Current State | Resolution |
|------|----------|---------------|------------|
| `storage-network-default-deny` | StorageAccount | No NetworkRuleSet (default: allow) | Accept — needs private endpoint/VNet |
| `keyvault-network-acls` | KeyVault | No networkAcls | Accept — needs private endpoint/VNet |
| `appconfig-disable-local-auth` | ConfigurationStore | `DisableLocalAuth = false` | Accept — Pulumi provider limitation |
| `appconfig-purge-protection` | ConfigurationStore | `EnablePurgeProtection = false` | Accept — dev teardown/rebuild blocker |
| `appconfig-soft-delete-retention` | ConfigurationStore | `SoftDeleteRetentionInDays = 0` | Accept — dev teardown/rebuild blocker |
| `sql-no-allow-azure-services-firewall` | FirewallRule | `0.0.0.0`–`0.0.0.0` range | Accept — needed for Container Apps connectivity |
| `ai-services-public-network-disabled` | CognitiveServices Account | `PublicNetworkAccess = Enabled` | Accept — needs private endpoint |

### Known coverage gaps

- **Generic-resource AI Services accounts** — `aiServices` and `foundryAiServices` are provisioned via `azure-native:resources:Resource` (not typed `cognitiveservices:Account`), so the `ai-services-public-network-disabled` rule does not evaluate them. Both have `publicNetworkAccess: "Enabled"`. Track as a policy-pack follow-up.
- **Search service** — Also uses `azure-native:resources:Resource` pattern. No rule targets Search public network access.

## Local Developer Workflow

### Running Checkov locally

```bash
# Install Checkov
pip install checkov

# Scan Dockerfiles
checkov -d 7-Deployment --framework dockerfile --config-file 7-Deployment/scanning/.checkov.yaml

# Scan GitHub Actions workflows
checkov -d .github --framework github_actions --config-file 7-Deployment/scanning/.checkov.yaml

# Scan both with SARIF output
checkov -d 7-Deployment -d .github \
  --framework dockerfile,github_actions \
  --config-file 7-Deployment/scanning/.checkov.yaml \
  --output sarif --output-file-path checkov.sarif
```

### Running CrossGuard locally

```bash
# Build the policy pack
cd 7-Deployment/scanning/policy-packs/azure
npm ci
npm run build

# Run Pulumi preview with the policy pack
# (requires Azure login and Pulumi backend access)
cd 7-Deployment/infrastructure
pulumi preview --policy-pack ../scanning/policy-packs/azure
```

> **Note:** CrossGuard requires Azure credentials and Pulumi backend access. Run `az login` and configure Pulumi backend before running locally.

## Enforcement Ratchet

### Current state: Advisory

All CrossGuard rules and Checkov scans run in advisory mode:
- **CrossGuard:** `enforcementLevel: "advisory"` — findings logged, never blocks `pulumi up`
- **Checkov:** `--soft-fail` — findings logged, never fails the CI job

### Ratcheting to mandatory

When findings are triaged and either fixed or documented as accepted risks:

1. **CrossGuard:** Change individual rule `enforcementLevel` from `"advisory"` to `"mandatory"` in `policy-packs/azure/policies/*.ts`
2. **Checkov:** Remove `--soft-fail` from the `iac-scan` job in `pr-gate.yml` and `nightly.yml`

### How to add a new rule

1. Create a new `.ts` file in `policy-packs/azure/policies/` (one file per resource family)
2. Implement the rule using `validateResourceOfType` from `@pulumi/policy`
3. Import and add the rule to `policy-packs/azure/index.ts`
4. Run `npm run build` to verify compilation
5. Test locally with `pulumi preview --policy-pack ...`

### How to add a skip-check

1. Add an entry to `.checkov.yaml` under `skip-check:` with a documented justification
2. Update this README's findings table to reflect the skip
