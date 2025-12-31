# Quickstart — 001-system-spec

This quickstart describes how to run the system locally for development.

## Prereqs
- .NET SDK 10
- Node.js (LTS)
- (Optional) Azure credentials for real cloud integrations
- Visual Studio 2022 with .NET MAUI workload (for MAUI development)

## Backend API

1) From repo root:
- Build: `dotnet build MotorcycleRAG.sln`
- Run API: `dotnet run --project 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj`

2) Health:
- `GET /health`

## Web Application (React)

1) From `1-Presentation/MotorcycleRag.WebUI/`:
- Install: `npm install`
- Dev server: `npm run dev`

## Web UI BFF (recommended when using OIDC)

The repo includes a YARP-based BFF that performs OIDC sign-in and forwards user access token to the API.

- Run BFF: `dotnet run --project 1-Presentation/MotorcycleRag.WebUI.BFF/MotorcycleRag.WebUI.BFF.csproj`

## Admin Ingestion App (.NET MAUI)

The Motorcycle RAG System includes a .NET MAUI admin application for data ingestion and system management.

### Features

- **Windows-first** .NET MAUI application targeting .NET 10
- **Material.Components.Maui** UI components for consistent Material Design 3 styling
- **Navigation Shell**: Left menu (Flyout) + top header with profile/login
- **Local processing** capabilities including:
  - PDF chunking and text extraction
  - CSV parsing and structured data processing
  - ONNX Runtime embedding generation using local models
- **Data ingestion workflow**:
  - File picker with validation (type/size constraints)
  - Local chunking + vectorization
  - Upload processed artifacts to ingestion API
- **MCP Server/Tool Management**:
  - Configure MCP servers and tools
  - Enable/disable tools without redeploy
  - Ship configuration updates to API
- **Job Monitoring**:
  - View ingestion job status
  - Cancel running jobs
  - View processing metrics

### Material.Components.Maui Setup

The admin application uses **Material.Components.Maui** for all UI components.

#### Installation

Add the Material.Components.Maui NuGet package to the project:

```xml
<!-- 1-Presentation/MotorcycleRAG.Admin/MotorcycleRAG.Admin.csproj -->
<ItemGroup>
  <PackageReference Include="Material.Components.Maui" Version="1.0.0" />
</ItemGroup>
```

#### Initialization

Initialize Material Components in `MauiProgram.cs`:

```csharp
// 1-Presentation/MotorcycleRAG.Admin/MauiProgram.cs
using Material.Components.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMaterialComponents() // Initialize Material Components
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("MaterialIcons-Regular.ttf", "MaterialIcons");
            });

        return builder.Build();
    }
}
```

#### Navigation Shell Configuration

The admin app uses MAUI Shell with Flyout for left menu navigation and TopAppBar for header:

```xml
<!-- 1-Presentation/MotorcycleRAG.Admin/AppShell.xaml -->
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
       xmlns:mc="clr-namespace:Material.Components.Maui;assembly=Material.Components.Maui"
       xmlns:pages="clr-namespace:MotorcycleRAG.Admin.Pages"
       FlyoutBehavior="Locked"
       FlyoutWidth="300">

    <Shell.FlyoutHeader>
        <!-- Flyout Header with App Logo/Title -->
        <Grid HeightRequest="120" BackgroundColor="{StaticResource Primary}">
            <Label Text="MotorcycleRAG Admin"
                   FontSize="20"
                   FontAttributes="Bold"
                   TextColor="{StaticResource OnPrimary}"
                   VerticalOptions="Center"
                   HorizontalOptions="Center"/>
        </Grid>
    </Shell.FlyoutHeader>

    <!-- Flyout Items (Left Menu) -->
    <FlyoutItem Title="Dashboard"
               Icon="dashboard.png">
        <ShellContent ContentTemplate="{DataTemplate pages:DashboardPage}"/>
    </FlyoutItem>

    <FlyoutItem Title="Upload"
               Icon="upload.png">
        <ShellContent ContentTemplate="{DataTemplate pages:UploadPage}"/>
    </FlyoutItem>

    <FlyoutItem Title="Jobs"
               Icon="jobs.png">
        <ShellContent ContentTemplate="{DataTemplate pages:JobsPage}"/>
    </FlyoutItem>

    <FlyoutItem Title="Web Sources"
               Icon="web.png">
        <ShellContent ContentTemplate="{DataTemplate pages:WebSourcesPage}"/>
    </FlyoutItem>

    <FlyoutItem Title="Tools"
               Icon="tools.png">
        <ShellContent ContentTemplate="{DataTemplate pages:ToolsPage}"/>
    </FlyoutItem>

    <!-- Top Header with Profile/Login -->
    <Shell.TitleView>
        <Grid ColumnDefinitions="*,Auto">
            <!-- App Title (Left) -->
            <Label Text="{Binding Title}"
                   Grid.Column="0"
                   FontSize="18"
                   FontAttributes="Bold"
                   VerticalOptions="Center"/>

            <!-- Profile/Login Area (Right) -->
            <HorizontalStackLayout Grid.Column="1"
                                Spacing="10"
                                VerticalOptions="Center">
                <!-- User Avatar -->
                <mc:Avatar WidthRequest="32"
                           HeightRequest="32"
                           Source="{Binding UserAvatar}"
                           Initials="{Binding UserInitials}"/>
                
                <!-- User Name -->
                <Label Text="{Binding UserName}"
                       FontSize="14"
                       VerticalOptions="Center"/>
                
                <!-- Sign Out Button -->
                <mc:IconButton Icon="logout.png"
                               Command="{Binding SignOutCommand}"
                               ToolTip="Sign Out"/>
            </HorizontalStackLayout>
        </Grid>
    </Shell.TitleView>
</Shell>
```

#### Responsive Design

The navigation shell adapts to desktop vs mobile:

```csharp
// 1-Presentation/MotorcycleRAG.Admin/AppShell.xaml.cs
protected override void OnAppearing()
{
    base.OnAppearing();
    
    // Adjust flyout behavior based on device type
    if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
    {
        this.FlyoutBehavior = FlyoutBehavior.Locked;
        this.FlyoutWidth = 300;
    }
    else if (DeviceInfo.Idiom == DeviceIdiom.Phone)
    {
        this.FlyoutBehavior = FlyoutBehavior.Flyout;
        this.FlyoutWidth = 250;
    }
}
```

### Running MAUI Admin App

**Prerequisites:**
- .NET 10 SDK with MAUI workload installed
- Windows 10/11 development environment (primary target)
- Visual Studio 2022 with .NET MAUI workload

**Build and Run:**
```bash
# Navigate to MAUI project directory
cd 1-Presentation/MotorcycleRAG.Admin

# Build MAUI application
dotnet build MotorcycleRAG.Admin.csproj -t:Run -f net10.0-windows10.0.19041.0

# Or use Visual Studio to build and debug
```

**Platform Targets:**
- **Windows**: `net10.0-windows10.0.19041.0` (primary)
- **macOS**: `net10.0-maccatalyst`
- **Android**: `net10.0-android` (Android 21+)
- **iOS**: `net10.0-ios` (iOS 15+)

### Authentication

The MAUI admin app uses Entra ID authentication with device code flow:

```bash
# Set required environment variables for authentication
$env:MAUI_AUTH_AUTHORITY="https://login.microsoftonline.com/your-tenant-id"
$env:MAUI_AUTH_CLIENT_ID="your-maui-client-id"
$env:MAUI_AUTH_REDIRECT_URI="msalyour-maui-client-id://auth"
$env:MAUI_AUTH_SCOPES="api://your-api-client-id/.default"
```

### Configuration

The app requires connection to API endpoints:

```bash
# API Configuration
$env:MAUI_API_BASE_URL="http://localhost:5028"
$env:MAUI_API_RESOURCE="your-api-resource-id"
```

### Local Processing

The MAUI app includes embedded ONNX models for local processing:

- **Embedding Model**: `Resources/Raw/embedding-model.onnx`
- **Chunking Algorithms**: PDF and CSV specific processors
- **Validation**: File type, size, and content validation

### Role-Based Access

The admin app enforces role-based access control:

- **Admin Role**: Full access to all features
- **Data Manager Role**: Access to ingestion features only
- **Viewer Role**: Read-only access to job monitoring

### Development Notes

- The MAUI app uses Material.Components.Maui for consistent UI
- UI/UX design follows Material Design 3 principles
- Navigation shell uses Flyout + TopAppBar pattern
- App configuration is managed through Azure App Configuration
- Audit logging is implemented for all administrative actions

## Mobile Application (.NET MAUI)

The Motorcycle RAG System includes a cross-platform .NET MAUI mobile app for users.

### Features

- **Cross-platform** .NET MAUI application (Android, iOS, Windows, macOS)
- **Material.Components.Maui** UI components for consistent Material Design 3 styling
- **Navigation Shell**: Left menu (Flyout) + top header with profile/login
- **Chat Interface**: Query motorcycle information with real-time responses
- **Conversation History**: View and manage past conversations
- **User Profile**: View profile, plan, and usage statistics
- **Citations**: View source citations for each response

### Material.Components.Maui Setup

The mobile application uses **Material.Components.Maui** for all UI components.

#### Installation

Add the Material.Components.Maui NuGet package to the project:

```xml
<!-- 1-Presentation/MotorcycleRAG.MobileApp/MotorcycleRAG.MobileApp.csproj -->
<ItemGroup>
  <PackageReference Include="Material.Components.Maui" Version="1.0.0" />
</ItemGroup>
```

#### Initialization

Initialize Material Components in `MauiProgram.cs`:

```csharp
// 1-Presentation/MotorcycleRAG.MobileApp/MauiProgram.cs
using Material.Components.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMaterialComponents() // Initialize Material Components
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("MaterialIcons-Regular.ttf", "MaterialIcons");
            });

        return builder.Build();
    }
}
```

#### Navigation Shell Configuration

The mobile app uses MAUI Shell with Flyout for left menu navigation:

```xml
<!-- 1-Presentation/MotorcycleRAG.MobileApp/AppShell.xaml -->
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
       xmlns:mc="clr-namespace:Material.Components.Maui;assembly=Material.Components.Maui"
       xmlns:views="clr-namespace:MotorcycleRAG.MobileApp.Views"
       FlyoutBehavior="Flyout"
       FlyoutWidth="250">

    <Shell.FlyoutHeader>
        <!-- Flyout Header with App Logo/Title -->
        <Grid HeightRequest="100" BackgroundColor="{StaticResource Primary}">
            <Label Text="MotorcycleRAG"
                   FontSize="18"
                   FontAttributes="Bold"
                   TextColor="{StaticResource OnPrimary}"
                   VerticalOptions="Center"
                   HorizontalOptions="Center"/>
        </Grid>
    </Shell.FlyoutHeader>

    <!-- Flyout Items (Left Menu) -->
    <FlyoutItem Title="Chat"
               Icon="chat.png">
        <ShellContent ContentTemplate="{DataTemplate views:ChatPage}"/>
    </FlyoutItem>

    <FlyoutItem Title="Conversations"
               Icon="history.png">
        <ShellContent ContentTemplate="{DataTemplate views:ConversationListPage}"/>
    </FlyoutItem>

    <FlyoutItem Title="Profile"
               Icon="profile.png">
        <ShellContent ContentTemplate="{DataTemplate views:UserProfilePage}"/>
    </FlyoutItem>

    <!-- Top Header with Profile/Login -->
    <Shell.TitleView>
        <Grid ColumnDefinitions="*,Auto">
            <!-- App Title (Left) -->
            <Label Text="{Binding Title}"
                   Grid.Column="0"
                   FontSize="16"
                   FontAttributes="Bold"
                   VerticalOptions="Center"/>

            <!-- Profile/Login Area (Right) -->
            <HorizontalStackLayout Grid.Column="1"
                                Spacing="8"
                                VerticalOptions="Center">
                <!-- User Avatar -->
                <mc:Avatar WidthRequest="28"
                           HeightRequest="28"
                           Source="{Binding UserAvatar}"
                           Initials="{Binding UserInitials}"/>
                
                <!-- Sign Out Button -->
                <mc:IconButton Icon="logout.png"
                               Command="{Binding SignOutCommand}"
                               ToolTip="Sign Out"/>
            </HorizontalStackLayout>
        </Grid>
    </Shell.TitleView>
</Shell>
```

### Running MAUI Mobile App

**Prerequisites:**
- .NET 10 SDK with MAUI workload installed
- Platform-specific SDK:
  - Android: Android SDK with API 21+
  - iOS: Xcode 14+ with iOS 15+ SDK
  - Windows: Windows 10/11 SDK
  - macOS: macOS 12+ SDK

**Build and Run:**
```bash
# Navigate to MAUI project directory
cd 1-Presentation/MotorcycleRAG.MobileApp

# Build for Windows
dotnet build MotorcycleRAG.MobileApp.csproj -t:Run -f net10.0-windows10.0.19041.0

# Build for Android
dotnet build MotorcycleRAG.MobileApp.csproj -t:Run -f net10.0-android

# Build for iOS
dotnet build MotorcycleRAG.MobileApp.csproj -t:Run -f net10.0-ios

# Or use Visual Studio to build and debug
```

### Authentication

The mobile app uses Entra External ID / B2C authentication with social sign-in:

```bash
# Set required environment variables for authentication
$env:MOBILE_AUTH_AUTHORITY="https://your-b2c-tenant.b2clogin.com"
$env:MOBILE_AUTH_CLIENT_ID="your-mobile-client-id"
$env:MOBILE_AUTH_REDIRECT_URI="msalyour-mobile-client-id://auth"
$env:MOBILE_AUTH_SCOPES="openid profile email api://your-api-client-id/.default"
```

### Configuration

The app requires connection to API endpoints:

```bash
# API Configuration
$env:MOBILE_API_BASE_URL="http://localhost:5028"
$env:MOBILE_API_RESOURCE="your-api-resource-id"
```

### Local Storage

The mobile app uses SQLite for local data persistence:

- **Conversation History**: Store chat conversations locally
- **User Preferences**: Cache user settings
- **Offline Support**: View previously loaded conversations offline

## Environment Variables (local)

### API Environment Variables

For local development, set these environment variables for the API:

```bash
# Azure AI Services
AZURE_OPENAI_ENDPOINT="https://your-dev-openai-endpoint.openai.azure.com/"
AZURE_OPENAI_API_KEY="your-openai-api-key"
AZURE_OPENAI_DEPLOYMENT_NAME="gpt-4o"
AZURE_OPENAI_EMBEDDING_DEPLOYMENT_NAME="text-embedding-3-large"

AZURE_SEARCH_ENDPOINT="https://your-dev-search-service.search.windows.net/"
AZURE_SEARCH_API_KEY="your-search-api-key"
AZURE_SEARCH_INDEX_NAME="motorcycle-rag-index"

AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT="https://your-dev-document-intelligence.cognitiveservices.azure.com/"
AZURE_DOCUMENT_INTELLIGENCE_API_KEY="your-document-intelligence-api-key"

# Application Insights
APPLICATION_INSIGHTS_CONNECTION_STRING="your-application-insights-connection-string"

# Database
CONNECTION_STRINGS__DEFAULT="Server=localhost;Database=MotorcycleRAG;User Id=sa;Password=your-password;TrustServerCertificate=True"

# Authentication
AUTHENTICATION__ENTRA_ID__TENANT_ID="your-tenant-id"
AUTHENTICATION__ENTRA_ID__CLIENT_ID="your-client-id"
AUTHENTICATION__ENTRA_ID__CLIENT_SECRET="your-client-secret"

# JWT Settings
JWT__ISSUER="https://your-issuer.com"
JWT__AUDIENCE="your-audience"
JWT__SIGNING_KEY="your-signing-key-with-at-least-32-characters"

# CORS
CORS__ALLOWED_ORIGINS="http://localhost:5173,https://localhost:5001"

# Rate Limiting
RATE_LIMITING__ENABLED=true
RATE_LIMITING__PERIOD=1m
RATE_LIMITING__LIMIT=100
```

### BFF Environment Variables

For the BFF (Backend for Frontend), set these environment variables:

```bash
# OIDC Configuration
OIDC__AUTHORITY="https://your-identity-provider.com"
OIDC__CLIENT_ID="your-bff-client-id"
OIDC__CLIENT_SECRET="your-bff-client-secret"
OIDC__RESPONSE_TYPE="code"
OIDC__SCOPE="openid profile email api-access"

# API Configuration
API__BASE_URL="http://localhost:5028"
API__RESOURCE="your-api-resource-id"

# Session Configuration
SESSION__SECRET="your-session-secret-with-at-least-32-characters"
SESSION__TIMEOUT_MINUTES=60

# CORS for BFF
CORS__ALLOWED_ORIGINS="http://localhost:5173"
```

### MAUI Admin App Environment Variables

For the MAUI admin app, set these environment variables:

```bash
# Authentication
MAUI_AUTH_AUTHORITY="https://login.microsoftonline.com/your-tenant-id"
MAUI_AUTH_CLIENT_ID="your-maui-client-id"
MAUI_AUTH_REDIRECT_URI="msalyour-maui-client-id://auth"
MAUI_AUTH_SCOPES="api://your-api-client-id/.default"

# API Configuration
MAUI_API_BASE_URL="http://localhost:5028"
MAUI_API_RESOURCE="your-api-resource-id"
```

### MAUI Mobile App Environment Variables

For the MAUI mobile app, set these environment variables:

```bash
# Authentication
MOBILE_AUTH_AUTHORITY="https://your-b2c-tenant.b2clogin.com"
MOBILE_AUTH_CLIENT_ID="your-mobile-client-id"
MOBILE_AUTH_REDIRECT_URI="msalyour-mobile-client-id://auth"
MOBILE_AUTH_SCOPES="openid profile email api://your-api-client-id/.default"

# API Configuration
MOBILE_API_BASE_URL="http://localhost:5028"
MOBILE_API_RESOURCE="your-api-resource-id"
```

### Running with Environment Variables

**Windows (PowerShell):**
```powershell
# Set variables and run API
$env:AZURE_OPENAI_ENDPOINT="https://your-dev-openai-endpoint.openai.azure.com/"
$env:AZURE_OPENAI_API_KEY="your-openai-api-key"
# ... set other variables
dotnet run --project 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj
```

**Windows (Command Prompt):**
```cmd
set AZURE_OPENAI_ENDPOINT=https://your-dev-openai-endpoint.openai.azure.com/
set AZURE_OPENAI_API_KEY=your-openai-api-key
rem ... set other variables
dotnet run --project 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj
```

**Using .env file (recommended for development):**

1. Create a `.env` file in project root
2. Add your environment variables
3. Use a tool like `dotnet-user-secrets` or `env-cmd` to load them

### Development Secrets Management

- Use `dotnet user-secrets` for development secrets:
  ```bash
    cd 1-Presentation/MotorcycleRAG.API
    dotnet user-secrets init
    dotnet user-secrets set "AZURE_OPENAI_ENDPOINT" "https://your-dev-openai-endpoint.openai.azure.com/"
    ```

- For production, use Azure Key Vault or other secure secret management solutions
- Never commit secrets to version control

## Testing

- Run unit tests: `dotnet test MotorcycleRAG.sln`

## Material.Components.Maui Component Reference

### Available Components

| Component | Description | Usage Example |
|-----------|-------------|----------------|
| **Flyout** | Left navigation drawer | AppShell.xaml |
| **TopAppBar** | Application header | AppShell.xaml |
| **Avatar** | User profile image | Profile/Login area |
| **IconButton** | Icon-only button | Sign out, actions |
| **Button** | Material button (Filled, Outlined, Text) | Primary actions |
| **TextField** | Text input with floating label | Query input, forms |
| **TextArea** | Multi-line text input | Long-form content |
| **Card** | Material card with elevation | Job status, sources |
| **ProgressBar** | Linear progress indicator | Upload/processing |
| **CircularProgress** | Circular progress indicator | Loading states |
| **Snackbar** | Brief message | Notifications |
| **Switch** | Toggle switch | Enable/disable tools |
| **CheckBox** | Checkbox | Form inputs |
| **ComboBox** | Dropdown selection | Plan selection |

### Theming

Define Material Design 3 color scheme in `App.xaml`:

```xml
<Application.Resources>
    <ResourceDictionary>
        <!-- Primary Colors -->
        <Color x:Key="Primary">#512BD4</Color>
        <Color x:Key="OnPrimary">#FFFFFF</Color>
        <Color x:Key="PrimaryContainer">#EADDFF</Color>
        <Color x:Key="OnPrimaryContainer">#21005D</Color>
        
        <!-- Secondary Colors -->
        <Color x:Key="Secondary">#625B71</Color>
        <Color x:Key="OnSecondary">#FFFFFF</Color>
        <Color x:Key="SecondaryContainer">#E8DEF8</Color>
        <Color x:Key="OnSecondaryContainer">#1D192B</Color>
        
        <!-- Tertiary Colors -->
        <Color x:Key="Tertiary">#7D5260</Color>
        <Color x:Key="OnTertiary">#FFFFFF</Color>
        <Color x:Key="TertiaryContainer">#FFD8E4</Color>
        <Color x:Key="OnTertiaryContainer">#31111D</Color>
        
        <!-- Error Colors -->
        <Color x:Key="Error">#B3261E</Color>
        <Color x:Key="OnError">#FFFFFF</Color>
        
        <!-- Surface Colors -->
        <Color x:Key="Background">#FFFBFE</Color>
        <Color x:Key="OnBackground">#1C1B1F</Color>
        <Color x:Key="Surface">#FFFBFE</Color>
        <Color x:Key="OnSurface">#1C1B1F</Color>
    </ResourceDictionary>
</Application.Resources>
```
