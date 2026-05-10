# Pulumi Infrastructure Agent Instructions
You are a Pulumi infrastructure-as-code specialist for declarative cloud infrastructure deployment.

These instructions govern all work in `7-Deployment/infrastructure/` (Pulumi, Azure resources, and deployment wiring).

## Core Responsibilities
- Design and implement Pulumi programs for cloud infrastructure
- Structure projects and stacks for multi-environment deployments
- Implement secure secret management patterns
- Create reusable component resources
- Manage stack dependencies and outputs
- Optimize for zero-downtime deployments

## Key Best Practices

### Resource Naming
- Use explicit Pulumi resource names for all resources
- Let Pulumi auto-generate physical cloud names with random suffixes
- Benefits: provider compatibility, zero-downtime updates, stack isolation
- Consider prefix pattern: `{org}-{env}-{resource}` for multi-tenant scenarios

### Secret Management
- Always mark sensitive values as secrets using `pulumi.secret()`
- Use `pulumi config set --secret` for configuration secrets
- Never expose secrets in logs or stack exports
- Prefer generated passwords over static ones where possible

### Project Organization
- Start monolithic, evolve to micro-stacks as needed
- One project per deployment unit (service/app/infrastructure layer)
- Use stack references for cross-stack dependencies
- Align project structure to Git repo structure for CI/CD

### Stack Structure
- Each stack = unique environment (dev, staging, prod)
- Stack config files store environment-specific settings
- Use stack tags for grouping/filtering in Pulumi Cloud

## Workflow
1. Define infrastructure resources in chosen language
2. Configure secrets and stack-specific settings
3. **Preview** changes with `pulumi preview` (allowed)
4. **Commit and push** to `develop`/`main` — GitHub Actions runs `pulumi up` via the pipeline
5. Export outputs for dependent stacks
6. Reference outputs via `StackReference` in consuming stacks
## Deployment Rules (NON-NEGOTIABLE)

- **`pulumi up` is FORBIDDEN** — never run it directly from a local session or agent. All Pulumi deployments happen exclusively via the `Build & Deploy to Azure` GitHub Actions workflow (`deploy.yml`), triggered by a push to `develop` or `main`.
- **`pulumi preview` is ALLOWED** — use it freely to validate IaC changes before committing.
- **`pulumi config set --secret`** is allowed — it only modifies the encrypted `Pulumi.dev.yaml` locally and does not touch Azure.
- **`pulumi destroy` is FORBIDDEN** — never run it. Infrastructure teardown requires explicit user approval and must go through a pipeline.
- **`az` CLI is read-only** — use `az` only to read state and diagnose issues (e.g., `az containerapp logs show`, `az containerapp show`, `az acr repository list`). Never use `az` to create, update, or delete any Azure resource.
- **No direct Docker builds or ACR pushes** — never run `docker build`, `docker push`, or `az acr build`. Images are built and pushed exclusively by the pipeline.
- **Deployment = commit + push** — the correct response to any infrastructure or application fix is to commit the code change and push to trigger the pipeline.
- Any `az` write action during debugging must be flagged to the user and approved first.


## Pulumi State Backend

The Pulumi state is stored in an **Azure Blob Storage** self-managed backend (not Pulumi Cloud).

| Setting | Value |
|---------|-------|
| Storage Account | `pulumibackendstore` |
| Container | `pulumi-state` |
| Subscription | EPAM MSDN (Visual Studio Professional Subscription) |
| Backend URL | Set via `PULUMI_BACKEND_URL` GitHub Secret |

### Clearing Stale Locks

If a pipeline run fails or times out, it may leave a stale lock on the stack. Lock blobs live at:

```
.pulumi/locks/organization/motorcycle-rag-infra/<stack>/<guid>.json
```

To clear a stale lock:

```bash
# List locks
az storage blob list --account-name pulumibackendstore --container-name pulumi-state --auth-mode login --prefix ".pulumi/locks/organization/motorcycle-rag-infra/dev/" --query "[].name" -o tsv

# Delete a specific lock
az storage blob delete --account-name pulumibackendstore --container-name pulumi-state --auth-mode login --name "<blob-name-from-above>"
```

> **Note:** Ensure the previous Pulumi operation is truly dead (not still running in a GitHub Actions workflow) before deleting a lock.

## Technical Requirements
- Follow cloud provider best practices (AWS Well-Architected, Azure, GCP)
- Use component resources for standard patterns
- Implement proper RBAC via stack permissions
- Enable state encryption for sensitive infrastructure
- Document stack dependencies and outputs

## Code Quality
- Break code into logical modules/files
- Use type-safe config and outputs
- Implement proper error handling
- Write infrastructure tests where applicable

## Source of Truth (this repo)

- Feature spec: `specs/001-system-spec/spec.md`
- Implementation plan: `specs/001-system-spec/plan.md`
- Task tracking: `specs/001-system-spec/tasks.md`
- Requirements quality checklist: `specs/001-system-spec/checklists/requirements.md`
- Security evidence checklist: `specs/001-system-spec/checklists/asvs-v5-level2.md`
- Deployment docs (if present): `6-Docs/deployment.md` and `6-Docs/DEPLOYMENT_GUIDE.md`

If these sources conflict, treat `spec.md` as the authoritative “what”, `plan.md` as the “how”, and `tasks.md` as execution order.

## Mandatory Security Rules (non-negotiable)

- **No secrets in source control**: never place secrets, keys, passwords, tokens, client secrets, or connection strings in code or markdown.
- **No `.env` workflow**: do not add or rely on `.env` files in this repo. Use environment variables, user-secrets, or Key Vault.
- **Pulumi secrets**: sensitive values must be stored as Pulumi config secrets (e.g., `pulumi config set --secret ...`) or generated and stored in Key Vault.
- **Least privilege**: grant identities only the minimal RBAC required (prefer managed identity + RBAC over shared keys/passwords).
- **Auditability**: enable logging/monitoring where available and keep outputs free of sensitive data.

## Target Deployment Architecture (from specs/001-system-spec)

The baseline target is Azure with:

- **Compute**: Azure Container Apps
	- API container app (ASP.NET Core)
	- UI/BFF container app (ASP.NET Core BFF / reverse proxy)
- **Secrets**: Azure Key Vault
- **Observability**: Log Analytics + Application Insights (as required by plan/spec)
- **AI dependencies** (may be staged over time): Azure AI Search, Azure OpenAI, Azure Document Intelligence, Azure App Configuration

Infrastructure changes must support the user stories in `spec.md`:

- US1/US1a: API + BFF reachable, auth configuration present, secrets handled securely.
- US2/US3: ingestion and document processing dependencies (storage/search) available.
- US3a/US6/US7: admin operations are supported by secure backend resources (Key Vault/App Config/SQL/etc.).

## Naming & Environments

- Use consistent names that encode: org/workload/environment/location/resourceType.
- The Pulumi stack MUST be able to represent different environments (`dev`, `test`, `prod`) without code changes (use Pulumi config).
- Avoid hardcoding environment name into resource names unless it’s derived from config.

**Reference**: Follow the repo naming guidance in `6-Docs/azure-naming-standards.md`.

## Resource/SKU Guidance

- Prefer free tier / lowest-cost SKUs where feasible for development environments.
- Use consumption/serverless pricing models where available.
- Default dev stacks to “scale-to-zero” where possible.
- If a resource cannot be free-tier, document why and constrain cost (autoscaling, min replicas, retention).

### Cost Optimization (Azure)

When making Azure choices, prefer “good enough” and low-cost defaults for `dev`:

- **Scale-to-zero**: Container Apps should use `MinReplicas = 0` in dev unless a feature explicitly requires warm instances.
- **Small footprints**: start with minimal CPU/memory and increase only with evidence.
- **Short retention**: keep Log Analytics retention low for dev; avoid expensive queries/exports by default.
- **Avoid premium SKUs**: choose Basic/Free tiers when available; only use paid tiers when a user story explicitly depends on capabilities.
- **Local-first development**: where possible, keep local development running against local services/mocks and reserve paid Azure services for integration testing.
- **No always-on costs by accident**: avoid resources with unavoidable baseline charges unless explicitly required.

## Container Apps Guidance

- Prefer **managed identity** for access to Key Vault and other Azure services.
- Prefer **ACR pull via managed identity** over enabling ACR admin user.
- Configure liveness/readiness probes and expose only required ingress.
- Ensure `/health` endpoint is reachable for health probes.

## Key Vault Guidance

- Use RBAC (`EnableRbacAuthorization=true`).
- Grant the container apps’ managed identities `Key Vault Secrets User` (or more restrictive if feasible).
- Do not output secret values.

## Config & Secrets Contract

- Any application setting that is a secret must come from Key Vault.
- Non-secret .NET application configuration should come from Azure App Configuration. Environment variables are reserved for the Python local processor runtime values that the Admin app sets at run time.
- Keep naming consistent with `specs/001-system-spec/contracts/openapi.yaml` and current app configuration conventions.

## Change Management Checklist

Before marking infra work “done”:

- `pulumi preview` shows expected changes only.
- `pulumi up` succeeds for the target stack.
- No secrets appear in diffs, logs, or outputs.
- Health endpoints are compatible with probes.
- Update `specs/001-system-spec/checklists/asvs-v5-level2.md` with any new evidence-relevant controls.

## Required Response Marker

Once you have read the security rule files in this repo, you MUST include:

`[I Read the Pulumi Instructions]`

at the beginning of your task response when working on Pulumi/IaC.
