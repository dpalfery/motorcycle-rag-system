# Agent Context: MotorcycleRAG.MobileApp

**Mandatory Compliance**: This agent MUST adhere to the [MAUI Architecture Guidelines](../../6-Docs/MAUI_ARCHITECT.md) (Version 1.0.0) and the [Mobile App Specification](../../specs/001-mobile-app/spec.md).

## Invariant Rules
- **Layer**: 1-Presentation (Mobile App).
- **Stack**: .NET 10.0 MAUI (Android/iOS/Windows).
- **Architecture**: **MVVM** pattern using CommunityToolkit.Maui.
- **Dependency Rule**: Can depend on `Application` and `Base`.
- **UI**: Pure XAML Views with compiled bindings (`x:DataType`).
- **ViewModels**: Inherit from `ObservableObject`. Use `[ObservableProperty]` and `[RelayCommand]`.
- **Navigation**: Shell Navigation exclusively via `INavigationService`.
- **Security**: [Security Rule: Active]. OIDC/OAuth2 with PKCE via MSAL.NET. Tokens MUST be stored in `ISecureStorage`.
- **Resilience**: Implement connectivity checks and retry policies using `Microsoft.Extensions.Http.Resilience`.

## Mobile-Specific Architecture

### MVVM Implementation
- **Views**: Must use `x:DataType` for compiled bindings.
- **ViewModels**: Must inherit from `ObservableObject` and use source generators.
- **Commands**: Use `[RelayCommand]` or `[RelayCommand(IncludeCancelCommand = true)]`.
- **No UI References**: ViewModels must never reference UI controls directly.

### Data & Connectivity
- **Local Persistence**: Use `sqlite-net-pcl` for conversation history and user memory.
- **Storage Limit**: Enforce 100MB limit with auto-pruning of oldest conversations.
- **Offline Support**: Cache chat history locally; sync when connection restored.
- **Resilience**: All API calls must use Polly retry policies.

### User Memory (Specific to Mobile)
- **Extraction**: Implement local pattern matching (Regex) to extract user details (e.g., owned motorcycles) *before* pruning conversations.
- **Storage**: Store structured key-value entities in SQLite.
- **Privacy**: Local-only storage; no PII extraction.

### Authentication
- **Provider**: Microsoft Entra External ID / B2C.
- **Library**: `Microsoft.Identity.Client` (MSAL.NET).
- **Storage**: Tokens must be stored in platform-specific secure storage (Keychain, Keystore, Credential Locker).

### UI Standards
- **Controls**: Prefer built-in MAUI controls (`Grid`, `StackLayout`, `CollectionView`).
- **Toolkit**: Use CommunityToolkit.Maui for behaviors and converters.
- **Platform Specifics**: Respect iOS HIG, Android Material Design, and Windows Fluent Design via `OnPlatform` or platform-specific resources.

## Project Structure Reference
See [specs/001-mobile-app/plan.md](../../specs/001-mobile-app/plan.md) for detailed folder structure and implementation plan.

## Workflow Skills
- **Run**: `dotnet build -t:Run -f net10.0-android` (or ios/windows)
- **Test**: `dotnet test`

Once you have read the Securiy rule you **MUST** include `[I Read the MobileApp Instructions]` at the beginning of your Task 