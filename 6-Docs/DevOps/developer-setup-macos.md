# macOS Setup

Support Apple Silicon and Intel macOS. Prefer Homebrew for CLI tools.

## Package Manager

Install Homebrew only with explicit user approval if it is missing.

Typical required installs:

```sh
brew install git gh node rust python poetry azure-cli ollama
brew install --cask docker visual-studio-code-insiders
```

If the user prefers stable VS Code, install `visual-studio-code` instead of Insiders.

## Xcode and MAUI

For Mac Catalyst/MAUI work, Xcode matters as much as the .NET workload. Check:

```sh
xcode-select -p
xcodebuild -version
dotnet workload list
launchctl getenv DEVELOPER_DIR
```

If Xcode is missing, ask the user to install it from the App Store or Apple Developer downloads. Do not silently switch Xcode versions.

If the selected Xcode does not match the installed .NET MacCatalyst/iOS workload pack requirements, present options:

- install/update the matching .NET workload pack
- switch selected Xcode with `sudo xcode-select -s ...`
- set account-wide `DEVELOPER_DIR` with `launchctl setenv DEVELOPER_DIR ...`

Wait for approval before changing `xcode-select` or `launchctl` state.

After Xcode environment changes intended for GUI apps, fully quit and relaunch VS Code Insiders. Window reload is not enough.

## .NET

Install the SDK required by `global.json`, then run:

```sh
dotnet --info
dotnet workload restore MotorcycleRAG.sln
```

## Node

Use lockfiles:

```sh
(cd 1-Presentation/MotorcycleRag.WebUI && npm ci)
(cd 1-Presentation/MotorcycleRAG.AdminDesktop && npm ci)
```

## Rust/Tauri

Validate Rust after install:

```sh
rustc --version
cargo --version
```

Tauri on macOS requires Xcode Command Line Tools at minimum:

```sh
xcode-select --install
```

Ask before running it because it launches a machine-level installer.

## Python

Use Poetry from the project directory:

```sh
(cd 2-Application/local-processing-service && poetry env use python3 && poetry install)
```

Do not create `.env` files.
