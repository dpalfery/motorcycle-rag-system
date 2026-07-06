---
name: delegate-maui
description: Forces delegation to the maui-dev agent for .NET MAUI tasks.
---

# MAUI Delegation Protocol

**Trigger:**
This skill MUST be used whenever the user requests:
- Writing or editing .NET MAUI code (`.xaml`, `.xaml.cs` files).
- Creating or modifying MAUI ViewModels, Pages, or Services.
- Working on the Mobile App at `1-Presentation/MotorcycleRAG.MobileApp`.
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
- **Project**: `1-Presentation/MotorcycleRAG.MobileApp`
- **Target**: Windows + MacCatalyst (.NET 10.0)
- **Pattern**: MVVM with CommunityToolkit.Mvvm source generators
- **Navigation**: Shell with FlyoutItems and absolute routes
- **DI**: ViewModels and Pages registered as transient, services as singleton

### File Structure
- **ViewModels**: `/ViewModels/` — Inherit `ObservableObject`, use `[ObservableProperty]` and `[RelayCommand]`
- **Pages**: `/Pages/` — XAML pages with code-behind, one per ViewModel
- **Services**: `/Services/` — Abstracted behind interfaces
- **Shell**: `AppShell.xaml`
