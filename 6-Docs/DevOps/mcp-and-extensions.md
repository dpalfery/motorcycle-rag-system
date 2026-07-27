---
id: devops/mcp-and-extensions
title: MCP and Extensions
doc-type: reference
status: current
owner: Developer-experience maintainers
last-reviewed: 2026-07-21
code-refs: []
api-endpoints: []
decided-by: []
supersedes: []
---
# MCP and Extensions

Part of the [Developer Machine Setup Standard](developer-setup-standard.md).

Configure editor extensions and MCP servers after core tooling exists.

## VS Code Extensions

Install recommendations from `.vscode/extensions.json`.

Choose the command that matches the user's editor:

```sh
code --install-extension github.vscode-pull-request-github
code --install-extension ms-dotnettools.csharp
code --install-extension ms-dotnettools.csdevkit
code --install-extension ms-dotnettools.dotnet-maui
code --install-extension ms-mssql.mssql
code --install-extension ms-python.python
code --install-extension ms-python.vscode-pylance
code --install-extension ms-python.vscode-python-envs
code --install-extension ms-toolsai.jupyter
code --install-extension ms-azuretools.vscode-azure-github-copilot
code --install-extension ms-azuretools.vscode-azure-mcp-server
code --install-extension ms-azuretools.vscode-azureresourcegroups
code --install-extension ms-azuretools.vscode-cosmosdb
code --install-extension ms-windows-ai-studio.windows-ai-studio
code --install-extension teamsdevapp.vscode-ai-foundry
```

Use `code-insiders` instead of `code` for VS Code Insiders.

## MCP Servers

Required project MCP servers:

- `context7`: HTTP, `https://mcp.context7.com/mcp`
- `microsoft-learn`: HTTP, `https://learn.microsoft.com/api/mcp`
- `playwright`: stdio, `npx -y @playwright/mcp@latest` in current repo config
- `azure`: stdio, provided by the Azure MCP VS Code extension, read-only mode

The repo has `.mcp.json` and `.codex/config.toml`. Treat them as source material, but do not preserve stale machine-specific paths from another computer.

For Azure MCP:

1. Install `ms-azuretools.vscode-azure-mcp-server`.
2. Discover the extension server binary path on the current machine.
3. Configure it with `server start --mode namespace --read-only`.
4. Set `AZURE_MCP_COLLECT_TELEMETRY=false`.
5. Keep Azure tools read-only.

Before any `az` command:

```sh
az account show --query id -o tsv
```

Proceed only if the subscription ID is `5df33f46-892f-4dc1-9d0c-701464efd7e5`.

## Playwright MCP

Playwright MCP depends on Node/npm:

```sh
npx -y @playwright/mcp@<approved-version> --help
```

The current repo config uses `@latest`, which is not reproducible. Do not execute `@latest` silently during bootstrap. Present options to the user:

- use the repo config exactly as-is with explicit approval
- pin a reviewed version and update the MCP config after approval
- skip Playwright MCP until the user chooses a version

Any `npx` execution uses the network if the package is not cached; ask before running it during setup.
