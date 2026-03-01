# MAUI Delegation Protocol

**Trigger:**
This skill MUST be used whenever the user requests:
- Writing or editing .NET MAUI code (`.xaml`, `.xaml.cs` files).
- Creating or modifying MAUI ViewModels, Pages, or Services.
- Working on the Admin App at `1-Presentation/MotorcycleRAG.Admin`.
- Shell navigation, flyout configuration, or MAUI-specific UI patterns.
- CommunityToolkit.Mvvm source generator usage (`[ObservableProperty]`, `[RelayCommand]`).

**Procedure:**
1. **DO NOT** write the code yourself.
2. Immediately invoke the `maui-dev` sub-agent.
3. Pass the full user request and any relevant file context to the sub-agent.
4. Wait for the sub-agent to complete the task.
5. Report the sub-agent's results back to the user.

**Example Hand-off:**
> "This involves MAUI UI work. I will have the `MAUI Specialist` handle this to ensure correct MVVM patterns and Shell navigation."

---

## Project Conventions (Pass to Sub-Agent)

### Architecture
- **Project**: `1-Presentation/MotorcycleRAG.Admin`
- **Target**: Windows-only (.NET 10.0)
- **Pattern**: MVVM with CommunityToolkit.Mvvm v8.4.0 source generators
- **Navigation**: Shell with FlyoutItems and absolute routes (e.g., `//settings/settingspage`)
- **DI**: ViewModels and Pages registered as transient, services as singleton

### File Structure
- **ViewModels**: `/ViewModels/` — Inherit `ObservableObject`, use `[ObservableProperty]` and `[RelayCommand]`
- **Pages**: `/Pages/` — XAML pages with code-behind, one per ViewModel
- **Services**: `/Services/` — Abstracted behind interfaces (`INavigationService`, `ISettingsService`, `IConfigurationStateService`, `IAdminAuthService`)
- **Processing**: `/Processing/` — Local processing services (`PdfChunker`, `CsvChunker`, `OnnxEmbeddingService`)
- **Shell**: `AppShell.xaml` — 6 FlyoutItems (Dashboard, Upload, Jobs, WebSources, Tools, Settings)

### Key Patterns
- **MVVM**: Use `ObservableObject` base, `[ObservableProperty]` for bindable properties, `[RelayCommand]` for commands — never implement `INotifyPropertyChanged` manually
- **Navigation**: Use `INavigationService` wrapping Shell navigation, not direct `Shell.Current.GoToAsync()`
- **Settings**: Use `ISettingsService` wrapping `Preferences`, not direct `Preferences.Get/Set`
- **Auth**: MSAL-based with encrypted token storage via `IAdminAuthService`
- **HTTP**: Use `Microsoft.Extensions.Http.Resilience` for retry/circuit breaker policies on HttpClient
- **Clean Architecture**: References Domain/Contracts layers only — no direct Persistence references

### MUST NOT
- Use EF Core or direct database access from the MAUI project
- Skip service interface abstractions (all services must have an `I*` interface)
- Use direct `Shell.Current` calls — go through `INavigationService`
- Store secrets in preferences or code — use environment variables or MSAL secure storage
