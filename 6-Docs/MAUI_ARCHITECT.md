# MAUI Architecture Guidelines: Motorcycle RAG System

**Version**: 1.0.0
**Status**: Active
**Mandatory Compliance**: All MAUI development (Admin & Mobile) MUST adhere to this document.

## 1. Executive Summary

This document defines the "Golden Path" architecture for the Motorcycle RAG System's .NET MAUI applications. It synthesizes Microsoft's Enterprise Application Patterns, the .NET MAUI Community Toolkit best practices, and the specific requirements of the Motorcycle RAG System.

**Core Philosophy**:
- **MVVM**: Strict Model-View-ViewModel pattern using `CommunityToolkit.Mvvm`.
- **Shell-First**: Exclusive use of Shell for navigation and structure.
- **Dependency Injection**: All services and ViewModels must be registered in DI.
- **Resilience**: Robust handling of connectivity and API failures.
- **Platform Native**: Use built-in MAUI controls augmented by the Community Toolkit; avoid heavy third-party UI libraries unless absolutely necessary.

---

## 2. Core Patterns

### 2.1 MVVM (Model-View-ViewModel)

We strictly use the **CommunityToolkit.Mvvm** source generators to reduce boilerplate.

#### **Views (`/Views` or `/Pages`)**
- **Role**: Define structure and layout.
- **Rules**:
  - Pure XAML preferred.
  - Minimal code-behind (only for UI logic not expressible in XAML/Behaviors).
  - **MUST** include `x:DataType` pointing to the specific ViewModel for compiled bindings.
  - **MUST** use `ContentPage` or `ContentView`.

#### **ViewModels (`/ViewModels`)**
- **Role**: Presentation logic, state management, and orchestration.
- **Rules**:
  - **MUST** inherit from `ObservableObject`.
  - **MUST** use `[ObservableProperty]` for bindable state.
  - **MUST** use `[RelayCommand]` or `[RelayCommand(IncludeCancelCommand = true)]` for actions.
  - **NEVER** reference UI controls directly (e.g., `Button`, `Label`).
  - **NEVER** instantiate services directly (`new Service()`); use Constructor Injection.

#### **Models (`/Models`)**
- **Role**: Data transfer objects (DTOs) and domain entities.
- **Rules**:
  - Pure classes or records.
  - Should not contain business logic (unless rich domain models).
  - Should not depend on UI types.

### 2.2 Dependency Injection (DI)

All application components must be registered in `MauiProgram.cs`.

**Registration Pattern**:
```csharp
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder
        .UseMauiApp<App>()
        .UseMauiCommunityToolkit();

    // 1. Core Services (Singletons)
    builder.Services.AddSingleton<ISettingsService, SettingsService>();
    builder.Services.AddSingleton<INavigationService, MauiNavigationService>();
    builder.Services.AddSingleton<IAuthenticationState, AuthenticationState>();

    // 2. Data Services (HTTP Clients)
    // Use Typed Clients with Resilience Handlers
    builder.Services.AddHttpClient<ICatalogService, CatalogService>(client => 
        client.BaseAddress = new Uri(GlobalSettings.BaseEndpoint))
        .AddStandardResilienceHandler();

    // 3. ViewModels & Views (Transient)
    // Register as Transient to ensure fresh state on navigation (unless state preservation is explicitly required)
    builder.Services.AddTransient<LoginViewModel>();
    builder.Services.AddTransient<LoginView>();

    return builder.Build();
}
```

---

## 3. Navigation Architecture

We use **Shell Navigation** exclusively, wrapped in a service to maintain ViewModel testability.

### 3.1 The Navigation Service
We do NOT call `Shell.Current.GoToAsync` directly in ViewModels. We use `INavigationService`.

**Interface**:
```csharp
public interface INavigationService
{
    Task NavigateToAsync(string route, IDictionary<string, object>? parameters = null);
    Task GoBackAsync();
}
```

### 3.2 Route Registration
Routes must be registered in `AppShell.xaml.cs` or `MauiProgram.cs` to be accessible via string-based routing.

```csharp
Routing.RegisterRoute(nameof(DetailPage), typeof(DetailPage));
```

### 3.3 Passing Data
Use the `IQueryAttributable` interface or `[QueryProperty]` attributes in ViewModels to receive data.

```csharp
[QueryProperty(nameof(UserId), "userId")]
public partial class ProfileViewModel : ObservableObject
{
    [ObservableProperty]
    private string _userId;
}
```

---

## 4. Data & Connectivity

### 4.1 Resilience
All remote data access MUST implement resilience patterns using `Microsoft.Extensions.Http.Resilience`.
- **Retry**: Handle transient errors (408, 503) automatically.
- **Circuit Breaker**: Prevent cascading failures during outages.

### 4.2 Connectivity Check
Always check connectivity before initiating network requests.

```csharp
if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
{
    // Handle offline state (e.g., show cached data or alert user)
    return;
}
```

### 4.3 Caching
Implement the **Cache-Aside** pattern.
1. Check local storage (SQLite or SecureStorage).
2. If missing/stale, fetch from API.
3. Update local storage.

---

## 5. Configuration & Settings

**NEVER** use `Preferences.Get()` directly in ViewModels.

**Pattern**:
1. Define `ISettingsService`.
2. Implement `SettingsService` wrapping `Microsoft.Maui.Storage.Preferences` (or `SecureStorage` for secrets).
3. Inject `ISettingsService` into ViewModels.

**Why?** This allows Unit Tests to run without a UI thread or platform context by mocking `ISettingsService`.

---

## 6. Authentication

- **Standard**: OIDC/OAuth2 with PKCE.
- **Admin App**: Microsoft Entra ID (Workforce).
- **Mobile App**: Microsoft Entra External ID / B2C (Customer/Social).
- **Library**: `Microsoft.Identity.Client` (MSAL.NET) for native broker support.
- **Token Storage**: **MUST** use `ISecureStorage` (via `ISettingsService`). NEVER store tokens in plain text preferences.

---

## 7. UI Standards

### 7.1 Controls
- Prefer **Built-in MAUI Controls** (`Grid`, `StackLayout`, `CollectionView`, `Label`, `Button`).
- Use **CommunityToolkit.Maui** for:
  - Behaviors (`EventToCommandBehavior`, `TouchBehavior`).
  - Converters (`BoolToObjectConverter`, `InvertedBoolConverter`).
  - Layouts (`UniformItemsLayout`).

### 7.2 Theming
- Define shared resources in `App.xaml` or `Resources/Styles`.
- Use Semantic Colors (`Primary`, `Secondary`, `Error`) rather than hex codes in Views.

### 7.3 Responsive Design
- Use `Grid` for adaptive layouts.
- Use `FlexLayout` or `StateManager` for complex responsive needs.
- Handle `DeviceIdiom` (Phone vs Desktop) in `AppShell` for Flyout behavior.

---

## 8. Unit Testing Strategy

Code **MUST** be designed for testability.

- **Scope**: Test ViewModels, Services, and Logic. Do NOT test Views (UI).
- **Mocking**: Use `Moq` or `NSubstitute`.
- **Rules**:
  - ViewModels must accept Interfaces in the constructor.
  - Tests should verify:
    - State changes (`PropertyChanged`).
    - Command execution.
    - Service calls (Verify `INavigationService.NavigateToAsync` was called).

---

## 9. Specific Requirements for Motorcycle RAG

### 9.1 Local Processing (Admin App)
- **PDF/CSV Chunking**: Must be performed locally using `PdfPig` / `CsvHelper`.
- **Embeddings**: Must use `ONNX Runtime` locally if configured, or call API.
- **Architecture**: Logic for chunking/embedding must reside in `Services` (e.g., `ILocalProcessingService`), not in the View code-behind.

### 9.2 Tooling (Admin App)
- **MCP Configuration**: Managed via `ISettingsService` and synced with API.

### 9.3 Offline Support (Mobile App)
- **Chat History**: Must be cached locally (SQLite).
- **Sync**: Sync with server when connection is restored.

---

## 10. Checklist for New Features

Before submitting a PR, verify:
- [ ] **MVVM**: Logic is in ViewModel, not Code-Behind.
- [ ] **Bindings**: `x:DataType` is set; Compiled Bindings are active.
- [ ] **DI**: All new Services/ViewModels registered in `MauiProgram.cs`.
- [ ] **Resilience**: HTTP calls have retry/circuit-breaker policies.
- [ ] **Navigation**: Using `INavigationService`, not `Shell.Current`.
- [ ] **Tests**: Unit tests added for new ViewModel logic.
- [ ] **Styles**: Using shared resources, no hardcoded colors.