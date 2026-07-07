# Validation

Run validation in layers. Stop on root-cause failures and fix them instead of masking failures with workarounds.

## Core Tools

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

For Azure CLI validation, first check whether the binary exists with `command -v az` or `where az`. Do not run `az --version` or any other `az` command until the active subscription is verified against the allowlist.

## Dependency Restore

```sh
dotnet restore MotorcycleRAG.sln
(cd 1-Presentation/MotorcycleRag.WebUI && npm ci)
(cd 1-Presentation/MotorcycleRAG.AdminDesktop && npm ci)
(cd 2-Application/local-processing-service && poetry install)
```

## Builds and Tests

Use focused checks first:

```sh
dotnet build MotorcycleRAG.sln -c Debug
dotnet test MotorcycleRAG.sln
(cd 1-Presentation/MotorcycleRag.WebUI && npm run build)
(cd 1-Presentation/MotorcycleRAG.AdminDesktop && npm run build)
(cd 2-Application/local-processing-service && poetry run pytest)
```

For AdminDesktop Tauri:

```sh
(cd 1-Presentation/MotorcycleRAG.AdminDesktop && npm run tauri -- --version)
```

For Mac Catalyst/MAUI validation, do not claim VS Code F5 is fixed from CLI build alone. Confirm the actual VS Code launch path when the user asks for debug readiness.

## Database

After approved local database startup/provisioning:

```sh
docker ps
dotnet run --project 7-Deployment/DbSetup/MotorcycleRAG.DbSetup -- --help
```

Run the interactive database setup CLI if schema provisioning has not been completed:

```sh
cd 7-Deployment/DbSetup/MotorcycleRAG.DbSetup
dotnet run -- --env-vars-in-proc
```

## Final Report

Include:

- OS and architecture
- installed tool versions
- installed VS Code extensions
- MCP servers configured or blocked
- database container/provisioning status
- validation commands run and results
- remaining manual steps or approvals needed

Do not say "ready" unless required validations have passed.
