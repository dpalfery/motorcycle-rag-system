# Quickstart — 001-system-spec

This quickstart describes how to run the system locally for development.

## Prereqs
- .NET SDK 10
- Node.js (LTS)
- Azure CLI (`az`)
- (Optional) Azure credentials for real cloud integrations
- Visual Studio 2022 with .NET MAUI workload (for MAUI development)

## Infrastructure Setup (Auth & Secrets)

Before running the applications, you need to set up the Azure resources (Key Vault, App Registrations) and local secrets.

### Step 1: Create Azure Resources

Run the setup script to create Azure resources:

```powershell
./7-Deployment/scripts/setup-azure-auth.ps1 -Environment dev -SubscriptionId <your-sub-id>
```

This script will:
- Create an Azure Key Vault.
- Create 4 Entra ID App Registrations (API, Web BFF, Admin, Mobile).
- Generate secrets and store them in Key Vault.
- Output the necessary environment variables for local development.

### Step 2: Set Up Local Environment Variables

**SECURITY NOTICE**: Real Azure tenant IDs, client IDs, API keys, and connection strings must NEVER be committed to source control or stored in configuration files. The `appsettings.json` files contain only empty placeholders. All secrets must come from **secure storage only** (User Secrets for development, Azure Key Vault for production).

**Never use `.env` files or commit secrets to version control.**

### Step 3: Load Environment Variables for Local Development

Choose one method appropriate for your development workflow:

**Option A: Using User Secrets (Recommended for Visual Studio)**
```powershell
cd 1-Presentation/MotorcycleRAG.API
dotnet user-secrets init
dotnet user-secrets set "AzureAd:TenantId" "your-actual-tenant-id"
dotnet user-secrets set "AzureAd:ClientId" "your-actual-client-id"
dotnet user-secrets set "AzureAd:Audience" "your-actual-client-id"
```

User Secrets are stored securely outside the repository and override `appsettings.json` values during development. This is the recommended approach.

**Option B: Set Environment Variables Directly (PowerShell)**
```powershell
$env:AZURE_AD_TENANT_ID="your-tenant-id"
$env:AZURE_AD_CLIENT_ID="your-client-id"
$env:AZURE_AD_CLIENT_SECRET="your-client-secret"
# ... set other required variables (see "Environment Variables (local)" section below)

# Then run the API
dotnet run --project 1-Presentation/MotorcycleRAG.API
```

**Option C: Set Environment Variables (Command Prompt)**
```cmd
set AZURE_AD_TENANT_ID=your-tenant-id
set AZURE_AD_CLIENT_ID=your-client-id
set AZURE_AD_CLIENT_SECRET=your-client-secret
REM ... set other required variables (see "Environment Variables (local)" section below)
dotnet run --project 1-Presentation/MotorcycleRAG.API
```

**Option D: VS Code / IDE Launch Configuration**
For VS Code development, add to `.vscode/launch.json`:
```json
{
  "configurations": [
    {
      "name": "Motorcycle RAG API",
      "type": "coreclr",
      "request": "launch",
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  ]
}
```

Then configure environment variables via User Secrets (Option A) or by setting them in your shell before launching. **Never hardcode secrets in launch.json**.

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
- **.NET MAUI Community Toolkit** (`CommunityToolkit.Maui`) for common UI behaviors and helper views
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

### .NET MAUI Community Toolkit Setup

The admin application uses built-in .NET MAUI controls, augmented by **.NET MAUI Community Toolkit** (`CommunityToolkit.Maui`).

#### Installation

Add the CommunityToolkit.Maui NuGet package to the project:

```xml
<!-- 1-Presentation/MotorcycleRAG.Admin/MotorcycleRAG.Admin.csproj -->
<ItemGroup>
  <PackageReference Include="CommunityToolkit.Maui" />
</ItemGroup>
```

#### Initialization

Initialize the .NET MAUI Community Toolkit in `MauiProgram.cs`:

```csharp
// 1-Presentation/MotorcycleRAG.Admin/MauiProgram.cs
using CommunityToolkit.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
          .UseMauiCommunityToolkit() // Initialize .NET MAUI Community Toolkit
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        return builder.Build();
    }
}
```

#### Navigation Shell Configuration

The admin app uses MAUI Shell with Flyout for left menu navigation and a `Shell.TitleView` header:

```xml
<!-- 1-Presentation/MotorcycleRAG.Admin/AppShell.xaml -->
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
  xmlns:toolkit="http://schemas.microsoft.com/dotnet/2022/maui/toolkit"
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

    <!-- Header with Profile/Login -->
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
                  <Image WidthRequest="32"
                    HeightRequest="32"
                    Source="{Binding UserAvatar}"
                    Aspect="AspectFill"/>
                
                <!-- User Name -->
                <Label Text="{Binding UserName}"
                       FontSize="14"
                       VerticalOptions="Center"/>
                
                <!-- Sign Out Button -->
                <Button Text="Sign out"
                  Command="{Binding SignOutCommand}"/>
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
$env:MAUI_AUTH_AUTHORITY="https://login.microsoftonline.com/<tenant-id>"
$env:MAUI_AUTH_CLIENT_ID="<maui-admin-client-id>"
$env:MAUI_AUTH_REDIRECT_URI="msalmauiadmin:/auth"
$env:MAUI_AUTH_SCOPES="api://<api-client-id>/.default"
```

Where:
- `<tenant-id>`: Your Azure Tenant ID
- `<maui-admin-client-id>`: Client ID of the MAUI Admin app registration
- `<api-client-id>`: Client ID of the API resource app registration

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

- The MAUI app uses CommunityToolkit.Maui plus built-in controls for consistent UI patterns
- UI/UX design follows shared MAUI theming tokens and accessibility guidelines
- Navigation shell uses Flyout + Shell.TitleView header pattern
- App configuration is managed through Azure App Configuration
- Audit logging is implemented for all administrative actions

## Mobile Application (.NET MAUI)

The Motorcycle RAG System includes a cross-platform .NET MAUI mobile app for users.

### Features

- **Cross-platform** .NET MAUI application (Android, iOS, Windows, macOS)
- **.NET MAUI Community Toolkit** (`CommunityToolkit.Maui`) for common UI behaviors and helper views
- **Navigation Shell**: Left menu (Flyout) + top header with profile/login
- **Chat Interface**: Query motorcycle information with real-time responses
- **Conversation History**: View and manage past conversations
- **User Profile**: View profile, plan, and usage statistics
- **Citations**: View source citations for each response

### .NET MAUI Community Toolkit Setup

The mobile application uses built-in .NET MAUI controls, augmented by **.NET MAUI Community Toolkit** (`CommunityToolkit.Maui`).

#### Installation

Add the CommunityToolkit.Maui NuGet package to the project:

```xml
<!-- 1-Presentation/MotorcycleRAG.MobileApp/MotorcycleRAG.MobileApp.csproj -->
<ItemGroup>
  <PackageReference Include="CommunityToolkit.Maui" />
</ItemGroup>
```

#### Initialization

Initialize the .NET MAUI Community Toolkit in `MauiProgram.cs`:

```csharp
// 1-Presentation/MotorcycleRAG.MobileApp/MauiProgram.cs
using CommunityToolkit.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
          .UseMauiCommunityToolkit() // Initialize .NET MAUI Community Toolkit
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
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
  xmlns:toolkit="http://schemas.microsoft.com/dotnet/2022/maui/toolkit"
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

    <!-- Header with Profile/Login -->
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
                  <Image WidthRequest="28"
                    HeightRequest="28"
                    Source="{Binding UserAvatar}"
                    Aspect="AspectFill"/>
                
                <!-- Sign Out Button -->
                <Button Text="Sign out"
                  Command="{Binding SignOutCommand}"/>
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
$env:MOBILE_AUTH_AUTHORITY="https://<tenant-name>.b2clogin.com"
$env:MOBILE_AUTH_CLIENT_ID="<mobile-client-id>"
$env:MOBILE_AUTH_REDIRECT_URI="msamobile:/auth"
$env:MOBILE_AUTH_SCOPES="openid profile email api://<api-client-id>/.default"
```

Where:
- `<tenant-name>`: Your B2C tenant name
- `<mobile-client-id>`: Client ID of the Mobile app registration
- `<api-client-id>`: Client ID of the API resource app registration

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

For local development, you **must** set these environment variables for the API. The application will fail fast with clear error messages if required endpoints are not configured.

#### Critical Azure AI Service Endpoints (Required)

These endpoints are **MANDATORY** for the API to start. They must be valid HTTPS URLs.

```bash
# Azure OpenAI Service - Required for chat and query planning
# Format: https://<your-resource-name>.openai.azure.com/
AZURE_OPENAI_ENDPOINT="https://<your-openai-endpoint>.openai.azure.com/"

# Azure AI Search - Required for hybrid vector/keyword search on motorcycle data
# Format: https://<your-resource-name>.search.windows.net/
AZURE_SEARCH_ENDPOINT="https://<your-search-service>.search.windows.net/"

# Azure Document Intelligence - Required for PDF processing and chunking
# Format: https://<your-resource-name>.cognitiveservices.azure.com/
AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT="https://<your-di-service>.cognitiveservices.azure.com/"

# Azure AI Foundry - Required for additional AI services
# Format: https://<your-foundry>.cognitiveservices.azure.com/ or AI Foundry hub URL
AZURE_FOUNDRY_ENDPOINT="https://<your-foundry-endpoint>.cognitiveservices.azure.com/"
```

#### Azure Service API Keys (Required)

These keys authenticate requests to the Azure services above:

```bash
# OpenAI API Key - Required for LLM operations
AZURE_OPENAI_API_KEY="<your-openai-api-key>"

# Search Service API Key - Required for search operations
AZURE_SEARCH_API_KEY="<your-search-api-key>"

# Document Intelligence API Key - Required for PDF processing
AZURE_DOCUMENT_INTELLIGENCE_API_KEY="<your-document-intelligence-api-key>"
```

#### Azure AD Authentication (Required)

```bash
# Azure AD Tenant ID - Required for authentication
AZURE_AD_TENANT_ID="<your-tenant-id>"

# Azure AD Client ID (API Application) - Required for API authentication
AZURE_AD_CLIENT_ID="<your-api-client-id>"
```

#### Optional/Development Configuration

```bash
# CORS
CORS__ALLOWED_ORIGINS="http://localhost:5173,https://localhost:5001"

# Rate Limiting
RATE_LIMITING__ENABLED=true
RATE_LIMITING__PERIOD=1m
RATE_LIMITING__LIMIT=100
```

**Database Configuration:**
- Database connection strings must NEVER be stored in code, configuration files, or launch configurations
- Use User Secrets or environment variables to provide connection string at runtime
- Set via: `dotnet user-secrets set "ConnectionStrings:Default" "your-connection-string"`

#### Endpoint Validation on Startup

The API performs strict validation of Azure endpoints:

1. **All required endpoints must be configured** - Application fails if any endpoint is missing
2. **Endpoints must use HTTPS** - HTTP endpoints are rejected
3. **Endpoints must be valid URIs** - Malformed URLs are rejected

Example error message if AZURE_OPENAI_ENDPOINT is not set:

```
Azure OpenAI endpoint is not configured. Set the AZURE_OPENAI_ENDPOINT environment variable.
For local development, use 'dotnet user-secrets set "AZURE_OPENAI_ENDPOINT" "https://your-openai-endpoint.com/"'.
Endpoint must be a valid HTTPS URL.
```

#### Setting Environment Variables for Development

**Option A: Using User Secrets (Recommended)**
```powershell
cd 1-Presentation/MotorcycleRAG.API
dotnet user-secrets init
dotnet user-secrets set "AZURE_OPENAI_ENDPOINT" "https://your-openai-endpoint.openai.azure.com/"
dotnet user-secrets set "AZURE_OPENAI_API_KEY" "your-api-key"
dotnet user-secrets set "AZURE_SEARCH_ENDPOINT" "https://your-search-service.search.windows.net/"
dotnet user-secrets set "AZURE_SEARCH_API_KEY" "your-search-key"
dotnet user-secrets set "AZURE_DOCUMENT_INTELLIGENCE_ENDPOINT" "https://your-di-service.cognitiveservices.azure.com/"
dotnet user-secrets set "AZURE_DOCUMENT_INTELLIGENCE_API_KEY" "your-di-key"
dotnet user-secrets set "AZURE_FOUNDRY_ENDPOINT" "https://your-foundry-endpoint.cognitiveservices.azure.com/"
dotnet user-secrets set "AZURE_AD_TENANT_ID" "your-tenant-id"
dotnet user-secrets set "AZURE_AD_CLIENT_ID" "your-api-client-id"
```

**Option B: Set Environment Variables Directly**
```powershell
$env:AZURE_OPENAI_ENDPOINT="https://your-openai-endpoint.openai.azure.com/"
$env:AZURE_OPENAI_API_KEY="your-key"
# ... set other required variables
dotnet run --project 1-Presentation/MotorcycleRAG.API
```

#### Application Insights Configuration

**For Development (Local)**:
- Application Insights telemetry is **disabled by default** in `appsettings.Development.json`
- `EnableTelemetry` is set to `false`
- No environment variable needed for local development

**For Production**:
- Application Insights telemetry is **enabled** in `appsettings.json` and `appsettings.Production.json`
- `EnableTelemetry` is set to `true`
- `APPINSIGHTS_CONNECTION_STRING` environment variable **must be set**
- The application will fail fast with a clear error message if telemetry is enabled without a connection string

**Configuration precedence**:
1. Environment variable: `APPINSIGHTS_CONNECTION_STRING`
2. Configuration file: `ConnectionStrings:ApplicationInsights` (from `appsettings.json`)
3. Configuration file: `ApplicationInsights:ConnectionString` (from `appsettings.json`)

**Validation behavior**:
- If `EnableTelemetry=true` and `ConnectionString` is empty/missing: Application startup fails with clear error message
- If `EnableTelemetry=false`: Connection string is not required (telemetry disabled)
- Error message: "Application Insights is enabled (EnableTelemetry=true) but ConnectionString is not configured. Set the APPINSIGHTS_CONNECTION_STRING environment variable..."

### BFF Environment Variables

The BFF (Backend for Frontend) requires proper configuration to forward API requests via reverse proxy. **IMPORTANT**: The `API_BASE_URL` environment variable is mandatory for the BFF to function.

**Required Environment Variables:**

```bash
# Reverse Proxy / API Configuration (REQUIRED)
# This tells the BFF where to forward API requests
# For development: Use the API's local address
# For production: Must be a valid HTTPS URL with the correct domain
API_BASE_URL="http://localhost:5028"  # For development (HTTP allowed for localhost)
API_BASE_URL="https://api.example.com"  # For production (HTTPS required)
```

**Azure AD / OIDC Configuration (from setup script):**

```bash
# Entra ID Tenant Configuration
AZURE_AD_TENANT_ID="<your-tenant-id>"
AZURE_AD_CLIENT_ID="<bff-app-client-id>"
AZURE_AD_CLIENT_SECRET="<bff-app-client-secret>"

# API Token Validation
API_VALID_AUDIENCE="<api-client-id>"
API_VALID_ISSUER="https://login.microsoftonline.com/<tenant-id>/v2.0"
```

**Complete Example for Development:**

```bash
# Required: API reverse proxy target
API_BASE_URL="http://localhost:5028"

# Entra ID (from setup-azure-auth.ps1 output)
AZURE_AD_TENANT_ID="12345678-1234-1234-1234-123456789012"
AZURE_AD_CLIENT_ID="87654321-4321-4321-4321-210987654321"
AZURE_AD_CLIENT_SECRET="client-secret-value"

# API token validation
API_VALID_AUDIENCE="87654321-4321-4321-4321-210987654321"
API_VALID_ISSUER="https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012/v2.0"
```

**Configuration Precedence:**

1. Environment variable (`API_BASE_URL`) - takes priority
2. `appsettings.json` - base/default configuration (empty, requires env var or file override)
3. `appsettings.{Environment}.json`:
   - `appsettings.Development.json` - defaults to `http://localhost:5028` for convenience
   - `appsettings.Production.json` - empty, requires env var for security

**Important Notes:**

- If `API_BASE_URL` is not set and running in Production, the BFF will fail with a clear error message at startup
- In Development, if `API_BASE_URL` is not set, it falls back to `appsettings.Development.json` (`http://localhost:5028`)
- **Production HTTPS requirement**: If the environment is Production and `API_BASE_URL` uses HTTP, the BFF will fail to start
- The BFF uses YARP (reverse proxy) to forward all `/api/*` requests to the configured API_BASE_URL with the user's access token attached

### MAUI Admin App Environment Variables

For the MAUI admin app, set these environment variables:

```bash
# Authentication
MAUI_AUTH_AUTHORITY="https://login.microsoftonline.com/<tenant-id>"
MAUI_AUTH_CLIENT_ID="<redacted>"
MAUI_AUTH_REDIRECT_URI="msalmauiadmin:/auth"
MAUI_AUTH_SCOPES="api://<api-client-id>/.default"

# API Configuration
MAUI_API_BASE_URL="http://localhost:5028"
MAUI_API_RESOURCE="<redacted>"
```

### MAUI Mobile App Environment Variables

For the MAUI mobile app, set these environment variables:

```bash
# Authentication
MOBILE_AUTH_AUTHORITY="https://<tenant-name>.b2clogin.com"
MOBILE_AUTH_CLIENT_ID="<redacted>"
MOBILE_AUTH_REDIRECT_URI="msamobile:/auth"
MOBILE_AUTH_SCOPES="openid profile email api://<api-client-id>/.default"

# API Configuration
MOBILE_API_BASE_URL="http://localhost:5028"
MOBILE_API_RESOURCE="<redacted>"
```

### Running with Environment Variables

**Windows (PowerShell):**
```powershell
# Set variables and run API
$env:AZURE_OPENAI_ENDPOINT="https://<your-openai-endpoint>.openai.azure.com/"
$env:AZURE_OPENAI_API_KEY="<your-key>"
# ... set other variables
dotnet run --project 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj
```

**Windows (Command Prompt):**
```cmd
set AZURE_OPENAI_ENDPOINT=https://<your-openai-endpoint>.openai.azure.com/
set AZURE_OPENAI_API_KEY=<your-key>
rem ... set other variables
dotnet run --project 1-Presentation/MotorcycleRAG.API/MotorcycleRAG.API.csproj
```

**Using environment variables directly:**

Set variables in your shell before running the application.

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

## CommunityToolkit.Maui Reference

The .NET MAUI Community Toolkit provides MVVM-friendly helpers (behaviors, converters, animations, and helper views) that complement built-in MAUI controls.

- Docs: https://learn.microsoft.com/dotnet/communitytoolkit/maui/get-started
- XAML namespace:
  - `xmlns:toolkit="http://schemas.microsoft.com/dotnet/2022/maui/toolkit"`
