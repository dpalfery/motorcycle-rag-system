# Windows Setup

Default to Windows-native setup. Use WSL only if the user explicitly wants a Linux development path.

## Package Manager

Prefer `winget`. If unavailable, ask before installing or using Chocolatey/Scoop.

Typical required installs:

```powershell
winget install --id Git.Git -e
winget install --id GitHub.cli -e
winget install --id Microsoft.DotNet.SDK.10 -e
winget install --id OpenJS.NodeJS.LTS -e
winget install --id Rustlang.Rustup -e
winget install --id Python.Python.3.12 -e
winget install --id Python.Poetry -e
winget install --id Docker.DockerDesktop -e
winget install --id Microsoft.AzureCLI -e
winget install --id Microsoft.VisualStudioCode.Insiders -e
winget install --id Ollama.Ollama -e
```

Visual Studio / Build Tools are required for some .NET, MAUI, and Tauri work. Prefer Visual Studio Installer workloads, not ad hoc SDK copying:

- `.NET desktop development`
- `Desktop development with C++`
- `Universal Windows Platform development` when targeting Windows app surfaces
- `.NET Multi-platform App UI development` when targeting MAUI

Ask before installing Visual Studio workloads because this is a large machine-level change.

## .NET

After installing the SDK:

```powershell
dotnet --info
dotnet workload restore MotorcycleRAG.sln
```

If workload restore fails, inspect the failure. Do not replace it with a workaround unless the user approves.

## Node

Use lockfiles:

```powershell
Push-Location 1-Presentation/MotorcycleRag.WebUI
npm ci
Pop-Location

Push-Location 1-Presentation/MotorcycleRAG.AdminDesktop
npm ci
Pop-Location
```

## Rust/Tauri

After `rustup` install, reopen the shell or refresh PATH, then validate:

```powershell
rustc --version
cargo --version
```

Install WebView2 Runtime if it is missing. Tauri 2 also needs C++ build tools from Visual Studio.

## Python

Use Poetry from the project directory:

```powershell
Push-Location 2-Application/local-processing-service
poetry env use python
poetry install
Pop-Location
```

Do not create `.env` files. Do not persist C#/.NET application secrets in user-level environment variables.
