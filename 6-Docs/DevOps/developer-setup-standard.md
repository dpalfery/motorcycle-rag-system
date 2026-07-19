# Developer Machine Setup Standard

This is the canonical standard for bootstrapping a brand-new Windows or macOS machine into a working MotorcycleRAG development machine: the required tooling, install approach, safety guardrails, and validation criteria. The `.agents/skills/setup-dev-environment` skill follows this standard when running an interactive setup session; it is registered as **Developer Setup Standard** in the root [AGENTS.md](../../AGENTS.md) Repository Configuration & Paths registry so other skills and agents can find it without depending on that skill directly.

This document does not replace per-component onboarding. Once the tooling below is installed, start with [system onboarding](../system/onboarding.md) to pick the right component, then read that component's own `onboarding.md` for its specific prerequisites, run/debug steps:

- [API onboarding](../MotorcycleRAG.API/onboarding.md)
- [Web UI onboarding](../MotorcycleRag.WebUI/onboarding.md)
- [Admin Desktop onboarding](../MotorcycleRAG.AdminDesktop/onboarding.md)
- [Mobile App onboarding](../MotorcycleRAG.MobileApp/onboarding.md)
- [Local Processing Service onboarding](../local-processing-service/onboarding.md)
- [Azure Environment onboarding](../AzureEnvironment/onboarding.md)

## Required Tooling

- Git, GitHub CLI.
- .NET SDK 10.0.100 or a compatible roll-forward version, plus the .NET workloads this repository's projects require (MAUI, etc.).
- Node.js and npm.
- Rust and Cargo, plus platform Tauri prerequisites.
- Python and Poetry (for `2-Application/local-processing-service`).
- Docker, and SQL Server tools where present.
- Azure CLI (read-only usage only — see Guardrails).
- VS Code (or VS Code Insiders) with the extensions listed in `.vscode/extensions.json`.
- MCP servers configured from `.mcp.json` and `.codex/config.toml`.
- Ollama.

Platform-specific additions:

- **macOS:** Xcode or Command Line Tools, with `DEVELOPER_DIR` set appropriately.
- **Windows:** Visual Studio Build Tools or Visual Studio workloads needed for .NET, MAUI, and Tauri.

## Platform Setup

Exact package IDs and per-platform commands:

- [Windows Setup](developer-setup-windows.md)
- [macOS Setup](developer-setup-macos.md)

## Editor and MCP Configuration

VS Code extension list, required MCP servers, and Azure MCP / Playwright MCP configuration: [MCP and Extensions](mcp-and-extensions.md).

## Local Database

Local SQL Server container startup and schema/user provisioning via the DbSetup CLI: [Database Setup CLI](database-setup.md).

## Install Approach

- Prefer native package managers: `winget` on Windows, Homebrew on macOS — see Platform Setup for exact commands.
- Use repo package locks with `npm ci`, not `npm install`, for Node workspaces.
- Use Poetry for `2-Application/local-processing-service`.
- Install VS Code extensions from `.vscode/extensions.json` — see Editor and MCP Configuration.
- Configure MCP from `.mcp.json` and `.codex/config.toml`; do not preserve stale machine-specific paths.
- Use the existing database setup CLI for schema/user provisioning — do not hand-roll schema scripts. See Local Database.

## Guardrails

- Do not create new infrastructure scripts, Makefiles, CI/CD, IaC, or docker-compose files.
- Do not run `pulumi up`, `docker build`, `docker push`, `az acr build`, or any Azure write command.
- Before any `az` command, run `az account show --query id -o tsv`; proceed only if it returns `5df33f46-892f-4dc1-9d0c-701464efd7e5`. If the subscription cannot be verified, stop.
- Do not hardcode or print secrets. Use prompts or existing secure setup CLIs for passwords.
- Do not create `.env` files.
- Do not persist C#/.NET application secrets in user-level environment variables. Local database setup may use process-level values for validation, but durable app secrets must come from the approved configuration path.
- Treat `docker-compose.yml` as an existing local development dependency only. Ask before starting containers.
- Do not overwrite existing shell profiles, VS Code settings, MCP configs, or environment variables without showing the diff/impact and getting approval.
- Prefer account-wide environment alignment when it is the root cause of Mac Catalyst/MAUI issues.
- Wait for explicit user approval before network installs, machine-level changes, Docker/container startup, database provisioning, or extension installation.

## Validation Criteria

Run validation in layers. Stop on root-cause failures and fix them instead of masking failures with workarounds.

**Core tools:**

```sh
git --version
gh --version
dotnet --info
dotnet workload list
node --version
npm --version
python --version || python3 --version
poetry --version
rustc --version
cargo --version
docker --version
docker compose version
```

For Azure CLI, first check whether the binary exists with `command -v az` or `where az`. Do not run `az --version` or any other `az` command until the active subscription is verified against the allowlist (see Guardrails).

**Dependency restore:**

```sh
dotnet restore MotorcycleRAG.sln
(cd 1-Presentation/MotorcycleRag.WebUI && npm ci)
(cd 1-Presentation/MotorcycleRAG.AdminDesktop && npm ci)
(cd 2-Application/local-processing-service && poetry install)
```

**Builds and tests** (focused checks first):

```sh
dotnet build MotorcycleRAG.sln -c Debug
dotnet test MotorcycleRAG.sln
(cd 1-Presentation/MotorcycleRag.WebUI && npm run build)
(cd 1-Presentation/MotorcycleRAG.AdminDesktop && npm run build)
(cd 2-Application/local-processing-service && poetry run pytest)
(cd 1-Presentation/MotorcycleRAG.AdminDesktop && npm run tauri -- --version)
```

For Mac Catalyst/MAUI, do not claim VS Code F5 is fixed from a CLI build alone — confirm the actual VS Code launch path when the user asks for debug readiness.

**Database:** see [Database Setup CLI](database-setup.md) Local development startup for container and provisioning validation commands.

**A machine is not "ready" unless, for each stack touched:**

- Focused validation for that stack has run and passed, or
- Every blocked item is explicitly listed with the reason it is blocked.

The final report must include: OS and architecture, installed tool versions, installed VS Code extensions, MCP servers configured or blocked, database container/provisioning status, validation commands run and results, and remaining manual steps or approvals needed. Do not say "ready" unless required validations have passed.
