# MotorcycleRAG Admin Application

Windows-first MAUI desktop application for administrative operations: data ingestion, pipeline management, and system configuration.

## Build Requirements

### Windows Environment Required

This is a .NET MAUI application targeting Windows App SDK. **Windows is required for building.**

**WSL2 Limitation**:
- The XAML compiler (`XamlCompiler.exe`) is a Windows-only .NET Framework tool
- Builds in WSL2 will fail at XAML compilation phase with error `MSB3073: XamlCompiler.exe exited with code 1`
- This is expected behavior, not a bug

**Developer Workflow**:
- Code editing: WSL2 or Windows
- Building/Running: Windows only
- Testing: Windows only

### Build Commands

```powershell
# From Windows (PowerShell or CMD)
dotnet build -c Debug -p:Platform="Any CPU"

# Run the application
dotnet run --project 1-Presentation/MotorcycleRAG.Admin
```

### Prerequisites

- Windows 10 version 19041.0 or higher
- .NET 10.0 SDK
- Windows App SDK 1.7 (installed automatically via NuGet)

## Security - Token Storage

### Authentication Token Caching

- **Package**: `Microsoft.Identity.Client.Extensions.Msal` v4.81.0
- **Storage Location**: Windows Credential Manager
- **Encryption**: DPAPI (Data Protection API)
- **Lifetime**: Tokens respect MSAL refresh token policies

### Why This Dependency?

- Base `Microsoft.Identity.Client` provides in-memory caching only
- Extensions package enables persistent, encrypted storage
- Admins remain authenticated across app restarts
- Complies with OWASP ASVS Level 2 credential storage requirements

### Token Cleanup

To clear cached tokens manually:

1. Open Windows Credential Manager → Windows Credentials
2. Find entries prefixed with `msal.cache` or `MotorcycleRAG.Admin`
3. Delete entries to force re-authentication on next app launch

Alternatively, delete the cache file directly:
```powershell
Remove-Item "$env:LOCALAPPDATA\MotorcycleRAG.Admin\msal_cache.dat" -ErrorAction SilentlyContinue
```

## Architecture

This project follows Clean Architecture principles:

- **Layer**: 1-Presentation (MAUI UI)
- **Dependencies**: Application layer interfaces, Domain models
- **Pattern**: MVVM via CommunityToolkit.Mvvm
- **Navigation**: Shell-based with INavigationService abstraction

### Key Components

- **ViewModels**: Business logic and state management
- **Services**: Authentication, API client, settings management
- **Processing**: Local PDF/CSV chunking and ONNX embedding generation

## Development

### Project Structure

```
MotorcycleRAG.Admin/
├── Pages/           # XAML pages (UI)
├── ViewModels/      # MVVM view models
├── Services/        # Authentication, API, settings
├── Processing/      # PDF/CSV chunking, ONNX embeddings
├── Utilities/       # Helpers and extensions
└── Resources/       # Images, fonts, assets
```

### Code Quality

Target: 0 warnings, 0 errors per project constitution.

**Known Technical Debt**:
- 5 build warnings (pre-existing, tracked separately)
- See GitHub issue #[TBD] for remediation plan

## Related Documentation

- Global architecture rules: `/AGENTS.md`
- System specification: `/specs/001-system-spec/spec.md`
- Environment variables: `/6-Docs/environment-variables.md`
