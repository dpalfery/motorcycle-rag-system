# Research Notes — 001-system-spec

## Decisions

### 1) Web UI stack: React 19 (chosen)
**Decision**: Use the existing React 19 + Vite UI at `1-Presentation/MotorcycleRag.WebUI/` as the primary web application.

**Rationale**:
- The repository already contains a React 19 app (`react`/`react-dom` 19.x) and modern tooling.
- The user explicitly requested “C# + React 19”.

**Notes**:
- The repo also includes a YARP-based BFF at `1-Presentation/MotorcycleRag.WebUI.BFF/` to front the SPA and attach user access tokens to downstream API calls.

**Alternatives considered**:
- SvelteKit + Open WebUI base (documented in `6-Docs/ui-technology-stack.md`).
  - Rejected for this plan due to conflict with current repo implementation and stated direction; treat that document as legacy/out-of-date unless you want to revive it.

### 2) Admin Ingestion UI: .NET MAUI app (chosen)
**Decision**: Implement a dedicated .NET MAUI admin application as a separate project, targeting .NET 10 (Windows-first).

**Rationale**:
- Requirement calls out a Windows admin app.
- MAUI keeps the option open for future cross-platform while still supporting local model execution (chunking/vectorization) and local file system access.

**Alternatives considered**:
- Electron/Tauri desktop wrapper around the web UI.
  - Rejected because the requirement calls for local-model processing and a “Windows app of some sort” but does not require web tech; staying native keeps footprint and deployment simpler.
- WPF / WinUI 3.
  - Rejected because you prefer MAUI and we want one codebase that can grow beyond Windows if needed.

### 3) Local chunking + vectorization in MAUI app
**Decision**: Do chunking and embedding generation locally in the MAUI admin app via an on-device embedding model and ship only chunks+metadata (and optionally vectors) to the API.

**Rationale**:
- Matches the requirement that local processing does not require a cloud-hosted model call.
- Reduces cloud cost and avoids sending raw documents off-machine during preprocessing.

**Alternatives considered**:
- Chunk in-app, vectorize in cloud.
  - Rejected: violates the explicit “local chunking and vectorizing” requirement.

**NEEDS CLARIFICATION resolved (provisional)**:
- Exact local model packaging: package an ONNX-based embedding model with the Windows MAUI app and run inference locally via ONNX Runtime; keep the model choice/configuration pluggable.

### 3a) Local embedding model runtime (packaging + execution)
**Decision**: Use ONNX Runtime in the MAUI app to run a packaged embedding model locally (no cloud call) and produce vectors compatible with the server-side retrieval index.

**Rationale**:
- Satisfies the requirement that local processing does not require a cloud-hosted model call.
- Keeps the model distribution as a deterministic app artifact (versioned with the admin app).

### 4) “Beyond a shadow of a doubt” correctness
**Decision**: Implement two complementary correctness strategies:
1) Agentic verification: retrieval/claim-generation step + independent verification step before final answer.
2) Structured citations: each claim must have at least one citation with precise location.

**Rationale**:
- Verification reduces hallucination risk.
- Citations provide user-verifiable provenance.

**Alternatives considered**:
- “Single pass RAG with citations only”.
  - Rejected: citations alone don’t guarantee claims match evidence.

### 5) Website scrape + index capability
**Decision**: Maintain an admin-managed allow-list of websites and run scrape/index jobs into the same retrieval corpus with clear attribution.

**Rationale**:
- Curated sources reduce risk and improve consistency.
- Indexing reduces repeated live browsing.

**Alternatives considered**:
- Unrestricted web browsing per query.
  - Rejected: higher risk and less controllable.

### 6) MCP tool configuration in MAUI admin app
**Decision**: Add MCP server/tool configuration management to the MAUI admin app (create/update/enable/disable/validate), plus an audit trail. The MAUI app ships configuration changes to the API layer.

**Rationale**:
- Centralizes operational/admin workflows (ingestion + MCP configuration) in one privileged desktop app.
- Enables safe operational control without redeploy.

**Alternatives considered**:
- Web application UI.
  - Rejected: you prefer MCP configuration to live in the MAUI admin app.
- Config file only.
  - Rejected: operational friction and no UI governance.

**Live update approach (provisional)**:
- Persist MCP configuration (non-secret values) as versioned JSON in Azure App Configuration; store secrets as Key Vault references.
- Agents/orchestrator consume the active version via an App Configuration refresh/sentinel strategy, applying updates to new runs rather than mutating in-flight runs.

### 6a) MCP configuration persistence + live updates (chosen)
**Decision**: Use Azure App Configuration as the shared store for MCP configuration (versioned; one active version), with Key Vault references for any secrets.

**Rationale**:
- Designed for configuration distribution to multiple API instances.
- Supports safe refresh patterns (sentinel key + refresh interval) without introducing a bespoke pub/sub system.

### 7) Auth + user management + SKU plans
**Decision**: Add authentication, user profile, and plan enforcement:
- Free (10 requests/day), Plus (100 requests/day), Pro (unlimited)
- Track usage per user per day; enforce at query entry point.

Auth decisions:
- Customers: Microsoft Entra External ID / B2C (OIDC) with social identity providers (Google, GitHub, Microsoft, Facebook).
- Admins/operators: Microsoft Entra ID (workforce).
- Admin authorization: Entra application roles (`Admin`, `Operator`, `Viewer`).

**Rationale**:
- Required for productization and abuse control.

**Alternatives considered**:
- Anonymous-only.
  - Rejected: cannot support per-user plans/limits.

### 8) Security standard (chosen)
**Decision**: Target OWASP ASVS v5.0.0 at ASVS Level 2.

**Rationale**:
- Matches an internet-facing app with authentication and administrative capabilities.
- Provides a concrete, auditable standard for engineering + security testing.

### 9) Persistence store for users/plans/usage/audit/web-sources (chosen)
**Decision**: Use a relational store (Azure SQL in production) accessed via ADO.NET behind Domain/Application interfaces.

**Rationale**:
- Fits strongly-relational entities (plan/SKU, usage records, audit events, web source registry).
- Aligns with repo constitution preference of ADO.NET (no EF for DAL).

## Open Items (Implementation choices to finalize during build)
- Exact schema for citations and claim verification results in API response.

---

## .NET MAUI Mobile App Development Best Practices

### 10) Local Data Persistence: SQLite vs LiteDB

**Decision**: Use **SQLite** with sqlite-net-pcl for local conversation history and user memory.

**Rationale**:
- **Mature ecosystem**: sqlite-net-pcl is the de facto standard for .NET MAUI with excellent community support
- **Performance**: Write-Ahead Logging (WAL) mode enables concurrent read/write operations without blocking
- **Size efficiency**: Compact storage suitable for 100MB limits
- **Query capability**: Full LINQ support via SQLite.NET ORM for complex search operations
- **Cross-platform**: Native support on all .NET MAUI platforms (iOS, Android, Windows, Mac Catalyst)
- **Async-first**: SQLiteAsyncConnection provides proper async/await patterns for mobile apps

**Implementation Pattern**:
```csharp
public class ConversationDatabase
{
    SQLiteAsyncConnection database;

    public ConversationDatabase()
    {
        database = new SQLiteAsyncConnection(DatabasePath, Flags);
        await database.EnableWriteAheadLoggingAsync(); // Enable WAL for performance
    }

    public const SQLiteOpenFlags Flags =
        SQLite.SQLiteOpenFlags.ReadWrite |
        SQLite.SQLiteOpenFlags.Create |
        SQLite.SQLiteOpenFlags.SharedCache;
}
```

**Dependency Injection**:
Register as singleton in MauiProgram.cs:
```csharp
builder.Services.AddSingleton<ConversationDatabase>();
```

**LiteDB Comparison**:
- LiteDB is a document database (NoSQL) with simpler schema management
- SQLite provides better performance for frequent small reads/writes (conversation messages)
- SQLite has broader tooling support and debugging capabilities
- For this use case (structured conversation data with search), SQLite's relational model is more appropriate

**Data Model Example**:
```csharp
public class ConversationMessage
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string ConversationId { get; set; }

    public string Role { get; set; } // user/assistant/system
    public string Content { get; set; }
    public DateTime Timestamp { get; set; }

    [Indexed]
    public string UserId { get; set; }
}
```

### 11) MSAL Authentication Best Practices

**Decision**: Implement Microsoft Entra External ID / B2C authentication using Microsoft.Identity.Client (MSAL) with broker support and token caching.

**Rationale**:
- Aligns with Decision #7 (Auth + user management)
- Broker authentication (WAM on Windows, platform brokers on iOS/Android) provides enhanced security
- Built-in token caching reduces authentication prompts
- Supports social identity providers (Google, GitHub, Microsoft, Facebook)

**Implementation Pattern**:

**1. Initialize Public Client Application**:
```csharp
public class AuthenticationService
{
    private readonly IPublicClientApplication _pca;

    public AuthenticationService()
    {
        _pca = PublicClientApplicationBuilder
            .Create(ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, TenantId)
            .WithRedirectUri("msal{ClientId}://auth") // Platform-specific redirect
            .WithBroker() // Enable broker for enhanced security
            .Build();

        // Configure token cache serialization (see below)
        TokenCacheHelper.EnableSerialization(_pca.UserTokenCache);
    }
}
```

**2. Silent Token Acquisition with Fallback Pattern**:
```csharp
public async Task<AuthenticationResult> AcquireTokenAsync()
{
    var accounts = await _pca.GetAccountsAsync();

    try
    {
        // Always try silent acquisition first
        return await _pca.AcquireTokenSilent(Scopes, accounts.FirstOrDefault())
            .ExecuteAsync();
    }
    catch (MsalUiRequiredException)
    {
        // Fallback to interactive authentication only when required
        return await _pca.AcquireTokenInteractive(Scopes)
            .WithParentActivityOrWindow(GetParentWindow()) // Platform-specific
            .WithPrompt(Prompt.SelectAccount)
            .ExecuteAsync();
    }
}
```

**3. Token Cache Serialization**:
Use .NET MAUI SecureStorage for cross-platform token persistence:

```csharp
public static class TokenCacheHelper
{
    private const string CacheFileName = "msal_token_cache.dat";

    public static void EnableSerialization(ITokenCache tokenCache)
    {
        tokenCache.SetBeforeAccess(BeforeAccessNotification);
        tokenCache.SetAfterAccess(AfterAccessNotification);
    }

    private static void BeforeAccessNotification(TokenCacheNotificationArgs args)
    {
        var cacheData = SecureStorage.GetAsync(CacheFileName).GetAwaiter().GetResult();
        if (!string.IsNullOrEmpty(cacheData))
        {
            args.TokenCache.DeserializeMsalV3(Convert.FromBase64String(cacheData));
        }
    }

    private static void AfterAccessNotification(TokenCacheNotificationArgs args)
    {
        if (args.HasStateChanged)
        {
            var data = Convert.ToBase64String(args.TokenCache.SerializeMsalV3());
            SecureStorage.SetAsync(CacheFileName, data).GetAwaiter().GetResult();
        }
    }
}
```

**4. Platform-Specific Considerations**:

**Android**: Specify parent activity:
```csharp
.WithParentActivityOrWindow(Platform.CurrentActivity)
```

**iOS**: Configure Keychain security group in Entitlements.plist:
```csharp
.WithIosKeychainSecurityGroup("com.motorcyclerag.shared")
```

**Windows**: Call from UI thread for embedded browser:
```csharp
.WithParentActivityOrWindow(new WindowInteropHelper(this).Handle)
```

**5. Token Refresh Pattern**:
MSAL handles token refresh automatically via AcquireTokenSilent:
- Access tokens typically expire in 1 hour
- Refresh tokens used automatically when access token expires
- No manual refresh logic required

**6. Logout Pattern**:
```csharp
public async Task SignOutAsync()
{
    var accounts = await _pca.GetAccountsAsync();
    foreach (var account in accounts)
    {
        await _pca.RemoveAsync(account);
    }
    SecureStorage.Remove("msal_token_cache.dat");
}
```

### 12) MVVM Architecture Framework

**Decision**: Use **CommunityToolkit.Mvvm** (official Microsoft MVVM toolkit) for ViewModels, Commands, and data binding.

**Rationale**:
- Official Microsoft recommendation for .NET MAUI
- Source generator-based approach reduces boilerplate
- Excellent performance (no reflection overhead)
- À la carte design - use only what you need
- Platform-agnostic and well-maintained
- Modern async command support (AsyncRelayCommand)

**Core Components**:

**1. ObservableObject with Source Generators**:
```csharp
using CommunityToolkit.Mvvm.ComponentModel;

public partial class ConversationViewModel : ObservableObject
{
    [ObservableProperty]
    private string _userMessage;

    [ObservableProperty]
    private ObservableCollection<Message> _messages;

    [ObservableProperty]
    private bool _isLoading;
}
```

Source generators automatically create:
- INotifyPropertyChanged implementation
- Property change notifications
- Backing fields

**2. RelayCommand and AsyncRelayCommand**:
```csharp
using CommunityToolkit.Mvvm.Input;

public partial class ConversationViewModel : ObservableObject
{
    private readonly IConversationService _conversationService;

    public ConversationViewModel(IConversationService conversationService)
    {
        _conversationService = conversationService;
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(UserMessage))
            return;

        IsLoading = true;

        try
        {
            var response = await _conversationService.SendMessageAsync(UserMessage);
            Messages.Add(new Message { Role = "user", Content = UserMessage });
            Messages.Add(new Message { Role = "assistant", Content = response });
            UserMessage = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClearHistory))]
    private void ClearHistory()
    {
        Messages.Clear();
    }

    private bool CanClearHistory() => Messages.Count > 0;
}
```

**3. Messaging System for Loose Coupling**:
```csharp
using CommunityToolkit.Mvvm.Messaging;

// Message definition
public class ConversationUpdatedMessage
{
    public string ConversationId { get; set; }
}

// Sender
WeakReferenceMessenger.Default.Send(new ConversationUpdatedMessage
{
    ConversationId = conversationId
});

// Receiver
public partial class MainViewModel : ObservableObject, IRecipient<ConversationUpdatedMessage>
{
    public MainViewModel()
    {
        WeakReferenceMessenger.Default.Register(this);
    }

    public void Receive(ConversationUpdatedMessage message)
    {
        // Handle conversation update
    }
}
```

**4. View-ViewModel Connection**:
Use View First composition with dependency injection:

```xml
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:vm="clr-namespace:MotorcycleRAG.ViewModels"
             x:DataType="vm:ConversationViewModel">
    <ContentPage.BindingContext>
        <vm:ConversationViewModel />
    </ContentPage.BindingContext>
</ContentPage>
```

Or inject via constructor for complex scenarios:
```csharp
public partial class ConversationPage : ContentPage
{
    public ConversationPage(ConversationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
```

**5. Service Registration**:
```csharp
// MauiProgram.cs
builder.Services.AddSingleton<MainViewModel>();
builder.Services.AddTransient<ConversationViewModel>();
builder.Services.AddTransient<ConversationPage>();
```

**Alternatives Considered**:
- **Prism Library**: More opinionated, heavier framework. Rejected for simplicity.
- **ReactiveUI**: Reactive programming model adds complexity. Rejected for learning curve.
- **Manual MVVM**: Too much boilerplate. CommunityToolkit source generators eliminate this.

### 13) HTTP Client Management

**Decision**: Use **IHttpClientFactory** with typed clients, Polly integration for resilience, and centralized header management.

**Rationale**:
- Prevents socket exhaustion from repeated HttpClient instantiation
- Handles DNS changes properly (pooled HttpMessageHandler with 2-minute lifetime)
- Integrates seamlessly with Polly for retry/circuit breaker patterns
- Centralizes configuration (base URLs, timeouts, headers)
- Aligns with existing API resilience patterns (see CLAUDE.md ResilienceService)

**Implementation Pattern**:

**1. Typed Client Registration**:
```csharp
// MauiProgram.cs
builder.Services.AddHttpClient<IMotorcycleRagApiService, MotorcycleRagApiService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"]);
    client.Timeout = TimeSpan.FromSeconds(30);
})
.AddPolicyHandler(GetRetryPolicy())
.AddPolicyHandler(GetCircuitBreakerPolicy())
.SetHandlerLifetime(TimeSpan.FromMinutes(5));
```

**2. Polly Resilience Policies**:
```csharp
using Polly;
using Polly.Extensions.Http;

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryAttempt, context) =>
            {
                Debug.WriteLine($"Retry {retryAttempt} after {timespan.TotalSeconds}s");
            });
}

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30));
}
```

**3. Typed Client Service**:
```csharp
public interface IMotorcycleRagApiService
{
    Task<ConversationResponse> SendMessageAsync(string message, CancellationToken ct = default);
    Task<List<Conversation>> GetConversationsAsync(CancellationToken ct = default);
}

public class MotorcycleRagApiService : IMotorcycleRagApiService
{
    private readonly HttpClient _httpClient;
    private readonly IAuthenticationService _authService;
    private readonly JsonSerializerOptions _jsonOptions;

    public MotorcycleRagApiService(
        HttpClient httpClient,
        IAuthenticationService authService)
    {
        _httpClient = httpClient;
        _authService = authService;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<ConversationResponse> SendMessageAsync(
        string message,
        CancellationToken ct = default)
    {
        // Add authentication header
        var token = await _authService.GetAccessTokenAsync();
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var request = new ConversationRequest { Message = message };
        var content = new StringContent(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/api/conversations", content, ct);
        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ConversationResponse>(responseContent, _jsonOptions);
    }
}
```

**4. Header Management Pattern**:
```csharp
public class AuthenticatedHttpClientHandler : DelegatingHandler
{
    private readonly IAuthenticationService _authService;

    public AuthenticatedHttpClientHandler(IAuthenticationService authService)
    {
        _authService = authService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _authService.GetAccessTokenAsync();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Correlation-Id", Guid.NewGuid().ToString());

        return await base.SendAsync(request, cancellationToken);
    }
}

// Registration
builder.Services.AddTransient<AuthenticatedHttpClientHandler>();
builder.Services.AddHttpClient<IMotorcycleRagApiService, MotorcycleRagApiService>()
    .AddHttpMessageHandler<AuthenticatedHttpClientHandler>();
```

**5. Performance Optimization**:
- Avoid `ReadAsStringAsync()` for large responses; use `JsonSerializer.DeserializeAsync()` with stream:
```csharp
var stream = await response.Content.ReadAsStreamAsync(ct);
return await JsonSerializer.DeserializeAsync<ConversationResponse>(stream, _jsonOptions, ct);
```

**Platform-Specific Considerations**:
- **Android**: Enable cleartext HTTP for local development in AndroidManifest.xml
- **iOS**: Configure App Transport Security (ATS) exceptions for local development

### 14) Secure Storage for Authentication Tokens

**Decision**: Use **.NET MAUI SecureStorage API** for all authentication token storage across platforms.

**Rationale**:
- Platform-specific secure storage automatically selected:
  - **iOS/Mac Catalyst**: Keychain API
  - **Android**: EncryptedSharedPreferences (AES-256 GCM)
  - **Windows**: DataProtectionProvider
- Simple async API with minimal configuration
- Built into .NET MAUI (no additional dependencies)
- Designed specifically for sensitive data like tokens

**Platform-Specific Implementation Details**:

**1. iOS Keychain**:
- Storage: `KeyChain` API with `SecRecord`
- Identifier: `[YOUR-APP-BUNDLE-ID].microsoft.maui.essentials.preferences`
- Security: Hardware-backed encryption on devices with Secure Enclave
- Persistence: May sync with iCloud; survives app uninstall
- Configuration: Requires Keychain entitlement in Entitlements.plist for simulator

**2. Android EncryptedSharedPreferences**:
- Storage: File named `[YOUR-APP-PACKAGE-ID].microsoft.maui.essentials.preferences`
- Encryption:
  - Keys: Deterministically encrypted for lookups
  - Values: Non-deterministically encrypted using AES-256 GCM
- Persistence: Does NOT survive app uninstall
- Configuration: Auto Backup can interfere; configure selective backup exclusion

**3. Windows Credential Locker**:
- Storage:
  - Packaged apps: `ApplicationData.Current.LocalSettings`
  - Unpackaged apps: `securestorage.dat` JSON file
- Encryption: `DataProtectionProvider`
- Limitations:
  - Setting name max 255 characters
  - Individual setting max 8K bytes
  - Composite setting max 64K bytes

**Implementation Pattern**:

**1. Token Storage**:
```csharp
public class SecureTokenStorage : ITokenStorage
{
    private const string AccessTokenKey = "msal_access_token";
    private const string RefreshTokenKey = "msal_refresh_token";
    private const string ExpiresAtKey = "msal_expires_at";

    public async Task SaveTokenAsync(AuthenticationResult result)
    {
        try
        {
            await SecureStorage.Default.SetAsync(AccessTokenKey, result.AccessToken);
            await SecureStorage.Default.SetAsync(ExpiresAtKey, result.ExpiresOn.ToString("O"));

            // Refresh token is managed by MSAL token cache (see MSAL section)
        }
        catch (Exception ex)
        {
            // Handle encryption/storage errors
            Debug.WriteLine($"Token storage failed: {ex.Message}");
            throw;
        }
    }

    public async Task<string> GetAccessTokenAsync()
    {
        try
        {
            var token = await SecureStorage.Default.GetAsync(AccessTokenKey);

            if (string.IsNullOrEmpty(token))
                return null;

            // Check expiration
            var expiresAtStr = await SecureStorage.Default.GetAsync(ExpiresAtKey);
            if (DateTimeOffset.TryParse(expiresAtStr, out var expiresAt))
            {
                if (DateTimeOffset.UtcNow >= expiresAt.AddMinutes(-5)) // 5 min buffer
                    return null; // Token expired
            }

            return token;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Token retrieval failed: {ex.Message}");
            return null;
        }
    }

    public async Task ClearTokensAsync()
    {
        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(ExpiresAtKey);
        SecureStorage.Default.RemoveAll(); // Clear all secure storage
    }
}
```

**2. Error Handling**:
Always wrap SecureStorage calls in try-catch:
```csharp
try
{
    await SecureStorage.Default.SetAsync(key, value);
}
catch (Exception ex)
{
    // Possible scenarios:
    // - Device doesn't support secure storage
    // - Encryption key changed (iOS Keychain)
    // - Data corruption
    // - Storage quota exceeded
    Debug.WriteLine($"SecureStorage error: {ex.Message}");
}
```

**3. Best Practices**:
- **Small Data Only**: Designed for small amounts of text (tokens, API keys)
- **Performance**: May be slow with large data stores; limit to essential secrets
- **Platform Testing**: Test on physical devices; simulator/emulator behavior may differ
- **Backup Configuration**: On Android, exclude SecureStorage from Auto Backup to prevent decryption issues on restore

**4. Android Auto Backup Configuration**:
```xml
<!-- res/xml/backup_rules.xml -->
<?xml version="1.0" encoding="utf-8"?>
<full-backup-content>
    <exclude domain="sharedpref" path="[YOUR-APP-PACKAGE-ID].microsoft.maui.essentials.preferences"/>
</full-backup-content>
```

**5. iOS Keychain Entitlements** (for simulator):
```xml
<!-- Entitlements.plist -->
<key>keychain-access-groups</key>
<array>
    <string>$(AppIdentifierPrefix)com.motorcyclerag.shared</string>
</array>
```

### 15) Performance Optimization for 60 FPS Scrolling

**Decision**: Implement CollectionView with virtualization, optimized templates, and data source management for smooth 60 FPS scrolling with large conversation lists.

**Rationale**:
- CollectionView automatically uses native platform virtualization
- .NET 10 provides optimized CollectionView handlers for iOS/Mac Catalyst
- Proper configuration can achieve native performance even with hundreds/thousands of messages

**Implementation Pattern**:

**1. CollectionView Configuration**:
```xml
<CollectionView ItemsSource="{Binding Messages}"
                ItemSizingStrategy="MeasureFirstItem"
                RemainingItemsThreshold="10"
                RemainingItemsThresholdReached="LoadMoreMessages">
    <CollectionView.ItemsLayout>
        <LinearItemsLayout Orientation="Vertical"
                          ItemSpacing="8" />
    </CollectionView.ItemsLayout>

    <CollectionView.ItemTemplate>
        <DataTemplate x:DataType="models:Message">
            <!-- Optimized template (see below) -->
        </DataTemplate>
    </CollectionView.ItemTemplate>
</CollectionView>
```

**2. Item Sizing Strategy (Critical for Performance)**:
```xml
ItemSizingStrategy="MeasureFirstItem"
```

**Options**:
- **MeasureFirstItem**: Measures only first item; assumes uniform sizes. Use for chat messages with consistent layout. **Significantly improves performance**.
- **MeasureAllItems**: Default; measures each item individually. Use only when item sizes vary significantly.

**Performance Impact**: MeasureFirstItem can reduce layout time by 80%+ with large lists.

**3. Optimized DataTemplate Design**:
```xml
<DataTemplate x:DataType="models:Message">
    <Grid Padding="12" ColumnDefinitions="Auto,*" RowDefinitions="Auto,Auto">
        <!-- Avoid nested layouts where possible -->
        <Image Grid.Column="0" Grid.Row="0"
               Source="{Binding AvatarUrl}"
               HeightRequest="40" WidthRequest="40"
               Aspect="AspectFill">
            <Image.Clip>
                <EllipseGeometry RadiusX="20" RadiusY="20" Center="20,20"/>
            </Image.Clip>
        </Image>

        <Label Grid.Column="1" Grid.Row="0"
               Text="{Binding Role}"
               FontAttributes="Bold"
               Margin="8,0,0,4"/>

        <Label Grid.Column="1" Grid.Row="1"
               Text="{Binding Content}"
               LineBreakMode="WordWrap"
               Margin="8,0,0,0"/>
    </Grid>
</DataTemplate>
```

**Template Optimization Rules**:
- Minimize nesting depth (max 3 levels)
- Use Grid over StackLayout (Grid has better performance)
- Set explicit HeightRequest/WidthRequest where possible
- Use LineBreakMode="TailTruncation" for truncatable text
- Avoid bindings in loops (use x:DataType for compiled bindings)

**4. Data Virtualization (Incremental Loading)**:
```csharp
public partial class ConversationViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Message> _messages = new();

    private int _currentPage = 0;
    private const int PageSize = 50;

    [RelayCommand]
    private async Task LoadMoreMessagesAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;

        try
        {
            var newMessages = await _conversationService.GetMessagesAsync(
                conversationId,
                _currentPage * PageSize,
                PageSize);

            foreach (var message in newMessages)
            {
                Messages.Add(message);
            }

            _currentPage++;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
```

**5. Memory Management**:
```csharp
// Implement message pruning for very long conversations
public void PruneOldMessages(int keepCount = 200)
{
    while (Messages.Count > keepCount)
    {
        Messages.RemoveAt(0); // Remove oldest messages
    }
}

// Call periodically or on memory warning
public override void OnMemoryWarning()
{
    base.OnMemoryWarning();
    PruneOldMessages(100);
    GC.Collect();
}
```

**6. Compiled Bindings for Performance**:
Always use x:DataType for compiled bindings:
```xml
<ContentPage xmlns:models="clr-namespace:MotorcycleRAG.Models"
             x:DataType="vm:ConversationViewModel">
    <CollectionView ItemsSource="{Binding Messages}">
        <CollectionView.ItemTemplate>
            <DataTemplate x:DataType="models:Message">
                <!-- Bindings are compiled, not reflection-based -->
                <Label Text="{Binding Content}" />
            </DataTemplate>
        </CollectionView.ItemTemplate>
    </CollectionView>
</ContentPage>
```

**7. Image Optimization**:
```csharp
// Use image caching for avatars
<Image Source="{Binding AvatarUrl}"
       CachingEnabled="True"
       CacheValidity="7"/>
```

**8. Platform-Specific Optimizations**:

**iOS**:
- Use optimized handlers (default in .NET 10)
- Enable Fast Renderers for better performance

**Android**:
- Enable AAPT2 for resource compilation
- Use R8 code shrinker for release builds
- Enable Startup Tracing for app launch optimization

**9. Profiling and Measurement**:
```csharp
// Use Stopwatch for performance measurement
var sw = Stopwatch.StartNew();
await LoadMessagesAsync();
sw.Stop();
Debug.WriteLine($"Load time: {sw.ElapsedMilliseconds}ms");
```

**Performance Targets**:
- Initial load: < 500ms for 50 messages
- Scroll frame time: < 16ms (60 FPS)
- Memory: < 100MB for 1000 messages
- Incremental load: < 200ms per page

**10. Additional Optimizations**:
- Use ObservableRangeCollection for batch updates to avoid multiple change notifications
- Implement pull-to-refresh efficiently with proper cancellation
- Debounce search/filter operations to reduce UI updates
- Use MainThread.BeginInvokeOnMainThread for cross-thread UI updates

### 16) Platform-Specific UI Strategies

**Decision**: Use **partial classes with Platforms folder structure** for platform-specific implementations while maintaining maximum code sharing through shared XAML and conditional styling.

**Rationale**:
- Clean separation of platform-specific code without conditional compilation clutter
- .NET MAUI multi-targeting automatically compiles only relevant platform code
- Enables platform-specific UI adherence (iOS HIG, Material Design, Fluent Design)
- Maintains single shared XAML where possible with platform-specific styling

**Implementation Pattern**:

**1. Folder Structure**:
```
MotorcycleRAG.MauiApp/
├── Views/
│   ├── ConversationPage.xaml          # Shared XAML
│   └── ConversationPage.xaml.cs       # Shared code-behind
├── Platforms/
│   ├── Android/
│   │   ├── MainActivity.cs
│   │   └── DeviceOrientationService.cs
│   ├── iOS/
│   │   ├── AppDelegate.cs
│   │   └── DeviceOrientationService.cs
│   └── Windows/
│       ├── App.xaml.cs
│       └── DeviceOrientationService.cs
```

**2. Partial Class Pattern for Platform Services**:

**Shared Interface** (outside Platforms folder):
```csharp
namespace MotorcycleRAG.Services
{
    public partial class PlatformService
    {
        public partial void ConfigureAppearance();
        public partial StatusBarStyle GetStatusBarStyle();
    }
}
```

**Platform Implementations**:

**Platforms/iOS/PlatformService.cs**:
```csharp
using UIKit;

namespace MotorcycleRAG.Services
{
    public partial class PlatformService
    {
        public partial void ConfigureAppearance()
        {
            // iOS-specific appearance configuration
            UINavigationBar.Appearance.TintColor = UIColor.SystemBlue;
            UINavigationBar.Appearance.BarTintColor = UIColor.SystemBackground;
        }

        public partial StatusBarStyle GetStatusBarStyle()
        {
            return UIDevice.CurrentDevice.UserInterfaceIdiom == UIUserInterfaceIdiom.Pad
                ? StatusBarStyle.Light
                : StatusBarStyle.Dark;
        }
    }
}
```

**Platforms/Android/PlatformService.cs**:
```csharp
using Android.Views;
using Microsoft.Maui.Controls.Platform;

namespace MotorcycleRAG.Services
{
    public partial class PlatformService
    {
        public partial void ConfigureAppearance()
        {
            // Android-specific Material Design configuration
            var activity = Platform.CurrentActivity;
            var window = activity?.Window;

            window?.SetStatusBarColor(Android.Graphics.Color.Transparent);
            window?.SetNavigationBarColor(Android.Graphics.Color.Transparent);
        }

        public partial StatusBarStyle GetStatusBarStyle()
        {
            return StatusBarStyle.Dark;
        }
    }
}
```

**3. Platform-Specific XAML Styling**:

**Resources/Styles/Styles.xaml**:
```xml
<ResourceDictionary>
    <!-- Shared base styles -->
    <Style x:Key="BaseButtonStyle" TargetType="Button">
        <Setter Property="FontSize" Value="16"/>
        <Setter Property="Padding" Value="12"/>
    </Style>

    <!-- iOS-specific styling -->
    <OnPlatform x:Key="PrimaryButtonStyle" x:TypeArguments="Style">
        <On Platform="iOS">
            <Style TargetType="Button" BasedOn="{StaticResource BaseButtonStyle}">
                <Setter Property="CornerRadius" Value="8"/>
                <Setter Property="BackgroundColor" Value="#007AFF"/> <!-- iOS Blue -->
                <Setter Property="TextColor" Value="White"/>
            </Style>
        </On>
        <On Platform="Android">
            <Style TargetType="Button" BasedOn="{StaticResource BaseButtonStyle}">
                <Setter Property="CornerRadius" Value="4"/>
                <Setter Property="BackgroundColor" Value="#6200EE"/> <!-- Material Purple -->
                <Setter Property="TextColor" Value="White"/>
            </Style>
        </On>
        <On Platform="WinUI">
            <Style TargetType="Button" BasedOn="{StaticResource BaseButtonStyle}">
                <Setter Property="CornerRadius" Value="2"/>
                <Setter Property="BackgroundColor" Value="#0078D4"/> <!-- Fluent Blue -->
                <Setter Property="TextColor" Value="White"/>
            </Style>
        </On>
    </OnPlatform>
</ResourceDictionary>
```

**4. Platform-Specific UI Components**:

Use OnPlatform markup for platform-specific values:
```xml
<ContentPage>
    <StackLayout Padding="{OnPlatform iOS='0,40,0,0', Android='0,0,0,0', WinUI='0,0,0,0'}">
        <!-- iOS: Account for status bar/notch -->
        <!-- Android: Material Design spacing -->
        <!-- Windows: Fluent Design spacing -->

        <Label Text="Conversation"
               FontSize="{OnPlatform iOS=34, Android=24, WinUI=28}"
               FontAttributes="{OnPlatform iOS=Bold, Android=None, WinUI=SemiBold}"/>
    </StackLayout>
</ContentPage>
```

**5. Platform Detection in Code**:
```csharp
if (DeviceInfo.Platform == DevicePlatform.iOS)
{
    // iOS-specific logic
    Shell.Current.FlyoutBehavior = FlyoutBehavior.Disabled; // iOS uses tab-based navigation
}
else if (DeviceInfo.Platform == DevicePlatform.Android)
{
    // Android-specific logic
    Shell.Current.FlyoutBehavior = FlyoutBehavior.Flyout; // Android uses drawer navigation
}
```

**6. Conditional Compilation for Complex Scenarios**:
```csharp
#if IOS
using UIKit;
#elif ANDROID
using Android.Content;
#elif WINDOWS
using Microsoft.UI.Xaml;
#endif

public class PlatformHelper
{
    public static void ShowNativeDialog(string message)
    {
#if IOS
        var alert = UIAlertController.Create("Alert", message, UIAlertControllerStyle.Alert);
        alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
        UIApplication.SharedApplication.KeyWindow.RootViewController.PresentViewController(alert, true, null);
#elif ANDROID
        var builder = new Android.App.AlertDialog.Builder(Platform.CurrentActivity);
        builder.SetMessage(message);
        builder.SetPositiveButton("OK", (s, e) => { });
        builder.Show();
#elif WINDOWS
        var dialog = new Windows.UI.Popups.MessageDialog(message);
        await dialog.ShowAsync();
#endif
    }
}
```

**7. Platform Idiom Detection**:
```csharp
// Detect device type for responsive UI
if (DeviceInfo.Idiom == DeviceIdiom.Phone)
{
    // Mobile phone layout
    MainLayout.Orientation = StackOrientation.Vertical;
}
else if (DeviceInfo.Idiom == DeviceIdiom.Tablet)
{
    // Tablet layout
    MainLayout.Orientation = StackOrientation.Horizontal;
}
else if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
{
    // Desktop layout
    MainLayout.WidthRequest = 1200;
}
```

**8. Platform-Specific Navigation Patterns**:

**iOS (Tab-based)**:
```xml
<Shell>
    <TabBar>
        <ShellContent Title="Chat" Icon="chat.png" ContentTemplate="{DataTemplate views:ConversationPage}"/>
        <ShellContent Title="History" Icon="history.png" ContentTemplate="{DataTemplate views:HistoryPage}"/>
        <ShellContent Title="Settings" Icon="settings.png" ContentTemplate="{DataTemplate views:SettingsPage}"/>
    </TabBar>
</Shell>
```

**Android (Drawer + Bottom Nav)**:
```xml
<Shell FlyoutBehavior="Flyout">
    <FlyoutItem Title="Home" Icon="home.png">
        <ShellContent ContentTemplate="{DataTemplate views:ConversationPage}"/>
    </FlyoutItem>
    <!-- Bottom navigation via TabBar -->
</Shell>
```

**Windows (Sidebar Nav)**:
```xml
<Shell FlyoutBehavior="Locked" FlyoutWidth="280">
    <FlyoutItem Title="Conversations">
        <ShellContent ContentTemplate="{DataTemplate views:ConversationPage}"/>
    </FlyoutItem>
</Shell>
```

**9. Platform-Specific Design System Adherence**:

**iOS Human Interface Guidelines**:
- Use SF Symbols for icons (via FontImageSource with SF Pro font)
- Implement swipe gestures for delete/actions
- Use native modal presentations (Sheet style)
- Respect safe areas (notch, home indicator)

**Material Design (Android)**:
- Use Material Design icons
- Implement FAB (Floating Action Button) for primary actions
- Use bottom sheets for contextual actions
- Respect material motion and elevation

**Fluent Design (Windows)**:
- Use Segoe MDL2 Assets for icons
- Implement command bar for actions
- Use acrylic/reveal effects where appropriate
- Support mouse/keyboard interactions

**10. Code Sharing Metrics Target**:
- Shared code: 95%+ (ViewModels, Services, Models)
- Shared XAML: 85%+ (conditional styling handles platform differences)
- Platform-specific code: < 5% (native integrations, platform services)

**Best Practices Summary**:
1. Use partial classes for platform-specific service implementations
2. Prefer shared XAML with OnPlatform/OnIdiom over duplicate pages
3. Centralize platform detection and configuration
4. Follow each platform's design guidelines for native feel
5. Test on physical devices for accurate platform behavior
6. Use DeviceInfo and DeviceDisplay for responsive layouts
7. Maintain platform-specific resources (icons, splash screens) in respective folders
