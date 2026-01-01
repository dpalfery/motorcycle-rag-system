# Quickstart Guide: .NET MAUI Mobile App

**Feature**: 001-mobile-app
**Date**: 2025-12-26
**Purpose**: Developer setup and quickstart guide for building and running the mobile app

## Prerequisites

### Development Environment

**Required**:
- Windows 10/11 (for development)
- Visual Studio 2022 v17.12+ with .NET MAUI workload installed
- .NET 10.0 SDK
- Git

**Platform-Specific Requirements**:
- **iOS Development**:
  - macOS machine (for iOS build agent) OR Visual Studio for Mac OR cloud build service
  - Xcode 15+ (on Mac)
  - Apple Developer account (for device testing and App Store distribution)

- **Android Development**:
  - Android SDK (installed with Visual Studio MAUI workload)
  - Android Emulator OR physical Android device with Developer Mode enabled

- **Windows Development**:
  - Windows 10 SDK 10.0.19041.0+
  - Included with Visual Studio 2022

### Backend API Access

- MotorcycleRAG.API running and accessible
- API base URL (e.g., `https://localhost:5001` for local dev, `https://api.motorcyclerag.com` for production)
- Microsoft Entra External ID / B2C tenant configured with:
  - Client ID for mobile app
  - Sign-up/Sign-in user flow
  - API scope for MotorcycleRAG.API

---

## Project Setup

### 1. Clone Repository

```powershell
git clone https://github.com/yourusername/motoRagApp.git
cd motoRagApp
git checkout 001-mobile-app
```

### 2. Install .NET MAUI Workload (if not already installed)

```powershell
dotnet workload install maui
```

### 3. Create .NET MAUI Project

```powershell
# Navigate to presentation layer
cd 1-Presentation

# Create MAUI app project
dotnet new maui -n MotorcycleRAG.MobileApp -o MotorcycleRAG.MobileApp

# Create test project
cd ../5-Test
dotnet new xunit -n MotorcycleRAG.MobileApp.Tests -o MotorcycleRAG.MobileApp.Tests
```

### 4. Add NuGet Packages

Edit `1-Presentation/MotorcycleRAG.MobileApp/MotorcycleRAG.MobileApp.csproj`:

```xml
<ItemGroup>
    <!-- Core MAUI -->
    <PackageReference Include="Microsoft.Maui.Controls" Version="10.0.0" />
    <PackageReference Include="Microsoft.Maui.Essentials" Version="10.0.0" />

    <!-- Authentication -->
    <PackageReference Include="Microsoft.Identity.Client" Version="4.66.0" />

    <!-- MVVM -->
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.0" />

    <!-- UI Components - Material Design 3 -->
    <PackageReference Include="Material.Components.Maui" Version="0.2.2-preview" />

    <!-- Local Database -->
    <PackageReference Include="sqlite-net-pcl" Version="1.9.172" />
    <PackageReference Include="SQLitePCLRaw.bundle_green" Version="2.1.10" />

    <!-- HTTP & Resilience -->
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http.Polly" Version="10.0.0" />

    <!-- JSON Serialization -->
    <PackageReference Include="System.Text.Json" Version="10.0.0" />
</ItemGroup>
```

Restore packages:
```powershell
dotnet restore
```

### 5. Configure App Settings

Create `appsettings.json` in project root:

```json
{
  "Api": {
    "BaseUrl": "https://localhost:5001",
    "Timeout": "00:00:30"
  },
  "Authentication": {
    "ClientId": "your-client-id-from-entra",
    "Authority": "https://yourtenant.b2clogin.com/tfp/yourtenant.onmicrosoft.com/B2C_1_SignUpSignIn",
    "RedirectUri": "msauth://com.motorcyclerag.mobile",
    "Scopes": ["https://yourtenant.onmicrosoft.com/api/user_impersonation"]
  }
}
```

**Important**: Add `appsettings.json` to `.gitignore` to avoid committing secrets.

Create `appsettings.Development.json` for local development:
```json
{
  "Api": {
    "BaseUrl": "https://localhost:5001"
  }
}
```

### 6. Configure Platform-Specific Settings

**iOS (`Platforms/iOS/Info.plist`)**:
```xml
<key>CFBundleURLTypes</key>
<array>
    <dict>
        <key>CFBundleURLSchemes</key>
        <array>
            <string>msauth</string>
        </array>
    </dict>
</array>

<key>LSApplicationQueriesSchemes</key>
<array>
    <string>msauthv2</string>
    <string>msauthv3</string>
</array>

<!-- For secure storage keychain -->
<key>KeychainAccessGroups</key>
<array>
    <string>$(AppIdentifierPrefix)com.motorcyclerag.mobile</string>
</array>
```

**Android (`Platforms/Android/AndroidManifest.xml`)**:
```xml
<application>
    <activity android:name="microsoft.identity.client.BrowserTabActivity">
        <intent-filter>
            <action android:name="android.intent.action.VIEW" />
            <category android:name="android.intent.category.DEFAULT" />
            <category android:name="android.intent.category.BROWSABLE" />
            <data
                android:scheme="msauth"
                android:host="com.motorcyclerag.mobile"
                android:path="/your-signature-hash" />
        </intent-filter>
    </activity>
</application>

<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
```

---

## Running the App

### Build and Run

**Visual Studio 2022**:
1. Open `motoRagApp.sln`
2. Set `MotorcycleRAG.MobileApp` as startup project
3. Select target platform (Android Emulator, Windows Machine, etc.)
4. Press F5 to build and run

**Command Line**:

```powershell
# Android
dotnet build -t:Run -f net10.0-android

# Windows
dotnet build -t:Run -f net10.0-windows10.0.19041.0

# iOS (requires Mac build agent)
dotnet build -t:Run -f net10.0-ios
```

### First Run Experience

1. **Authentication**: App launches → Authentication page
   - Tap "Sign In"
   - Browser opens for Entra B2C sign-in
   - Sign in with Google/GitHub/Microsoft/Facebook
   - Redirect back to app

2. **Plan Selection** (if required): Choose Free/Plus/Pro plan

3. **Home Screen**: Conversation list (empty on first run)
   - Tap "+" to start new conversation

4. **Chat**: Ask a question
   - Type: "What's the horsepower of a Yamaha R1?"
   - Tap Send
   - View answer with tappable citations

5. **User Memory**: After a few conversations
   - App extracts user information (e.g., "I own an R1M")
   - Future answers personalized based on memory

---

## Development Workflow

### Project Structure

```
1-Presentation/MotorcycleRAG.MobileApp/
├── Platforms/              # Platform-specific code
├── Views/                  # XAML pages
├── ViewModels/             # ViewModels (MVVM)
├── Services/               # Business logic and API client
├── Models/                 # Domain models
├── Persistence/            # SQLite repositories
├── Converters/             # XAML value converters
├── Resources/              # Images, fonts, styles
├── MauiProgram.cs          # DI container setup
└── App.xaml                # App entry point

5-Test/MotorcycleRAG.MobileApp.Tests/
├── ViewModels/             # ViewModel unit tests
├── Services/               # Service unit tests
└── Integration/            # API integration tests
```

### Dependency Injection Setup

In `MauiProgram.cs`:

```csharp
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMaterialComponents()  // Initialize Material Design 3 components
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Configuration
        builder.Configuration.AddJsonFile("appsettings.json");
#if DEBUG
        builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true);
#endif

        // Services
        builder.Services.AddSingleton<IAuthenticationService, AuthenticationService>();
        builder.Services.AddSingleton<IStorageService, StorageService>();
        builder.Services.AddSingleton<IConversationRepository, ConversationRepository>();
        builder.Services.AddSingleton<IUserMemoryRepository, UserMemoryRepository>();

        // HTTP Client with Polly
        builder.Services.AddHttpClient<IApiClient, MotorcycleRagApiClient>(client =>
        {
            var apiConfig = builder.Configuration.GetSection("Api");
            client.BaseAddress = new Uri(apiConfig["BaseUrl"]);
            client.Timeout = TimeSpan.Parse(apiConfig["Timeout"]);
        })
        .AddTransientHttpErrorPolicy(policy =>
            policy.WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))));

        // ViewModels
        builder.Services.AddTransient<AuthenticationViewModel>();
        builder.Services.AddTransient<ConversationListViewModel>();
        builder.Services.AddTransient<ChatViewModel>();
        builder.Services.AddTransient<ProfileViewModel>();

        // Views
        builder.Services.AddTransient<AuthenticationPage>();
        builder.Services.AddTransient<ConversationListPage>();
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddTransient<ProfilePage>();

        return builder.Build();
    }
}
```

### Building a Feature (Example: Chat Page)

**1. Create Model** (`Models/ChatMessage.cs`):
```csharp
public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public MessageSender Sender { get; set; }
    public string Content { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public List<SourceCitation> Citations { get; set; }
}
```

**2. Create ViewModel** (`ViewModels/ChatViewModel.cs`):
```csharp
[ObservableObject]
public partial class ChatViewModel
{
    private readonly IApiClient _apiClient;
    private readonly IConversationService _conversationService;

    [ObservableProperty]
    private string questionText;

    [ObservableProperty]
    private ObservableCollection<ChatMessage> messages = new();

    [ObservableProperty]
    private bool isSending;

    public ChatViewModel(IApiClient apiClient, IConversationService conversationService)
    {
        _apiClient = apiClient;
        _conversationService = conversationService;
    }

    [RelayCommand]
    private async Task SendQuestionAsync()
    {
        if (string.IsNullOrWhiteSpace(QuestionText)) return;

        IsSending = true;
        try
        {
            // Add user message
            var userMessage = new ChatMessage
            {
                Sender = MessageSender.User,
                Content = QuestionText
            };
            Messages.Add(userMessage);

            // Send to API
            var request = new QueryRequest { Query = QuestionText, ... };
            var response = await _apiClient.SendQueryAsync(request);

            // Add system response
            var systemMessage = new ChatMessage
            {
                Sender = MessageSender.System,
                Content = response.Response,
                Citations = MapCitations(response.Sources)
            };
            Messages.Add(systemMessage);

            // Save conversation
            await _conversationService.SaveMessageAsync(userMessage);
            await _conversationService.SaveMessageAsync(systemMessage);

            QuestionText = string.Empty;
        }
        catch (Exception ex)
        {
            // Handle error (show to user)
        }
        finally
        {
            IsSending = false;
        }
    }
}
```

**3. Create View** (`Views/ChatPage.xaml`):
```xaml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             x:Class="MotorcycleRAG.MobileApp.Views.ChatPage"
             x:DataType="vm:ChatViewModel"
             Title="Chat">
    <Grid RowDefinitions="*,Auto">
        <!-- Messages list -->
        <CollectionView Grid.Row="0" ItemsSource="{Binding Messages}">
            <CollectionView.ItemTemplate>
                <DataTemplate x:DataType="models:ChatMessage">
                    <Grid Padding="12" ColumnDefinitions="*">
                        <Label Text="{Binding Content}"
                               BackgroundColor="{Binding Sender, Converter={StaticResource SenderToColorConverter}}"
                               Padding="12" />
                    </Grid>
                </DataTemplate>
            </CollectionView.ItemTemplate>
        </CollectionView>

        <!-- Input area -->
        <Grid Grid.Row="1" ColumnDefinitions="*,Auto" Padding="12">
            <Entry Grid.Column="0"
                   Text="{Binding QuestionText}"
                   Placeholder="Ask a question..."
                   IsEnabled="{Binding IsSending, Converter={StaticResource InvertedBoolConverter}}" />
            <Button Grid.Column="1"
                    Text="Send"
                    Command="{Binding SendQuestionCommand}"
                    IsEnabled="{Binding IsSending, Converter={StaticResource InvertedBoolConverter}}" />
        </Grid>
    </Grid>
</ContentPage>
```

---

## Testing

### Run Unit Tests

```powershell
cd 5-Test/MotorcycleRAG.MobileApp.Tests
dotnet test
```

### Run on Physical Device

**Android**:
1. Enable Developer Mode on device
2. Enable USB Debugging
3. Connect via USB
4. Select device in Visual Studio
5. Run

**iOS**:
1. Register device UDID in Apple Developer Portal
2. Create provisioning profile
3. Connect via USB (to Mac build agent)
4. Select device in Visual Studio
5. Run

**Windows**:
- Run directly on development machine

---

## Troubleshooting

### Common Issues

**Issue**: MSAL authentication fails with "redirect URI not registered"
**Solution**: Ensure redirect URI in Entra B2C matches `appsettings.json` exactly

**Issue**: SQLite database errors on iOS
**Solution**: Ensure `SQLitePCLRaw.bundle_green` package is installed

**Issue**: API calls return 401 Unauthorized
**Solution**: Check token expiration, trigger silent token refresh

**Issue**: Android emulator won't connect to localhost API
**Solution**: Use `10.0.2.2` instead of `localhost` for Android emulator

**Issue**: iOS build fails with provisioning profile errors
**Solution**: Ensure Apple Developer account is configured, provisioning profile is valid

### Debug Logging

Enable detailed logging in `MauiProgram.cs`:

```csharp
#if DEBUG
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
#endif
```

View logs in Visual Studio Output window or use platform-specific tools:
- **Android**: `adb logcat`
- **iOS**: Xcode Console
- **Windows**: Debug Output

---

## Next Steps

1. **Authentication**: Implement `AuthenticationService` with MSAL
2. **Chat UI**: Build chat page with message list and input
3. **Conversation Management**: Implement list, search, delete
4. **User Memory**: Implement extraction and storage
5. **Offline Support**: Implement conversation caching
6. **Testing**: Write unit and integration tests
7. **Platform Customization**: Refine UI for each platform
8. **Performance**: Optimize scrolling and database queries

---

## Resources

**Documentation**:
- [.NET MAUI Official Docs](https://learn.microsoft.com/dotnet/maui/)
- [MSAL.NET Documentation](https://learn.microsoft.com/entra/msal/dotnet/)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [SQLite-net PCL](https://github.com/praeclarum/sqlite-net)

**Sample Code**:
- [.NET MAUI Samples](https://github.com/dotnet/maui-samples)
- [MSAL Mobile Samples](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/tree/main/tests/devapps)

**Support**:
- [Stack Overflow - .NET MAUI Tag](https://stackoverflow.com/questions/tagged/maui)
- [GitHub Discussions](https://github.com/dotnet/maui/discussions)
