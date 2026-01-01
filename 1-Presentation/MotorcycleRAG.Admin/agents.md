# Agent Context: MotorcycleRAG.Admin

**Mandatory Compliance**: This agent MUST adhere to the [MAUI Architecture Guidelines](6-Docs/MAUI_ARCHITECT.md) (Version 1.0.0).

## Invariant Rules
- **Layer**: 1-Presentation (Admin Desktop App).
- **Stack**: .NET 10.0 MAUI (Windows-first).
- **Architecture**: **MVVM** pattern using CommunityToolkit.Maui.
- **Dependency Rule**: Can depend on `Application` and `Base`.
- **UI**: Pure XAML Views with compiled bindings (`x:DataType`).
- **ViewModels**: Inherit from `ObservableObject`. Use `[ObservableProperty]` and `[RelayCommand]`.
- **Navigation**: Shell Navigation exclusively via `INavigationService`.
- **Security**: [Security Rule: Active]. OIDC/OAuth2 with PKCE via MSAL.NET.
- **Resilience**: Implement connectivity checks and retry policies using `Microsoft.Extensions.Http.Resilience`.
- **Dependency Injection**: All services and ViewModels must be registered in `MauiProgram.cs`.
- **Configuration**: Use `ISettingsService` for settings, never `Preferences.Get()` directly.
- **Data Processing**: Local PDF/CSV chunking via `PdfPig`/`CsvHelper` through `ILocalProcessingService`.
- **Caching**: Implement Cache-Aside pattern with SQLite/SecureStorage.
- **Testing**: ViewModels must be testable with mocked services.
- **UI Standards**: Use built-in MAUI controls with CommunityToolkit.Maui enhancements.
- **Theming**: Define shared resources in `App.xaml` or `Resources/Styles`.
- **Responsive Design**: Handle `DeviceIdiom` differences in `AppShell`.
- **Offline Support**: Cache chat history locally with SQLite sync on reconnect.
- **Authentication**: Store tokens securely via `ISecureStorage` through `ISettingsService`.

## MAUI-Specific Requirements

### MVVM Implementation
- **Views**: Must use `x:DataType` for compiled bindings
- **ViewModels**: Must inherit from `ObservableObject` and use source generators
- **Commands**: Use `[RelayCommand]` or `[RelayCommand(IncludeCancelCommand = true)]`
- **No UI References**: ViewModels must never reference UI controls directly

### Navigation Architecture
- **Service Pattern**: Use `INavigationService` interface, never call `Shell.Current.GoToAsync` directly
- **Route Registration**: All routes must be registered in `AppShell.xaml.cs`
- **Data Passing**: Use `[QueryProperty]` attributes or `IQueryAttributable` interface

### Data & Connectivity
- **Resilience Patterns**: All HTTP calls must implement retry and circuit breaker policies
- **Connectivity Check**: Always verify `Connectivity.Current.NetworkAccess == NetworkAccess.Internet` before network operations
- **Cache-Aside Pattern**: Check local storage first, fetch from API if missing/stale, update local storage

### Configuration & Settings
- **Settings Service**: Implement `ISettingsService` wrapping `Preferences`/`SecureStorage`
- **No Direct Preferences**: Never use `Preferences.Get()` directly in ViewModels
- **Token Storage**: Use `ISecureStorage` for authentication tokens

### Local Processing (Admin App)
- **PDF/CSV Processing**: Use `PdfPig` for PDF extraction and `CsvHelper` for CSV parsing
- **Embeddings**: Use `ONNX Runtime` locally or call API
- **Service Architecture**: Implement `ILocalProcessingService` for chunking/embedding logic

### UI Standards
- **Controls**: Prefer built-in MAUI controls (`Grid`, `StackLayout`, `CollectionView`)
- **Toolkit Enhancements**: Use CommunityToolkit.Maui for behaviors, converters, and layouts
- **Theming**: Define semantic colors in `App.xaml` or `Resources/Styles`
- **Responsive Design**: Use `Grid` for adaptive layouts, handle `DeviceIdiom` in `AppShell`

### Testing Requirements
- **Unit Testing**: Test ViewModels, Services, and Logic (not Views)
- **Mocking**: Use `Moq` or `NSubstitute` for dependencies
- **Test Coverage**: Verify state changes, command execution, and service calls

### Checklist for Compliance
- [ ] MVVM pattern with CommunityToolkit.Maui source generators
- [ ] Shell Navigation via `INavigationService`
- [ ] Dependency Injection in `MauiProgram.cs`
- [ ] Resilience patterns for HTTP calls
- [ ] Connectivity checks before network operations
- [ ] `ISettingsService` for configuration
- [ ] Local processing via `ILocalProcessingService`
- [ ] Unit tests for ViewModel logic
- [ ] Shared resources for theming
- [ ] Responsive design considerations

## Workflow Skills
- **Run**: `dotnet build -t:Run -f net10.0-windows10.0.19041.0`
- **Test**: `dotnet test`

Once you have read the Securiy rule you **MUST** include `[I Read the Admin Instructions]` at the beginning of your Task 