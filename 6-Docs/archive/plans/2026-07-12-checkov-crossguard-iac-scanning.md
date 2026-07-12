# Checkov + Pulumi CrossGuard IaC/Container/CI Security Scanning

**Status:** Archived
**Date:** 2026-07-12
**Archived:** 2026-07-12
**Goal:** Add security/compliance scanning for the Pulumi IaC (via Pulumi CrossGuard) and for the Dockerfiles and GitHub Actions workflows (via Checkov), integrated into the existing GitHub Actions pipeline.

---

## 1. Problem / Motivation

**Requested:** Add Checkov to scan the Pulumi infrastructure code for security and compliance issues.

**Premise correction (verified against authoritative source):** Checkov does **not** support Pulumi. The framework registry in `checkov/common/bridgecrew/check_type.py` (`main` branch) lists 35 frameworks:

```
ansible, argo_workflows, arm, azure_pipelines, bicep, bitbucket_pipelines,
cdk, circleci_pipelines, cloudformation, dockerfile, github_configuration,
github_actions, gitlab_configuration, gitlab_ci, bitbucket_configuration,
helm, json, yaml, kubernetes, kustomize, openapi, sca_package, sca_image,
secrets, serverless, terraform, terraform_json, terraform_plan,
sast, sast_python, sast_java, sast_javascript, sast_typescript, sast_golang,
3d_policy
```

There is no `pulumi` entry, and the Checkov docs `7.Scan Examples/` directory contains no `Pulumi.md`. Running `checkov -d 7-Deployment/infrastructure --framework pulumi` fails with "There are no runners to run."

A second, independent blocker: this repo's Pulumi program is **C#** (`7-Deployment/infrastructure/Pulumi.yaml` → `runtime: dotnet`; a single 1185-line `Program.cs`). Checkov's historical Pulumi support (now removed) parsed only **TypeScript/JavaScript** Pulumi programs via AST; it has never parsed C#. So even if a `pulumi` framework existed, it could not read `Program.cs`.

**Opportunity:** Two Checkov frameworks still deliver real value in this repo:
- `dockerfile` → `7-Deployment/Dockerfile.api`, `7-Deployment/Dockerfile.ui`
- `github_actions` → `.github/workflows/*.yml`

**Right tool for the Pulumi program:** Pulumi CrossGuard (`@pulumi/policy`) — Pulumi's native policy-as-code that runs against the resource graph emitted by `pulumi preview`. It is the only tool that can inspect the resources declared in the C# stack. CrossGuard enforcement runs as `pulumi preview --policy-pack <path>` and is controlled via per-pack and per-rule `enforcementLevel` (`advisory` vs `mandatory`).

**Concrete candidate findings observed in `Program.cs` that motivate the policy pack:**
- App Config: `DisableLocalAuth = false`, `EnablePurgeProtection = false`, `SoftDeleteRetentionInDays = 0`
- AI Services (primary + foundry) and Document Intelligence: `publicNetworkAccess = "Enabled"`
- SQL: firewall rule `AllowAzureServices` with `StartIpAddress=0.0.0.0`/`EndIpAddress=0.0.0.0`
- Storage: no `NetworkRuleSet` (default allow); Key Vault: no `networkAcls`
- Things already done right that policies should lock in: Storage `AllowBlobPublicAccess=false` + `MinimumTlsVersion=TLS1_2`; Key Vault `EnableRbacAuthorization=true`; ACR `AdminUserEnabled=false`; Container Apps using system+user-assigned managed identities.

---

## 2. Approved decisions

- **D1 — Tooling split.** Pulumi CrossGuard scans the C# Pulumi program. Checkov scans the two Dockerfiles and the GitHub Actions workflows. Checkov is not used for Pulumi.
- **D2 — CrossGuard enforcement point & severity.** Add `pulumi preview --policy-pack ...` to `deploy.yml` as a step before `pulumi up`. The pack starts at `enforcementLevel: advisory` (logs findings, does not block) and ratchets to `mandatory` (blocks the deploy) after the advisory findings are triaged. No Azure OIDC or Pulumi backend secrets are added to `pr-gate.yml`; enforcement stays in `deploy.yml` where that auth already exists.
- **D3 — Policy pack language.** TypeScript using `@pulumi/policy`. Reuses the Node toolchain already present in `deploy.yml` (React UI build). Introducing Python would require adding a Python/Poetry setup step to `deploy.yml`.
- **D4 — Policy scope.** A focused custom `PolicyPack` (~10–15 rules) covering the Azure Native resource types actually used. Rules split into "lock-in-good" (enforce things already correct) and "flag-dev-debt" (surface accepted-for-dev weaknesses). No broad community/CIS pack dependency in this iteration.
- **D5 — Checkov integration shape.** Frameworks `dockerfile` + `github_actions`. `--soft-fail` initially (advisory). SARIF uploaded to GitHub Security via `github/codeql-action/upload-sarif@v4` with `category: checkov`. New `iac-scan` job in `pr-gate.yml` (gated on `infra == 'true'`, depends only on `changes`, parallel with codeql/snyk) plus a mirror job in `nightly.yml`. Prefer explicit, justified `skip-check` entries over a binary `.checkov.baseline` file.
- **D6 — Folder layout.** All scanning assets consolidated under `7-Deployment/scanning/`: `.checkov.yaml`, `policy-packs/azure/` (TS pack), and `README.md`.
- **D7 — Local developer workflow.** Document manual `checkov` and `pulumi preview --policy-pack` commands in `7-Deployment/scanning/README.md`. Do **not** introduce a pre-commit hook in this plan (repo rule: no new cross-cutting concerns without explicit approval).

---

## 3. Investigation findings

- **Pulumi program:** `runtime: dotnet`; `Infrastructure.csproj` targets `net10.0` with `Pulumi 3.101.0` + `Pulumi.AzureNative 3.13.0`. Single `Program.cs` (1185 lines). `Pulumi.dev.yaml` and `Pulumi.yaml` exist; no `Pulumi.<stack>.yaml` secrets are committed (overview.md §3).
- **Checkov frameworks present and useful:** `dockerfile` (2 Dockerfiles under `7-Deployment/`), `github_actions` (4 workflows under `.github/workflows/`).
- **CI topology (pr-gate.yml):** Phase 0 `changes` job emits `code`/`docs`/`infra` path flags via `dorny/paths-filter`. `infra` matches `.github/**` and `7-Deployment/**`. Phase 1 `build-test` + `docs-quality`. Phase 2 `codeql` + `snyk` (both need `build-test`). Phase 3 `unit-mobile`/`integration`/`e2e`/`skill-gate`. `pr-gate-summary` aggregates a fixed `needs` list. Top-level `permissions` already grants `security-events: write` (required for SARIF upload).
- **deploy.yml:** Already performs Azure OIDC login (`azure/login@v3`) and sets `ARM_*`/Pulumi backend env for `pulumi/actions@v7`. Node and .NET are already set up. No Python. Adding a `pulumi preview --policy-pack` step before `pulumi up` is low-friction.
- **CrossGuard enforcement model:** Pack-level and per-rule `enforcementLevel` (`advisory` logs; `mandatory`/`deny` blocks). There is no Checkov-style "baseline file"; the ratchet is implemented by flipping `enforcementLevel` after triage. Rules return `Admission` via `policyFunctions`/`validateResource` callbacks keyed on Azure Native type tokens (e.g. `azure-native:storage:StorageAccount`).
- **Repo placement rules:** No new root-level files/folders; deployment assets live under `7-Deployment/`; docs under `6-Docs/`. The chosen `7-Deployment/scanning/` path complies.
- **Existing SARIF pattern:** `codeql`, `snyk` (sca+sast), and `skillforge` all upload SARIF via `github/codeql-action/upload-sarif@v4` with distinct `category` values. Checkov follows the same pattern with `category: checkov`.

---

## 4. Task list

| #  | Phase      | Component              | Description                                                                                                                                                                                                                                                                                                                                               | Skills                         |
|----|------------|------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|--------------------------------|
| 1  | Scaffold   | scanning/ + policy pack | Create `7-Deployment/scanning/` with `README.md` stub, `.checkov.yaml` placeholder, and `policy-packs/azure/` TS project (`package.json` with `@pulumi/policy` + `typescript`, `tsconfig.json`, `index.ts` registering an empty `PolicyPack` named `motorcycle-rag-azure`, `policies/` dir). Verify `npm install` + `tsc --noEmit` succeed locally.        | github-devops, pulumi-dev      |
| 2  | Implement  | policy pack            | Implement ~10–15 CrossGuard rules in `policy-packs/azure/policies/*.ts`, one file per resource family (storage, keyvault, appconfig, sql, aiservices, containerapps, acr). Each rule names the control it maps to (CIS/CKV-style ID in a comment) and registers against the correct `azure-native:<provider>:<type>` token. Pack `enforcementLevel: advisory`. | pulumi-dev                     |
| 3  | Wire CI    | deploy.yml             | Add a `pulumi preview --policy-pack 7-Deployment/scanning/policy-packs/azure` step (with `npm ci` + `npm run build`) before `Pulumi Up`, using the existing Azure/Pulumi env. Capture advisory output in the job log; do not fail the job while advisory. Gate with `if:` so it does not run on the policy-pack scaffolding commit until the pack builds.   | github-devops                  |
| 4  | Configure  | Checkov                | Author `7-Deployment/scanning/.checkov.yaml` (frameworks `dockerfile,github_actions`, `--soft-fail`, output flags). Run Checkov locally against `7-Deployment` (dockerfile) and `.github` (github_actions) to enumerate findings; record the finding set in the scanning README as the triage baseline.                                                    | github-devops                  |
| 5  | Wire CI    | pr-gate.yml + nightly  | Add `iac-scan` job to `pr-gate.yml` (needs `[changes]`, runs when `infra == 'true'`, parallel with Phase 2; installs Checkov via pip, runs `checkov -d 7-Deployment -d .github --framework dockerfile,github_actions --config-file 7-Deployment/scanning/.checkov.yaml --soft-fail --output sarif --output-file-path checkov.sarif`, uploads SARIF `category: checkov`). Add `iac-scan` to `pr-gate-summary.needs` and the results table. Mirror as a job in `nightly.yml`. | github-devops |
| 6  | Remediate  | Program.cs + Dockerfiles | Triage advisory findings from tasks 2 & 4. For each: fix in source (preferred) or add a justified `skip-check`/per-rule advisory retention with a documented reason. Targeted fixes likely include App Config purge protection, AI/DocIntel public network access, Storage network rule set, and Dockerfile `USER`/tag-pinning decisions.                 | pulumi-dev, dotnet-dev, github-devops |
| 7  | Document   | canonical docs         | Update `6-Docs/DevOps/overview.md` §4 (CI/CD Flow) to describe the new `iac-scan` PR-gate job, the nightly Checkov mirror, and the CrossGuard advisory step in deploy.yml. Add a `7-Deployment/scanning/README.md` covering local-run commands, the advisory→mandatory ratchet, and how to add a rule/skip. Add a reference from `7-Deployment/infrastructure/AGENTS.md`. Update `6-Docs/catalog.md` if it catalogs scanning tooling. | docs-dev                       |
| 8  | Review     | all changes            | Code review (`code-reviewer`) for correctness/quality of TS policy pack + workflow YAML + Dockerfile/Program.cs changes; security review (`code-reviewer` security focus / `security-review` skill) on the policy rules, skip justifications, and the new CI secret surface (confirm no new secrets enter `pr-gate.yml`).                                   | code-reviewer                  |
| 9  | Closeout   | plan                   | Verify acceptance criteria against implementation evidence, confirm canonical docs updated, update `6-Docs/plans/README.md`, archive this plan under `6-Docs/archive/plans/` with status `Archived`.                                                                                                                                                       | docs-dev                       |

---

## 5. Sequencing / dependency graph

```
1 (scaffold) ──┬─► 2 (policy rules) ──┬─► 3 (deploy.yml CrossGuard)
               │                       │
               └─► 4 (checkov config) ─┴─► 5 (pr-gate + nightly Checkov)
                                               │
                                               ▼
                                             6 (remediate/triage) ──► 7 (docs)
                                                                        │
                                                                        ▼
                                                                      8 (review) ──► 9 (closeout)
```

- Task 1 blocks 2 and 4 (scaffolding precedes pack code and Checkov config).
- Tasks 2 and 4 are parallel once 1 is done.
- Task 3 depends on 2 (pack must build before deploy.yml references it).
- Task 5 depends on 4 (config must exist before CI references it) and is independent of 2/3.
- Task 6 depends on 2 AND 5 (needs both scanners producing real findings to triage).
- Task 7 depends on 3, 5, 6 (docs describe finalized CI shape and triage outcomes).
- Task 8 depends on 7 (review the complete change set).
- Task 9 depends on 8.

---

## 6. Residual decisions / risks

- **Exact policy rule list** (task 2): the ~10–15 rule selection is confirmed after the first advisory run reports real findings against `Program.cs`. Owner: pulumi-dev + task 6 triage.
- **Ratchet-to-mandatory date:** the flip from `advisory` to `mandatory` (D2) happens after task 6 triage closes; not time-boxed in this plan. Owner: user, on task-6 recommendation.
- **`pr-gate-summary` branch-protection interaction:** adding `iac-scan` to the summary's `needs` keeps the single required check intact, but if `iac-scan` is later promoted from soft-fail to hard-fail it becomes gating; branch-protection expectations must follow. Owner: github-devops (task 5) + user.
- **Pulumi preview in deploy.yml cost/time:** a real `pulumi preview` adds Azure round-trips to every deploy run and can fail on transient Azure errors. Mitigation: keep advisory (non-blocking) and `continue-on-error: true` while advisory; revisit when flipping to mandatory.
- **Checkov Docker findings expected:** `CKV_DOCKER_2` (no `USER` — ASP.NET base images historically run as root/app), `CKV_DOCKER_7` (image tag `...:10.0` not pinned to digest). Each must be fixed or explicitly skipped with justification in task 6.
- **No new secret exposure:** confirmed `pr-gate.yml` gains no Azure/Pulumi secrets; only `deploy.yml` (already holds them) runs CrossGuard.

---

## 7. Out of scope

- **Checkov for Pulumi** — technically impossible (no framework; C# unparseable). Excluded permanently unless Checkov adds a real Pulumi/C# framework.
- **Broad community/CIS policy pack adoption** — deferred; D4 delivers a focused custom pack first. Revisit as a separate plan if broader coverage is later required.
- **Pre-commit hooks** — not introduced (repo rule: no new cross-cutting concerns without explicit approval). Local-run commands documented instead.
- **Checkov `secrets` framework** — gitleaks already runs in `docs-quality`; avoid duplicate secret scanning.
- **Checkov on Kubernetes/Helm/Bicep/ARM** — none of those IaC formats exist in this repo.
- **Runtime/Azure Policy (Defender for Cloud)** — runtime posture management is a separate concern from IaC scanning; not addressed here.
- **PR-gate `pulumi preview` enforcement** — deliberately deferred (would require PR-pipeline secret/OIDC exposure and fork-safety handling); see D2.

---

## 8. Required skills

- **pulumi-dev** — CrossGuard TypeScript policy pack design, Azure Native type tokens, `pulumi preview --policy-pack` behavior.
- **github-devops** — `pr-gate.yml`/`nightly.yml`/`deploy.yml` edits, Checkov job + SARIF upload, CI sequencing and branch-protection interaction.
- **dotnet-dev** — any `Program.cs` remediation in task 6 (C#/Pulumi.AzureNative).
- **docs-dev** — DevOps overview update, scanning README, catalog/AGENTS.md cross-references, plan closeout/archive.
- **code-reviewer** (incl. security focus / `security-review` skill) — review of policy rules, skip justifications, CI YAML, and the new secret surface.

(Orchestrator maps these skills to the performing specialist agents; this plan does not assign agents.)

---

## 9. Verification harness

- **CrossGuard pack (tasks 1–2):** `npm ci` and `npm run build` (`tsc --noEmit`) succeed in `7-Deployment/scanning/policy-packs/azure/`. A local authorized `pulumi preview --policy-pack ...` (after the root Azure guard rail per `7-Deployment/infrastructure/AGENTS.md`) executes and emits the expected advisory findings against the dev stack without error.
- **Checkov (tasks 4–5):** `pip install checkov && checkov -d 7-Deployment -d .github --framework dockerfile,github_actions --config-file 7-Deployment/scanning/.checkov.yaml` runs locally and produces a SARIF file. The `iac-scan` job in `pr-gate.yml` runs green (soft-fail) on a PR that touches `7-Deployment/**`; the SARIF appears in the repo's GitHub Security tab under category `checkov`.
- **deploy.yml (task 3):** A `deploy.yml` run on `main` logs CrossGuard advisory output and does not fail the job; `pulumi up` still proceeds.
- **Remediation (task 6):** every non-fixed finding has a documented justification in either `.checkov.yaml` (`skip-check` with comment) or the policy rule's retained `advisory` level + README entry.
- **Review gates (task 8):** `code-reviewer` returns APPROVE or changes-requested with explicit resolution; security review confirms no new secrets in `pr-gate.yml`, no over-broad `permissions`, and that `skip-check` justifications are sound.
- **Docs (tasks 7 & 9):** `6-Docs/DevOps/overview.md` §4 and `7-Deployment/scanning/README.md` accurately describe the running CI; `6-Docs/plans/README.md` reflects the new plan; per AGENTS.md plan-closeout, `docs-dev` archives this plan only after acceptance criteria and canonical docs are verified.
