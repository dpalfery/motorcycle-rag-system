# Inventory

Read these files before planning installs:

- `AGENTS.md`
- `global.json`
- `MotorcycleRAG.sln`
- `Directory.Build.props`
- `NuGet.config`
- `.vscode/extensions.json`
- `.mcp.json`
- `.codex/config.toml`
- `docker-compose.yml`
- `1-Presentation/MotorcycleRag.WebUI/package.json`
- `1-Presentation/MotorcycleRAG.AdminDesktop/package.json`
- `1-Presentation/MotorcycleRAG.AdminDesktop/src-tauri/Cargo.toml`
- `2-Application/local-processing-service/pyproject.toml`
- `7-Deployment/DbSetup/README.md`
- `7-Deployment/DbSetup/MotorcycleRAG.DbSetup/MotorcycleRAG.DbSetup.csproj`
- `7-Deployment/infrastructure/Pulumi.yaml`

Use these discovery commands when available:

```sh
dotnet --info
dotnet workload list
node --version
npm --version
python --version
python3 --version
poetry --version
rustc --version
cargo --version
docker --version
docker compose version
gh --version
ollama --version
```

Editor checks:

```sh
code --version
code-insiders --version
code --list-extensions
code-insiders --list-extensions
```

macOS-specific checks:

```sh
sw_vers
uname -m
xcode-select -p
xcodebuild -version
launchctl getenv DEVELOPER_DIR
```

Windows-specific checks:

```powershell
winget --version
where git
where dotnet
where node
where npm
where python
where py
where cargo
where docker
where gh
where az
```

Required tool families inferred from the repo:

- .NET SDK `10.0.100` compatible with `global.json`.
- .NET workloads for MAUI/mobile/Mac Catalyst when working on mobile or Mac desktop targets.
- Node/npm for WebUI, AdminDesktop, Playwright MCP, and frontend tests.
- Rust/Cargo and Tauri prerequisites for `MotorcycleRAG.AdminDesktop`.
- Python 3.9+ and Poetry for `2-Application/local-processing-service`.
- Docker Desktop or equivalent local Docker engine for SQL Server development database.
- GitHub CLI for repo/GitHub workflows.
- Azure CLI for read-only diagnostics only.
- VS Code/VS Code Insiders extensions from `.vscode/extensions.json`.
- MCP servers: context7, microsoft-learn, playwright, azure.
- Ollama for the local processing service when local model flows are needed.
- Pulumi CLI only for developers who need to inspect or author IaC. Agents must never deploy with it.

For Azure CLI, use `command -v az` or `where az` to check install presence. Do not run any `az` command until the active subscription can be verified against the allowlist.
