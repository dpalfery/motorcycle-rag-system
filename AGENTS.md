# MotorcycleRAG Repository Instructions

`AGENTS.md` files are mandatory instructions, not optional background material. Read this file before work anywhere in the repository, then read the nearest scoped `AGENTS.md` before changing files in that subtree.

## Non-negotiable rules

- **CodeGraph first:** Before using `grep`, `rg`, `find`, shell globbing, or direct file reads to locate or understand repository files or source code, use CodeGraph. Prefer the CodeGraph MCP tool; if it is unavailable, run `codegraph explore`. Use another discovery or read method only when CodeGraph fails to return a relevant, sufficiently complete source result, and state that failure before using the fallback.
- Do not create infrastructure, deployment assets, dependencies, cross-cutting concerns, or documentation files without user approval. Ask before an architectural decision; present the trade-offs.
- Do not commit, push, reset, restore, checkout, clean, or rebase without explicit user approval. Keep agent-generated notes under `6-Docs/agent-notes/`, never at repository root.
- Do not introduce fallbacks, stubs, or workarounds without explicit approval. Fix the root cause.
- Never commit secrets, tokens, connection strings, passwords, customer data, or `.env` files. Use approved configuration and Key Vault patterns; redact prompts and PII from logs.
- .NET code must use Azure App Configuration and Key Vault references for application configuration and secrets. Python local-processor runtime values set by Admin Desktop are the only approved environment-variable exception.
- Before every `az` read, verify the active subscription against the allowlist in [Azure agent access](6-Docs/AzureEnvironment/agent-access.md). Azure writes, local `pulumi up`, direct Docker builds, and ACR pushes are forbidden.
- Preserve Clean Architecture: inner layers never depend on outer layers; Contracts contains interfaces only; Contracts.Models contains shared DTOs only; business invariants belong in Domain; Application services belong in `Services`.
- Do not create new files or folders at repository root. Scripts, tools, and deployment assets belong under `7-Deployment/`; documentation belongs under `6-Docs/`; generated notes belong under `6-Docs/agent-notes/`.

Read the full [working agreement](6-Docs/system/agent-governance.md), [security directives](6-Docs/system/security.md), and [Azure environment rules](6-Docs/AzureEnvironment/agent-access.md) when the task touches their subject.

## Documentation and placement

Before changing a cataloged component, read the [documentation standard](6-Docs/documentation-standard.md), [component catalog](6-Docs/catalog.md), the source-root README, and the component's detailed documentation. Update canonical documentation when the public interface, configuration, architecture, runtime, operations, or workflow changes.

## Plan closeout

For plan-backed work, implementation completion does not close the plan. After implementation verification is complete, the orchestrator SHALL assign a `docs-dev` plan-closeout task before reporting the work complete. The documentation specialist SHALL verify the plan's acceptance criteria against the implementation evidence, update the affected canonical documentation, and maintain the [plan index](6-Docs/plans/README.md). Only then may it archive the plan under `6-Docs/archive/plans/` with status `Archived`; otherwise the plan remains `Review required` or returns to an active status. The documentation standard defines the detailed lifecycle.

## Agent configuration synchronization

Shared agent-role behavior SHALL remain synchronized across all six supported development-tool configurations: Codex (`.codex/agents/`), Cursor (`.cursor/agents/`), GitHub Copilot (`.github/agents/`), OpenCode (`.opencode/agents/`), Kilo (`.kilo/agents/`), and Claude (`.claude/agents/`). When changing an agent's instructions, scope, workflow, responsibilities, or policy, update the corresponding role in every one of these locations in the same change. Platform-specific metadata such as model names, tools, permissions, and front matter may differ, but the role behavior must remain equivalent. If a required counterpart is missing or cannot represent the change, resolve or explicitly report the discrepancy before claiming the agent update is complete.

Before creating, moving, renaming, or placing source/test files, or changing namespaces, project references, DTO placement, interfaces, or layer boundaries, read the [architecture placement rules](6-Docs/rules/architecture-general.md).

`6-Docs/archive/` is historical only: do not follow, cite, or copy it as current guidance.

## Task routing

| Work area | Mandatory scoped instructions | Read when relevant |
| --- | --- | --- |
| API | [API AGENTS](1-Presentation/MotorcycleRAG.API/AGENTS.md) | [API documentation](6-Docs/MotorcycleRAG.API/) |
| Web UI and BFF | [Web UI AGENTS](1-Presentation/MotorcycleRag.WebUI/AGENTS.md), [BFF AGENTS](1-Presentation/MotorcycleRag.WebUI.BFF/AGENTS.md) | [Web UI documentation](6-Docs/MotorcycleRag.WebUI/) |
| Admin Desktop | [Admin Desktop AGENTS](1-Presentation/MotorcycleRAG.AdminDesktop/AGENTS.md) | [Admin Desktop documentation](6-Docs/MotorcycleRAG.AdminDesktop/) and task-specific guides |
| Mobile App | [Mobile AGENTS](1-Presentation/MotorcycleRAG.MobileApp/AGENTS.md) | [Mobile documentation](6-Docs/MotorcycleRAG.MobileApp/) |
| Local processor | [Processor AGENTS](2-Application/local-processing-service/AGENTS.md) | [Processor documentation](6-Docs/local-processing-service/) |
| Core, Application, Domain, Contracts, Persistence | nearest scoped `AGENTS.md` | [Architecture rules](6-Docs/rules/architecture-general.md) |
| Tests | nearest test-suite `AGENTS.md` | affected component documentation and requirements |
| Infrastructure | [Infrastructure AGENTS](7-Deployment/infrastructure/AGENTS.md) | [DevOps documentation](6-Docs/DevOps/) and [Azure environment](6-Docs/AzureEnvironment/) |
| Codex configuration | [.codex AGENTS](.codex/AGENTS.md) | repository rules in this file remain controlling |

## Instruction hierarchy

1. This root file supplies repository-wide mandatory policy.
2. The nearest scoped `AGENTS.md` supplies additional rules for its subtree; it may not weaken this file.
3. Canonical system, component, deployment, and environment documentation provides detailed task-specific guidance. Scoped instructions link directly to the owning document; they are not a substitute for root policy.

## Cursor Cloud specific instructions

Cloud VMs run **Linux** (the `setup-dev-environment` skill targets Windows/macOS only). The startup update script already refreshes dependencies (`npm install` for both JS apps, `poetry install --no-root` for the processor, `dotnet restore` of the solution). Toolchains live at `~/.dotnet` (.NET 10 SDK, `DOTNET_ROOT`) and `~/.local/bin` (Poetry); both are on `PATH` via `~/.bashrc`. Standard build/test/run commands are in each component's README/onboarding under `6-Docs/` — reference those rather than duplicating.

Non-obvious caveats for running services here:

- **Docker is not auto-started.** Start it once per session with `sudo dockerd &` before `docker compose up -d` (SQL Server 2025, container `motoRAG`, `localhost:1433`, SA password from `docker-compose.yml`). Docker 29 needs `fuse-overlayfs` + `containerd-snapshotter:false` in `/etc/docker/daemon.json` and `iptables-legacy` (already configured in the snapshot).
- **Database provisioning:** after SQL Server is up, run the DbSetup CLI non-interactively (see `7-Deployment/DbSetup/README.md`), e.g. `--sa-password 'MotoRAG_Password123!' --db-name MotorcycleRAG --seed-test-data`.
- **MAUI Mobile App cannot build/run on Linux** (Mac Catalyst/Windows targets only). Restore succeeds, but exclude it from local build/run.
- **API/BFF local run:** `dotnet run` uses the HTTPS launch profile. To run over plain HTTP for local wiring, pass `--no-launch-profile` and set `ASPNETCORE_URLS`. The API reads its DB connection from `Sql:ConnectionString`; production delivers a passwordless string via App Config + Key Vault, so for a local SQL container set `Sql__ConnectionString` (embedded creds only log a warning). In Development, Azure App Configuration is skipped; Azure AI/Search/DocIntel/Foundry health checks report `Degraded` without cloud credentials — expected. `/health` still returns 200 and `sql_database` is `Healthy` against the local DB.
- **BFF requires `AzureAd:ClientSecret`** (an Entra secret) to serve *any* request — its auth handler initializes lazily on the first request and throws without it. The BFF's YARP proxy also rewrites the downstream `Host` header to `motorag.api.palfery.com`, which the API's host-validation rejects unless that host is added to `AllowedHosts`. So the browser → BFF → API chain needs both a client secret and that host allowance; pure frontend dev (`npm run dev`, Vite on `:5173`) renders the SPA without the BFF.
- **Public access-request path** (`POST /api/access-requests`, anonymous) writes the row to SQL successfully but currently returns HTTP 400 in a minimal dev config because a telemetry event throws an `NullReferenceException` when Application Insights is disabled — the persistence side still works.
- **Local Processing Service** needs `GRAPH_EXTRACTION_ENDPOINT`/`GRAPH_EXTRACTION_MODEL` set at startup (normally injected by Admin Desktop). It starts and serves `/health`, `/jobs`, etc., but reports `unhealthy` until an embedding provider (LM Studio/Ollama) and blob storage are configured — expected locally. Set `WATCH_FOLDER_DISABLED=true` for an HTTP-only run. Use `poetry run python src/main.py` (venv is Poetry-managed, not in-project `.venv`).
- **Full authenticated query E2E** (Web UI/Mobile → answer) requires Azure AI Foundry/Search + Entra CIAM secrets and cannot be exercised without them.
