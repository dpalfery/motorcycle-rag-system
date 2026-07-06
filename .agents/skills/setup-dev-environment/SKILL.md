---
name: setup-dev-environment
description: Set up the MotorcycleRAG development environment on a brand new Windows or macOS machine. Use when the user asks to setup/install/bootstrap/configure the repo or dev environment, including .NET, MAUI, Node/npm, Tauri/Rust, Python/Poetry, Docker/SQL Server database setup, VS Code extensions, MCP servers, Azure CLI read-only tooling, GitHub CLI, Ollama, and validation.
license: MIT
metadata:
  author: David R Palfery
  version: 1.0.0
---

# Setup Dev Environment

Use this skill to turn a new Windows or macOS machine into a working MotorcycleRAG development machine.

## Required Behavior

- Start every response with `[******Working Agreement: Active******]`.
- Detect OS first: Windows native/WSL, macOS Intel/Apple Silicon, shell, package managers, VS Code or VS Code Insiders.
- Inventory before installing. Read the repo files listed in [inventory.md](references/inventory.md), then run read-only version checks.
- Present a concise install plan with missing tools, install commands, and trade-offs. Wait for explicit user approval before network installs, machine-level changes, Docker/container startup, database provisioning, or extension installation.
- Install with the platform-native path:
  - Windows: read [windows.md](references/windows.md).
  - macOS: read [macos.md](references/macos.md).
- Configure local services and data only after approval:
  - Database: read [database.md](references/database.md).
  - MCP/VS Code/Codex: read [mcp-and-extensions.md](references/mcp-and-extensions.md).
- Validate with [validation.md](references/validation.md) before reporting success.

## Guardrails

- Do not create new infrastructure scripts, Makefiles, CI/CD, IaC, or docker-compose files.
- Do not run `pulumi up`, `docker build`, `docker push`, `az acr build`, or Azure write commands.
- Before any `az` command, run `az account show --query id -o tsv`; proceed only if it returns `5df33f46-892f-4dc1-9d0c-701464efd7e5`. If the subscription cannot be verified, stop.
- Do not hardcode or print secrets. Use prompts or existing secure setup CLIs for passwords.
- Do not create `.env` files.
- Do not persist C#/.NET application secrets in user-level environment variables. Local database setup may use process-level values for validation, but durable app secrets must come from the approved configuration path.
- Treat `docker-compose.yml` as an existing local development dependency only. Ask before starting containers.
- Do not overwrite existing shell profiles, VS Code settings, MCP configs, or environment variables without showing the diff/impact and getting approval.
- Prefer account-wide environment alignment when it is the root cause of Mac Catalyst/MAUI issues.

## Workflow

1. **Preflight**
   - Confirm repo root and git state with read-only commands.
   - Detect OS, architecture, shell, package manager, and editor command (`code` or `code-insiders`).
   - Read `global.json`, `.vscode/extensions.json`, `.mcp.json`, `.codex/config.toml`, package manifests, Python config, and database setup docs.

2. **Inventory**
   - Check: Git, GitHub CLI, .NET SDK 10.0.100 or compatible roll-forward, .NET workloads, Node/npm, Rust/Cargo, Tauri prerequisites, Python, Poetry, Docker, SQL Server tools if present, Azure CLI, VS Code extensions, MCP server availability, Ollama.
   - On macOS, check Xcode/Command Line Tools and selected `DEVELOPER_DIR`.
   - On Windows, check Visual Studio Build Tools or Visual Studio workloads needed for .NET, MAUI, and Tauri.

3. **Plan and Approval**
   - Group missing items by required, recommended, and optional.
   - Show exact install commands per platform.
   - Ask for one explicit approval to install required/recommended tools.
   - Ask separately before optional tools, Docker container startup, database provisioning, and machine/account-wide environment changes.

4. **Install and Configure**
   - Use package managers where possible: `winget` on Windows, Homebrew on macOS.
   - Use repo package locks with `npm ci`, not `npm install`, for Node workspaces.
   - Use Poetry for `2-Application/local-processing-service`.
   - Install VS Code extensions from `.vscode/extensions.json`.
   - Configure MCP from `.mcp.json` and `.codex/config.toml`; do not preserve stale machine-specific paths.
   - Use the existing database setup CLI for schema/user provisioning.

5. **Validate**
   - Run focused validation for each stack.
   - Report installed versions, remaining manual steps, and any blocked items.
   - Do not claim the environment is ready unless validation passes or blocked items are explicitly listed.
