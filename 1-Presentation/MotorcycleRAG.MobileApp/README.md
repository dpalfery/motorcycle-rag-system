# MotorcycleRAG Mobile App

A .NET MAUI mobile application for the Motorcycle RAG System.

## Features

- **Chat Interface**: Ask natural language questions about motorcycles.
- **Multi-Turn Conversations**: Context-aware follow-up questions.
- **User Memory**: Personalizes responses based on your profile and history.
- **Conversation Management**: Search, delete, and resume conversations.
- **Cross-Platform**: Runs on iOS, Android, and Windows.

## Prerequisites

- .NET 8.0 SDK or later
- Visual Studio 2022 (17.8+) with .NET MAUI workload
- Android Emulator or Device (for Android)
- Mac with Xcode (for iOS)

## Setup

1.  **Clone the repository**:
    ```bash
    git clone <repo-url>
    cd motoRagApp
    ```

2.  **Configure Settings**:
    - Open `1-Presentation/MotorcycleRAG.MobileApp/appsettings.json`.
    - Update `ApiSettings:BaseUrl` to point to your API backend.
    - Update `Authentication:ClientId` and `TenantId` with your Azure AD B2C details.

3.  **Build and Run**:
    - Open `MotorcycleRAG.sln` in Visual Studio.
    - Set `MotorcycleRAG.MobileApp` as the startup project.
    - Select your target device (Android Emulator, Windows Machine, etc.).
    - Press F5 to run.

## Architecture

- **MVVM Pattern**: Uses CommunityToolkit.Mvvm.
- **UI Components**: Material.Components.Maui for Material Design 3.
- **Dependency Injection**: Configured in `MauiProgram.cs`.
- **Persistence**: SQLite for local storage of conversations and messages.
- **Resilience**: Polly policies for API retries and circuit breaking.

## UI Framework

This app uses **Material.Components.Maui** (v0.2.2-preview) to implement Material Design 3 (Material You) components across all platforms.

### Key Components
- **Material Buttons**: Elevated, filled, outlined, text variants
- **Material Cards**: For message bubbles and content containers
- **Material TextFields**: With floating labels and validation
- **Material Navigation**: Bottom nav and app bars
- **Dynamic Theming**: Automatic light/dark mode support

### Usage Example

```xml
<ContentPage xmlns:material="clr-namespace:Material.Components.Maui.Core;assembly=Material.Components.Maui">
    <material:Button Text="Send" 
                    Style="{StaticResource ElevatedButton}"
                    Command="{Binding SendCommand}" />
</ContentPage>
```

For more details, see `/specs/001-mobile-app/research.md` section 9.

## Testing

Run unit tests from the `5-Test/MotorcycleRAG.MobileApp.Tests` project.

```bash
dotnet test 5-Test/MotorcycleRAG.MobileApp.Tests
```

## Troubleshooting

- **Authentication Issues**: Ensure the redirect URIs are correctly configured in Azure AD and in `Info.plist`/`AndroidManifest.xml`.
- **API Connection**: Check if the API is running and accessible from the device/emulator (use `10.0.2.2` for Android emulator to access localhost).
