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
- **UI Toolkit**: Uses built-in MAUI controls plus CommunityToolkit.Maui helpers.
- **Dependency Injection**: Configured in `MauiProgram.cs`.
- **Persistence**: SQLite for local storage of conversations and messages.
- **Resilience**: Polly policies for API retries and circuit breaking.

## UI Toolkit

This app uses **CommunityToolkit.Maui** plus built-in MAUI controls. The toolkit provides useful building blocks (behaviors, converters, and a small set of views) without forcing a full design system.

### Usage Example

```xml
<ContentPage xmlns:toolkit="http://schemas.microsoft.com/dotnet/2022/maui/toolkit">
    <Grid>
        <Grid.Behaviors>
            <toolkit:TouchBehavior Command="{Binding SendCommand}" />
        </Grid.Behaviors>
        <Label Text="Tap to Send" />
    </Grid>
</ContentPage>
```

For more details, see `specs/001-mobile-app/research.md`.

## Testing

Run unit tests from the `5-Test/MotorcycleRAG.MobileApp.Tests` project.

```bash
dotnet test 5-Test/MotorcycleRAG.MobileApp.Tests
```

## Troubleshooting

- **Authentication Issues**: Ensure the redirect URIs are correctly configured in Azure AD and in `Info.plist`/`AndroidManifest.xml`.
- **API Connection**: Check if the API is running and accessible from the device/emulator (use `10.0.2.2` for Android emulator to access localhost).
