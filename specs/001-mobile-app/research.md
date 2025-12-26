# Research: .NET MAUI Mobile App

**Feature**: 001-mobile-app
**Date**: 2025-12-26
**Purpose**: Document technology decisions and best practices for mobile app implementation

## Overview

This document captures research findings and technical decisions for building a cross-platform .NET MAUI chat application that integrates with the MotorcycleRAG.API backend.

## Technology Decisions

### 1. Local Data Persistence: SQLite

**Decision**: Use **sqlite-net-pcl** for local conversation and user memory persistence

**Rationale**:
- **Proven Track Record**: SQLite is the industry standard for mobile local databases (used by iOS, Android natively)
- **Performance**: Excellent read/write performance for the expected workload (conversation CRUD, search)
- **Query Capabilities**: Full SQL support for complex queries (conversation search, filtering by date)
- **File Size**: Efficient storage format, easily handles 100MB limit with thousands of conversations
- **MAUI Integration**: Well-documented with native .NET MAUI support via sqlite-net-pcl package
- **ACID Compliance**: Transactions ensure data integrity during conversation pruning and user memory extraction

**Alternatives Considered**:
- **LiteDB**: NoSQL document database, simpler API but less query flexibility; overkill for this use case
- **Realm**: Mobile-first database but heavier footprint and learning curve
- **Raw File Storage**: Too complex to implement search and transactions manually

**Implementation Pattern**:
```csharp
// Repository pattern with async/await
public interface IConversationRepository
{
    Task<List<ConversationEntity>> GetAllAsync();
    Task<ConversationEntity> GetByIdAsync(string id);
    Task<List<ConversationEntity>> SearchAsync(string query);
    Task<int> InsertAsync(ConversationEntity conversation);
    Task<int> UpdateAsync(ConversationEntity conversation);
    Task<int> DeleteAsync(string id);
    Task<long> GetTotalStorageSizeAsync();
}
```

**Package**: `sqlite-net-pcl` (latest stable)

---

### 2. Authentication: Microsoft.Identity.Client (MSAL)

**Decision**: Use **Microsoft.Identity.Client (MSAL)** for Microsoft Entra External ID / B2C authentication

**Rationale**:
- **Official SDK**: Microsoft's official library for Entra authentication in mobile apps
- **Token Management**: Automatic token caching, refresh, and renewal
- **Platform Support**: Native integration with iOS, Android, Windows authentication brokers
- **Security**: Secure token storage using platform-specific mechanisms (Keychain, etc.)
- **B2C Support**: Full support for Entra External ID / B2C with social identity providers

**Implementation Pattern**:
```csharp
// Public client application with platform-specific configuration
var app = PublicClientApplicationBuilder
    .Create(clientId)
    .WithB2CAuthority(authority)
    .WithRedirectUri(redirectUri)
    .WithIosKeychainSecurityGroup("com.motorcyclerag.mobile")
    .Build();

// Token acquisition with silent fallback
AuthenticationResult result;
try
{
    result = await app.AcquireTokenSilent(scopes, firstAccount).ExecuteAsync();
}
catch (MsalUiRequiredException)
{
    result = await app.AcquireTokenInteractive(scopes)
        .WithParentActivityOrWindow(parentWindow)
        .ExecuteAsync();
}
```

**Token Storage**:
- Tokens cached automatically by MSAL using platform-secure storage
- Access token included in `Authorization: Bearer {token}` header for API calls
- Automatic refresh before expiration

**Configuration**:
- Client ID, Authority (B2C tenant), Redirect URI stored in app configuration
- Scopes: `https://{tenant}.onmicrosoft.com/{api-id}/user_impersonation`

**Package**: `Microsoft.Identity.Client` (latest stable, 4.x+)

---

### 3. MVVM Framework: CommunityToolkit.Mvvm

**Decision**: Use **CommunityToolkit.Mvvm** for MVVM implementation

**Rationale**:
- **Source Generators**: Automatic boilerplate generation for ObservableProperty and RelayCommand
- **Performance**: Zero reflection overhead, compile-time code generation
- **Official**: Maintained by Microsoft as part of the .NET Community Toolkit
- **Modern C#**: Uses attributes and partial classes for clean code
- **MAUI Optimized**: Designed for .NET MAUI and modern XAML frameworks

**Implementation Pattern**:
```csharp
// ViewModel with source-generated properties and commands
[ObservableObject]
public partial class ChatViewModel
{
    [ObservableProperty]
    private string questionText;

    [ObservableProperty]
    private ObservableCollection<ChatMessage> messages;

    [ObservableProperty]
    private bool isSending;

    [RelayCommand]
    private async Task SendQuestionAsync()
    {
        IsSending = true;
        try
        {
            var response = await _apiClient.SendQueryAsync(QuestionText);
            Messages.Add(new ChatMessage { ... });
            QuestionText = string.Empty;
        }
        finally
        {
            IsSending = false;
        }
    }
}
```

**Benefits**:
- Reduces boilerplate by ~70% (no manual INotifyPropertyChanged)
- Compile-time safety (errors caught at build time, not runtime)
- Testable (ViewModels are POCO with no UI dependencies)

**Package**: `CommunityToolkit.Mvvm` (latest stable, 8.x+)

---

### 4. HTTP Client Management: Typed HttpClient with Dependency Injection

**Decision**: Use **typed HttpClient** registered in dependency injection with Polly resilience policies

**Rationale**:
- **Singleton Pattern**: Single HttpClient instance avoids socket exhaustion
- **DI Integration**: Proper lifecycle management via MAUI's built-in DI container
- **Resilience**: Polly integration for retry policies, circuit breaker, timeout
- **Testability**: Interface-based design allows mocking for unit tests
- **Header Management**: Centralized authentication header injection

**Implementation Pattern**:
```csharp
// Service registration in MauiProgram.cs
builder.Services.AddHttpClient<IApiClient, MotorcycleRagApiClient>(client =>
{
    client.BaseAddress = new Uri(configuration["ApiBaseUrl"]);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddTransientHttpErrorPolicy(policy =>
    policy.WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))))
.AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30)));

// Typed client with authentication injection
public class MotorcycleRagApiClient : IApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IAuthenticationService _authService;

    public MotorcycleRagApiClient(HttpClient httpClient, IAuthenticationService authService)
    {
        _httpClient = httpClient;
        _authService = authService;
    }

    public async Task<QueryResponse> SendQueryAsync(string question, CancellationToken ct)
    {
        var token = await _authService.GetAccessTokenAsync();
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var request = new QueryRequest { Query = question, ... };
        var response = await _httpClient.PostAsJsonAsync("/api/motorcycles/query", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<QueryResponse>();
    }
}
```

**Resilience Policies**:
- **Retry**: 3 attempts with exponential backoff (2^attempt seconds)
- **Timeout**: 30 seconds per request
- **Circuit Breaker**: Break after 5 consecutive failures, 1-minute wait (optional)

**Packages**:
- `System.Net.Http.Json` (built-in .NET)
- `Microsoft.Extensions.Http.Polly` (for resilience policies)

---

### 5. Secure Token Storage: MAUI SecureStorage API

**Decision**: Use **.NET MAUI SecureStorage** API for authentication token storage

**Rationale**:
- **Cross-Platform**: Single API that abstracts platform-specific secure storage
- **Native Security**: Uses iOS Keychain, Android EncryptedSharedPreferences, Windows Credential Locker
- **Simple API**: Key-value storage with async methods
- **MSAL Integration**: MSAL automatically uses platform-secure storage for tokens

**Implementation Pattern**:
```csharp
// MSAL handles token storage automatically, but for custom data:
public class SecureStorageService
{
    public async Task SetAsync(string key, string value)
    {
        await SecureStorage.SetAsync(key, value);
    }

    public async Task<string> GetAsync(string key)
    {
        try
        {
            return await SecureStorage.GetAsync(key);
        }
        catch (Exception)
        {
            return null; // Key not found or storage unavailable
        }
    }

    public void Remove(string key)
    {
        SecureStorage.Remove(key);
    }
}
```

**Platform-Specific Behavior**:
- **iOS**: Keychain with appropriate access group (`com.motorcyclerag.mobile`)
- **Android**: EncryptedSharedPreferences (API 23+) or legacy SharedPreferences with encryption
- **Windows**: Windows Credential Locker (PasswordVault API)

**Security Considerations**:
- Data survives app reinstall on iOS/Android (synced with device backup)
- On Windows, data is user-specific and encrypted
- Use consistent keys (e.g., `auth_token`, `user_id`, `refresh_token`)

**Built-in**: No additional packages required (part of .NET MAUI)

---

### 6. Performance Optimization: CollectionView Best Practices

**Decision**: Use **CollectionView** with virtualization, data template caching, and efficient bindings

**Rationale**:
- **Virtualization**: Only renders visible items + buffer, critical for 50+ message conversations
- **Smooth Scrolling**: Designed for 60 FPS performance on mobile devices
- **Data Template Caching**: Reuses templates instead of recreating UI elements
- **Incremental Loading**: Supports load-more scenarios for very long conversations

**Implementation Pattern**:
```xaml
<!-- Conversation list with optimized CollectionView -->
<CollectionView ItemsSource="{Binding Conversations}"
                SelectionMode="Single"
                RemainingItemsThreshold="5"
                RemainingItemsThresholdReachedCommand="{Binding LoadMoreCommand}">
    <CollectionView.ItemTemplate>
        <DataTemplate>
            <!-- Simple, flat UI hierarchy -->
            <Grid Padding="12" RowDefinitions="Auto,Auto">
                <Label Text="{Binding Title}" FontSize="16" FontAttributes="Bold" />
                <Label Grid.Row="1" Text="{Binding Preview}" FontSize="14" TextColor="Gray" />
            </Grid>
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

**Optimization Techniques**:
1. **Keep Item Templates Simple**: Flat hierarchy, minimal nesting
2. **Avoid Converters in Bindings**: Pre-compute values in ViewModel
3. **Use Compiled Bindings**: `x:DataType` for compile-time binding validation
4. **Virtualization Enabled by Default**: CollectionView handles this automatically
5. **Recycle Elements**: Set `ItemsUpdatingScrollMode="KeepLastItemInView"` for chat behavior
6. **Limit ObservableCollection Changes**: Batch updates instead of individual adds

**Search Performance**:
- SQLite FTS (Full-Text Search) for conversation content search
- Debounce search input (500ms delay) to avoid excessive queries
- Display results incrementally as they're found

**Target**: 60 FPS scrolling with 50-100 visible messages

---

### 7. Platform-Specific UI: Conditional Compilation and Platform Folders

**Decision**: Use **OnPlatform XAML markup**, **platform folders**, and **conditional compilation** for platform-specific UI

**Rationale**:
- **Code Sharing**: Maximize shared code (Views, ViewModels, Services) while respecting platform conventions
- **Native Feel**: Each platform looks and behaves according to its design guidelines
- **Maintainability**: Platform-specific code isolated in predictable locations
- **MAUI Built-in**: Framework provides multiple mechanisms for platform customization

**Implementation Patterns**:

**1. XAML OnPlatform for Styling**:
```xaml
<!-- Platform-specific padding and fonts -->
<ContentPage.Padding>
    <OnPlatform x:TypeArguments="Thickness">
        <On Platform="iOS" Value="0,20,0,0" />
        <On Platform="Android" Value="0" />
        <On Platform="WinUI" Value="12" />
    </OnPlatform>
</ContentPage.Padding>

<Label FontFamily="{OnPlatform iOS='San Francisco', Android='Roboto', WinUI='Segoe UI'}" />
```

**2. Platform Folders for Platform-Specific Views**:
```
Views/
├── Shared/
│   └── ChatPage.xaml (shared UI)
└── Platforms/
    ├── iOS/
    │   └── ChatPage.iOS.xaml (iOS-specific overrides)
    ├── Android/
    │   └── ChatPage.Android.xaml (Android-specific overrides)
    └── Windows/
        └── ChatPage.Windows.xaml (Windows-specific overrides)
```

**3. Conditional Compilation for Code**:
```csharp
#if IOS
    var statusBarHeight = 20; // iOS status bar
#elif ANDROID
    var statusBarHeight = 0; // Android handles this
#elif WINDOWS
    var statusBarHeight = 32; // Windows title bar
#endif
```

**4. Platform-Specific Services**:
```csharp
// Service registration with platform-specific implementations
#if IOS
builder.Services.AddSingleton<IHapticFeedback, iOSHapticFeedback>();
#elif ANDROID
builder.Services.AddSingleton<IHapticFeedback, AndroidHapticFeedback>();
#endif
```

**Platform Guidelines to Follow**:
- **iOS**: Human Interface Guidelines (HIG) - Back button navigation, swipe gestures, bottom tab bar
- **Android**: Material Design - FAB buttons, navigation drawer, top app bar
- **Windows**: Fluent Design - Command bar, navigation view, acrylic effects

**Example Platform Differences**:
| Aspect | iOS | Android | Windows |
|--------|-----|---------|---------|
| Navigation | Modal sheets, back swipe | Back button, drawer | Back button, hamburger |
| Primary Actions | Bottom sheet | FAB | Command bar |
| List Actions | Swipe-to-delete | Long-press menu | Right-click menu |
| Fonts | SF Pro | Roboto | Segoe UI |
| Spacing | 8pt grid | 8dp grid | 4px grid |

---

## User Memory Extraction Strategy

### Decision: Keyword Pattern Matching with Local Processing

**Approach**: Extract user memory on-device using keyword/phrase pattern matching before conversation pruning

**Rationale**:
- **Privacy**: Processing stays on-device, no conversation content sent to backend for extraction
- **Performance**: Simple regex/pattern matching is fast enough for mobile
- **Simplicity**: No additional backend API required
- **Real-time**: Can extract memory as conversation happens, not just at prune time

**Extraction Triggers**:
1. **On Conversation Prune**: Before deleting conversation, scan for extractable information
2. **Periodic Background Task**: Scan recent conversations (last 24 hours) nightly
3. **Manual Trigger**: User can request "Learn from this conversation" explicitly

**Extraction Patterns**:
```csharp
// Pattern-based extraction
public class UserMemoryExtractor
{
    private static readonly Dictionary<string, Regex> Patterns = new()
    {
        ["motorcycles_owned"] = new Regex(@"I (own|have|ride) (?:a |an )?(\d{4} )?([A-Z][a-z]+ [A-Z0-9]+)", RegexOptions.IgnoreCase),
        ["riding_style"] = new Regex(@"I (prefer|like|enjoy|do) (\w+) riding", RegexOptions.IgnoreCase),
        ["expertise_level"] = new Regex(@"I('m| am) (?:a |an )?(beginner|novice|intermediate|advanced|expert)", RegexOptions.IgnoreCase),
        ["maintenance_preference"] = new Regex(@"I (do|perform|handle) my own (maintenance|repairs|service)", RegexOptions.IgnoreCase),
    };

    public async Task<List<UserMemoryItem>> ExtractFromConversationAsync(ConversationSession conversation)
    {
        var memories = new List<UserMemoryItem>();
        foreach (var message in conversation.Messages.Where(m => m.Sender == "user"))
        {
            foreach (var (category, pattern) in Patterns)
            {
                var match = pattern.Match(message.Content);
                if (match.Success)
                {
                    memories.Add(new UserMemoryItem
                    {
                        Category = category,
                        Value = match.Groups[match.Groups.Count - 1].Value,
                        SourceConversationId = conversation.Id,
                        ExtractedAt = DateTime.UtcNow
                    });
                }
            }
        }
        return memories;
    }
}
```

**Storage Format**: Key-Value Pairs with Metadata
```csharp
public class UserMemoryEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Category { get; set; } // "motorcycles_owned", "riding_style", etc.
    public string Value { get; set; } // "2023 Yamaha R1M", "sport riding", etc.
    public string SourceConversationId { get; set; } // Traceability
    public DateTime ExtractedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsActive { get; set; } = true; // Can be deactivated instead of deleted
}
```

**Conflict Resolution**:
- New value for same category → Update existing, store previous value in history
- User explicitly contradicts → Ask for confirmation ("You previously said you own an R1. Update to R6?")
- Multiple motorcycles → Store as separate entries, all active

**Integration with API Context**:
```json
// Context field in API request
{
  "query": "What oil should I use?",
  "context": {
    "sessionId": "conv-123",
    "userMemory": {
      "motorcycles_owned": ["2023 Yamaha R1M"],
      "riding_style": "sport riding",
      "expertise_level": "intermediate"
    }
  }
}
```

**User Control**:
- View all extracted memories in Profile page
- Edit/delete individual memory items
- Clear all memories (reset personalization)
- Toggle "Learn from conversations" on/off

**Privacy Considerations**:
- Only extract factual statements (motorcycles, preferences), not sensitive personal data
- No extraction of names, locations, financial information
- User can audit what's been extracted
- Memory stored locally only (not synced to backend except in API context)

---

## Implementation Dependencies

**NuGet Packages** (minimum versions):
```xml
<ItemGroup>
    <!-- Core MAUI -->
    <PackageReference Include="Microsoft.Maui.Controls" Version="10.0.0" />
    <PackageReference Include="Microsoft.Maui.Essentials" Version="10.0.0" />

    <!-- Authentication -->
    <PackageReference Include="Microsoft.Identity.Client" Version="4.66.0" />

    <!-- MVVM -->
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.0" />

    <!-- Local Database -->
    <PackageReference Include="sqlite-net-pcl" Version="1.9.172" />
    <PackageReference Include="SQLitePCLRaw.bundle_green" Version="2.1.10" />

    <!-- HTTP & Resilience -->
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http.Polly" Version="10.0.0" />

    <!-- JSON Serialization -->
    <PackageReference Include="System.Text.Json" Version="10.0.0" />

    <!-- Testing (Test projects) -->
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="Moq" Version="4.20.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.0" />
</ItemGroup>
```

**Platform-Specific Requirements**:
- **iOS**: Minimum deployment target iOS 15.0
- **Android**: Minimum API 29 (Android 10), Target API 35 (Android 15)
- **Windows**: Minimum Windows 10 version 1809

---

## Summary of Decisions

| Area | Technology | Rationale |
|------|------------|-----------|
| Local Persistence | sqlite-net-pcl | Industry standard, excellent query support, ACID compliance |
| Authentication | Microsoft.Identity.Client (MSAL) | Official SDK, automatic token management, platform security |
| MVVM Framework | CommunityToolkit.Mvvm | Source generators, zero reflection, modern C# |
| HTTP Client | Typed HttpClient + Polly | Singleton pattern, DI integration, resilience policies |
| Secure Storage | MAUI SecureStorage | Cross-platform abstraction over Keychain/EncryptedSharedPreferences |
| Performance | CollectionView virtualization | 60 FPS target, efficient rendering, incremental loading |
| Platform UI | OnPlatform + Platform Folders | Code sharing with platform-specific customization |
| User Memory | Keyword pattern matching | Privacy-first, on-device processing, simple patterns |

All decisions align with .NET MAUI best practices, security requirements, and the project constitution while maximizing code sharing and maintainability.
