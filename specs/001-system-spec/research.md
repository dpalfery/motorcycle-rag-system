# Research: Motorcycle RAG System UI Framework & Architecture

**Date**: 2025-12-31  
**Phase**: 0 (Research)  
**Purpose**: Research findings to inform design and implementation decisions for Material.Components.Maui integration and navigation shell architecture.

## Executive Summary

This document summarizes research findings for standardizing the Motorcycle RAG System UI on **Material.Components.Maui** across all MAUI applications (Admin and MobileApp). Key findings include:

- **Material.Components.Maui** provides comprehensive Material Design 3 components compatible with .NET MAUI 10.0
- **MAUI Shell + Flyout** pattern is the recommended approach for left menu navigation
- **TopAppBar** integration provides consistent header with profile/login area
- **Responsive design** patterns support collapsible flyout on mobile devices
- **Local processing** capabilities are achievable with ONNX Runtime and PDF/CSV libraries
- **Azure AI Foundry** integration patterns are well-established with Semantic Kernel

## 1. Material.Components.Maui Research

### 1.1 Package Information

**Package**: `Material.Components.Maui`  
**Latest Version**: 1.0.0+ (compatible with .NET MAUI 10.0)  
**NuGet**: `https://www.nuget.org/packages/Material.Components.Maui`  
**Repository**: `https://github.com/MaterialComponents/Material.Components.Maui`

### 1.2 Available Components

Material.Components.Maui provides a comprehensive set of Material Design 3 components:

#### Navigation Components
- **Flyout**: Drawer-style navigation menu (left sidebar)
- **TopAppBar**: Application header with title, actions, and profile area
- **NavigationBar**: Bottom navigation bar (alternative to flyout for mobile)
- **TabView**: Tab-based navigation

#### Layout Components
- **Card**: Material card with elevation and ripple effects
- **CardView**: Enhanced card with more customization options
- **Chip**: Compact element representing input, attribute, or action
- **Divider**: Visual separator between content

#### Input Components
- **TextField**: Material text input with floating label
- **TextArea**: Multi-line text input
- **ComboBox**: Dropdown selection
- **CheckBox**: Checkbox with Material styling
- **RadioButton**: Radio button group
- **Switch**: Toggle switch

#### Action Components
- **Button**: Material button variants (Filled, Outlined, Text, Elevated, Tonal)
- **IconButton**: Icon-only button
- **FloatingActionButton**: Prominent action button
- **Menu**: Dropdown menu with items

#### Feedback Components
- **ProgressBar**: Linear progress indicator
- **CircularProgress**: Circular progress indicator
- **Snackbar**: Brief message about an operation
- **Dialog**: Modal dialog with content and actions

#### Display Components
- **Avatar**: User profile image with initials fallback
- **Badge**: Small count or status indicator
- **Icon**: Material Design icons (using Material Icons font)

### 1.3 Installation & Setup

```xml
<!-- Add to .csproj -->
<ItemGroup>
  <PackageReference Include="Material.Components.Maui" Version="1.0.0" />
</ItemGroup>
```

```csharp
// In MauiProgram.cs
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
                fonts.AddFont("MaterialIcons-Regular.ttf", "MaterialIcons");
            });

        return builder.Build();
    }
}
```

### 1.4 Theming & Styling

Material.Components.Maui supports Material Design 3 theming:

```xml
<!-- App.xaml -->
<Application.Resources>
    <ResourceDictionary>
        <!-- Material Design 3 Color Scheme -->
        <Color x:Key="Primary">#512BD4</Color>
        <Color x:Key="OnPrimary">#FFFFFF</Color>
        <Color x:Key="PrimaryContainer">#EADDFF</Color>
        <Color x:Key="OnPrimaryContainer">#21005D</Color>
        <Color x:Key="Secondary">#625B71</Color>
        <Color x:Key="OnSecondary">#FFFFFF</Color>
        <Color x:Key="Tertiary">#7D5260</Color>
        <Color x:Key="OnTertiary">#FFFFFF</Color>
        <Color x:Key="Error">#B3261E</Color>
        <Color x:Key="OnError">#FFFFFF</Color>
        <Color x:Key="Background">#FFFBFE</Color>
        <Color x:Key="OnBackground">#1C1B1F</Color>
        <Color x:Key="Surface">#FFFBFE</Color>
        <Color x:Key="OnSurface">#1C1B1F</Color>
    </ResourceDictionary>
</Application.Resources>
```

### 1.5 Platform-Specific Considerations

**Windows**: Full support with native Windows styling  
**macOS**: Full support with native macOS styling  
**iOS**: Full support with iOS-specific adaptations  
**Android**: Full support with Material Design 3 (Android 12+)  

## 2. Navigation Shell Architecture Research

### 2.1 MAUI Shell + Flyout Pattern

The recommended approach for left menu navigation in MAUI is using **Shell** with **Flyout**:

```xml
<!-- AppShell.xaml -->
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
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

### 2.2 Responsive Design Patterns

#### Desktop (Windows/macOS)
- **Flyout**: Always visible (left sidebar)
- **FlyoutWidth**: 300px (standard)
- **FlyoutBehavior**: `Locked` (cannot be dismissed)

#### Mobile (iOS/Android)
- **Flyout**: Collapsible (hamburger menu)
- **FlyoutWidth**: 250px (narrower)
- **FlyoutBehavior**: `Flyout` (can be dismissed)

#### Responsive Implementation

```csharp
// AppShell.xaml.cs
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

### 2.3 TopAppBar Integration

Material.Components.Maui provides **TopAppBar** for consistent header:

```xml
<!-- Alternative: Using Material TopAppBar -->
<mc:TopAppBar>
    <mc:TopAppBar.Leading>
        <!-- Hamburger menu button (mobile) -->
        <mc:IconButton Icon="menu.png"
                       Command="{Binding ToggleFlyoutCommand}"
                       IsVisible="{Binding IsMobile}"/>
    </mc:TopAppBar.Leading>
    
    <mc:TopAppBar.Title>
        <Label Text="{Binding PageTitle}"
               FontSize="18"
               FontAttributes="Bold"/>
    </mc:TopAppBar.Title>
    
    <mc:TopAppBar.Trailing>
        <!-- Profile/Login Area -->
        <HorizontalStackLayout Spacing="10">
            <mc:Avatar WidthRequest="32"
                       HeightRequest="32"
                       Source="{Binding UserAvatar}"
                       Initials="{Binding UserInitials}"/>
            <mc:IconButton Icon="logout.png"
                           Command="{Binding SignOutCommand}"/>
        </HorizontalStackLayout>
    </mc:TopAppBar.Trailing>
</mc:TopAppBar>
```

### 2.4 Authentication State Management

#### Authentication State Pattern

```csharp
// Services/IAuthenticationState.cs
public interface IAuthenticationState
{
    bool IsAuthenticated { get; }
    string? UserId { get; }
    string? UserName { get; }
    string? UserEmail { get; }
    string? UserAvatar { get; }
    string? UserInitials { get; }
    event EventHandler? AuthenticationChanged;
}

// Services/AuthenticationState.cs
public class AuthenticationState : IAuthenticationState
{
    private UserProfile? _userProfile;
    
    public bool IsAuthenticated => _userProfile != null;
    public string? UserId => _userProfile?.Id;
    public string? UserName => _userProfile?.Name;
    public string? UserEmail => _userProfile?.Email;
    public string? UserAvatar => _userProfile?.AvatarUrl;
    
    public string? UserInitials
    {
        get
        {
            if (string.IsNullOrEmpty(UserName))
                return "?";
            
            var parts = UserName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[1][0]}";
            return parts[0][0].ToString().ToUpper();
        }
    }
    
    public event EventHandler? AuthenticationChanged;
    
    public void SetUser(UserProfile? profile)
    {
        _userProfile = profile;
        AuthenticationChanged?.Invoke(this, EventArgs.Empty);
    }
    
    public void ClearUser()
    {
        _userProfile = null;
        AuthenticationChanged?.Invoke(this, EventArgs.Empty);
    }
}
```

#### Dependency Injection Setup

```csharp
// MauiProgram.cs
builder.Services.AddSingleton<IAuthenticationState, AuthenticationState>();
builder.Services.AddSingleton<IAdminAuthService, AdminAuthService>();
builder.Services.AddTransient<AuthenticationViewModel>();
```

## 3. Local Processing Research

### 3.1 ONNX Runtime for Embeddings

**Package**: `Microsoft.ML.OnnxRuntime`  
**Version**: 1.20.1 (compatible with .NET 10.0)  
**Purpose**: Local embedding generation without cloud API calls

```csharp
// Processing/OnnxEmbeddingService.cs
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

public class OnnxEmbeddingService : IEmbeddingService
{
    private readonly InferenceSession _session;
    
    public OnnxEmbeddingService()
    {
        // Load ONNX model from app resources
        var modelPath = FileSystem.OpenAppPackageFileAsync("embedding-model.onnx")
            .GetAwaiter().GetResult();
        _session = new InferenceSession(modelPath);
    }
    
    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        // Tokenize text (simplified - use proper tokenizer)
        var tokens = Tokenize(text);
        
        // Create input tensor
        var input = new DenseTensor<long>(tokens, new[] { 1, tokens.Length });
        
        // Run inference
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", input)
        };
        
        using var results = await Task.Run(() => _session.Run(inputs));
        var output = results.First().AsEnumerable<float>().ToArray();
        
        return output;
    }
}
```

### 3.2 PDF Processing

**Package**: `PdfPig`  
**Version**: 0.1.9  
**Purpose**: PDF text extraction with page/section metadata

```csharp
// Processing/PdfChunker.cs
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

public class PdfChunker
{
    public async Task<List<DocumentChunk>> ChunkPdfAsync(string filePath, int chunkSize = 500)
    {
        var chunks = new List<DocumentChunk>();
        
        using var pdfDocument = PdfDocument.Open(filePath);
        
        foreach (var page in pdfDocument.GetPages())
        {
            var text = page.Text;
            var sentences = text.Split(new[] { '.', '!', '?' }, 
                StringSplitOptions.RemoveEmptyEntries);
            
            var currentChunk = new StringBuilder();
            var chunkIndex = 0;
            
            foreach (var sentence in sentences)
            {
                currentChunk.Append(sentence.Trim()).Append(". ");
                
                if (currentChunk.Length >= chunkSize)
                {
                    chunks.Add(new DocumentChunk
                    {
                        Content = currentChunk.ToString().Trim(),
                        PageNumber = page.Number,
                        ChunkIndex = chunkIndex++,
                        SourceType = DocumentSourceType.PDF,
                        SourcePath = filePath
                    });
                    
                    currentChunk.Clear();
                }
            }
            
            // Add remaining text
            if (currentChunk.Length > 0)
            {
                chunks.Add(new DocumentChunk
                {
                    Content = currentChunk.ToString().Trim(),
                    PageNumber = page.Number,
                    ChunkIndex = chunkIndex,
                    SourceType = DocumentSourceType.PDF,
                    SourcePath = filePath
                });
            }
        }
        
        return chunks;
    }
}
```

### 3.3 CSV Processing

**Package**: `CsvHelper`  
**Version**: 33.1.0  
**Purpose**: CSV parsing and validation

```csharp
// Processing/CsvChunker.cs
using CsvHelper;
using CsvHelper.Configuration;

public class CsvChunker
{
    public async Task<List<DocumentChunk>> ChunkCsvAsync<T>(string filePath, 
        int chunkSize = 10) where T : class
    {
        var chunks = new List<DocumentChunk>();
        
        using var reader = new StreamReader(filePath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        
        var records = csv.GetRecords<T>().ToList();
        var chunkIndex = 0;
        
        for (int i = 0; i < records.Count; i += chunkSize)
        {
            var batch = records.Skip(i).Take(chunkSize).ToList();
            
            chunks.Add(new DocumentChunk
            {
                Content = SerializeRecords(batch),
                ChunkIndex = chunkIndex++,
                SourceType = DocumentSourceType.CSV,
                SourcePath = filePath,
                RecordCount = batch.Count
            });
        }
        
        return chunks;
    }
    
    private string SerializeRecords<T>(IEnumerable<T> records)
    {
        // Serialize records to JSON or structured text
        return JsonSerializer.Serialize(records);
    }
}
```

## 4. Azure AI Foundry Integration Research

### 4.1 Azure OpenAI Service Wrapper

**Package**: `Azure.AI.OpenAI`  
**Version**: 2.1.0 (compatible with .NET 10.0)

```csharp
// Azure/AzureOpenAIClientWrapper.cs
using Azure.AI.OpenAI;
using Azure.Identity;

public class AzureOpenAIClientWrapper : IAzureOpenAIClient
{
    private readonly OpenAIClient _client;
    private readonly string _deploymentName;
    
    public AzureOpenAIClientWrapper(string endpoint, string deploymentName)
    {
        _client = new OpenAIClient(
            new Uri(endpoint),
            new DefaultAzureCredential());
        _deploymentName = deploymentName;
    }
    
    public async Task<string> CompleteChatAsync(List<ChatMessage> messages, 
        CancellationToken cancellationToken = default)
    {
        var chatCompletionsOptions = new ChatCompletionsOptions
        {
            DeploymentName = _deploymentName,
            Messages = messages.Select(m => new ChatRequestMessage
            {
                Role = m.Role.ToString(),
                Content = m.Content
            }).ToList()
        };
        
        var response = await _client.GetChatCompletionsAsync(
            chatCompletionsOptions, cancellationToken);
        
        return response.Value.Choices[0].Message.Content;
    }
}
```

### 4.2 Azure AI Search Hybrid Search

**Package**: `Azure.Search.Documents`  
**Version**: 11.6.0

```csharp
// Azure/AzureSearchClientWrapper.cs
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

public class AzureSearchClientWrapper : ISearchClient
{
    private readonly SearchClient _client;
    
    public AzureSearchClientWrapper(string endpoint, string indexName, 
        string apiKey)
    {
        _client = new SearchClient(
            new Uri(endpoint),
            indexName,
            new AzureKeyCredential(apiKey));
    }
    
    public async Task<SearchResult> HybridSearchAsync(string query, 
        CancellationToken cancellationToken = default)
    {
        var searchOptions = new SearchOptions
        {
            VectorSearch = new()
            {
                Queries = { new VectorizedQuery(query) { KNearestNeighborsCount = 10 } }
            },
            SemanticSearch = new()
            {
                SemanticConfigurationName = "default",
                QueryCaption = new(QueryCaptionType.Extractive),
                QueryAnswer = new(QueryAnswerType.Extractive)
            }
        };
        
        var response = await _client.SearchAsync<SearchDocument>(
            query, searchOptions, cancellationToken);
        
        return MapToSearchResult(response.Value);
    }
}
```

### 4.3 Semantic Kernel Agent Orchestration

**Package**: `Microsoft.SemanticKernel`  
**Version**: 1.0.0

```csharp
// Services/AgentOrchestrator.cs
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;

public class AgentOrchestrator
{
    private readonly Kernel _kernel;
    private readonly QueryPlannerAgent _queryPlanner;
    private readonly VectorSearchAgent _vectorSearch;
    private readonly WebSearchAgent _webSearch;
    
    public AgentOrchestrator(Kernel kernel, 
        QueryPlannerAgent queryPlanner,
        VectorSearchAgent vectorSearch,
        WebSearchAgent webSearch)
    {
        _kernel = kernel;
        _queryPlanner = queryPlanner;
        _vectorSearch = vectorSearch;
        _webSearch = webSearch;
    }
    
    public async Task<QueryResponse> ExecuteQueryAsync(string query, 
        CancellationToken cancellationToken = default)
    {
        // Step 1: Plan search strategy
        var plan = await _queryPlanner.PlanAsync(query, cancellationToken);
        
        var results = new List<SearchResult>();
        
        // Step 2: Execute vector search
        if (plan.UseVectorSearch)
        {
            var vectorResults = await _vectorSearch.SearchAsync(
                plan.VectorQuery, cancellationToken);
            results.AddRange(vectorResults);
        }
        
        // Step 3: Augment with web search if needed
        if (plan.UseWebSearch && results.Count < plan.MinimumResults)
        {
            var webResults = await _webSearch.SearchAsync(
                plan.WebQuery, cancellationToken);
            results.AddRange(webResults);
        }
        
        // Step 4: Generate answer with citations
        var answer = await GenerateAnswerAsync(query, results, cancellationToken);
        
        return new QueryResponse
        {
            Answer = answer.Answer,
            Sources = results,
            QueryId = plan.QueryId,
            Metrics = answer.Metrics
        };
    }
}
```

## 5. Authentication & Authorization Research

### 5.1 Microsoft Entra External ID / B2C

**Package**: `Microsoft.Identity.Client` (MSAL)  
**Version**: 4.69.1

```csharp
// Services/AdminAuthService.cs
using Microsoft.Identity.Client;

public class AdminAuthService : IAdminAuthService
{
    private readonly IPublicClientApplication _msalClient;
    
    public AdminAuthService(string clientId, string tenantId, 
        string[] scopes)
    {
        _msalClient = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
            .WithRedirectUri("http://localhost")
            .Build();
    }
    
    public async Task<AuthenticationResult> SignInAsync()
    {
        var accounts = await _msalClient.GetAccountsAsync();
        
        try
        {
            return await _msalClient
                .AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                .ExecuteAsync();
        }
        catch (MsalUiRequiredException)
        {
            return await _msalClient
                .AcquireTokenInteractive(scopes)
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync();
        }
    }
    
    public async Task SignOutAsync()
    {
        var accounts = await _msalClient.GetAccountsAsync();
        foreach (var account in accounts)
        {
            await _msalClient.RemoveAsync(account);
        }
    }
}
```

### 5.2 Token Management

```csharp
// Services/TokenService.cs
public class TokenService
{
    private readonly IAdminAuthService _authService;
    private string? _accessToken;
    private DateTime _tokenExpiry;
    
    public async Task<string> GetAccessTokenAsync()
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry)
        {
            return _accessToken;
        }
        
        var result = await _authService.SignInAsync();
        _accessToken = result.AccessToken;
        _tokenExpiry = result.ExpiresOn;
        
        return _accessToken;
    }
    
    public void ClearToken()
    {
        _accessToken = null;
        _tokenExpiry = DateTime.MinValue;
    }
}
```

## 6. Multi-Agent RAG Architecture Research

### 6.1 Claim Extraction & Verification

```csharp
// Services/ModelValidationService.cs
public class ModelValidationService
{
    public async Task<List<Claim>> ExtractClaimsAsync(
        string answer, List<SearchResult> evidence)
    {
        // Use GPT-4o-mini to extract discrete claims
        var prompt = $@"
Extract discrete factual claims from the following answer.
For each claim, identify the supporting evidence from the provided sources.

Answer: {answer}

Evidence:
{string.Join("\n", evidence.Select(e => $"- {e.Content} (Source: {e.SourceUrl})"))}

Return claims in JSON format:
{{
  ""claims"": [
    {{
      ""text"": ""claim text"",
      ""supportingEvidence"": [""evidence_id1"", ""evidence_id2""],
      ""confidence"": 0.95
    }}
  ]
}}";
        
        var response = await _llmService.CompleteAsync(prompt);
        return JsonSerializer.Deserialize<ClaimExtraction>(response).Claims;
    }
    
    public async Task<VerificationResult> VerifyClaimAsync(
        Claim claim, List<SearchResult> evidence)
    {
        // Use GPT-4o to verify claim against evidence
        var prompt = $@"
Verify if the following claim is supported by the provided evidence.

Claim: {claim.Text}

Supporting Evidence:
{string.Join("\n", claim.SupportingEvidence.Select(id => 
    evidence.First(e => e.Id == id).Content))}

Return verification result:
- Supported: Evidence directly confirms the claim
- Unsupported: Evidence does not confirm the claim
- Conflicting: Evidence contradicts the claim
- Insufficient: Not enough evidence to verify

Return in JSON format:
{{
  ""status"": ""Supported"",
  ""confidence"": 0.9,
  ""reasoning"": ""explanation""
}}";
        
        var response = await _llmService.CompleteAsync(prompt);
        return JsonSerializer.Deserialize<VerificationResult>(response);
    }
}
```

### 6.2 Citation Generation

```csharp
// Services/CitationService.cs
public class CitationService
{
    public List<Citation> GenerateCitations(
        List<SearchResult> results)
    {
        var citations = new List<Citation>();
        
        foreach (var result in results)
        {
            var citation = new Citation
            {
                Id = result.Id,
                Content = result.Content,
                SourceUrl = result.SourceUrl,
                SourceName = result.SourceName,
                RelevanceScore = result.RelevanceScore,
                Metadata = new CitationMetadata
                {
                    // Manual/PDF sources
                    MotorcycleMake = result.Metadata?.GetValueOrDefault("make"),
                    MotorcycleModel = result.Metadata?.GetValueOrDefault("model"),
                    MotorcycleYear = result.Metadata?.GetValueOrDefault("year"),
                    ManualIdentifier = result.Metadata?.GetValueOrDefault("manual_id"),
                    SectionHeading = result.Metadata?.GetValueOrDefault("section"),
                    PageNumber = result.Metadata?.GetValueOrDefault("page"),
                    
                    // Web sources
                    PageTitle = result.Metadata?.GetValueOrDefault("title"),
                    RetrievalDate = DateTime.UtcNow,
                    
                    // Dataset sources
                    DatasetName = result.Metadata?.GetValueOrDefault("dataset"),
                    DatasetVersion = result.Metadata?.GetValueOrDefault("version"),
                    RecordKey = result.Metadata?.GetValueOrDefault("record_id")
                }
            };
            
            citations.Add(citation);
        }
        
        return citations;
    }
}
```

## 7. Resilience Patterns Research

### 7.1 Polly Retry Policies

```csharp
// Resilience/ResilienceService.cs
using Polly;
using Polly.Retry;

public class ResilienceService
{
    private readonly IAsyncPolicy _retryPolicy;
    private readonly IAsyncPolicy _circuitBreakerPolicy;
    
    public ResilienceService()
    {
        // Exponential backoff retry policy
        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>()
            .Or<Azure.RequestFailedException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => 
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timeSpan, retryCount, context) =>
                {
                    Console.WriteLine(
                        $"Retry {retryCount} after {timeSpan.TotalSeconds}s due to: {outcome.Exception?.Message}");
                });
        
        // Circuit breaker policy
        _circuitBreakerPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<Azure.RequestFailedException>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromMinutes(1),
                onBreak: (exception, breakDelay) =>
                {
                    Console.WriteLine($"Circuit broken for {breakDelay.TotalMinutes}m");
                },
                onReset: () =>
                {
                    Console.WriteLine("Circuit reset");
                });
    }
    
    public async Task<T> ExecuteWithResilienceAsync<T>(
        Func<Task<T>> action)
    {
        return await _retryPolicy
            .WrapAsync(_circuitBreakerPolicy)
            .ExecuteAsync(action);
    }
}
```

## 8. Observability Research

### 8.1 Application Insights Integration

**Package**: `Microsoft.ApplicationInsights.AspNetCore`  
**Version**: 2.22.0

```csharp
// Telemetry/TelemetryService.cs
using Microsoft.ApplicationInsights;

public class TelemetryService
{
    private readonly TelemetryClient _telemetryClient;
    
    public TelemetryService(TelemetryClient telemetryClient)
    {
        _telemetryClient = telemetryClient;
    }
    
    public void TrackQuery(string queryId, string userId, 
        double duration, bool degradedMode)
    {
        var properties = new Dictionary<string, string>
        {
            ["QueryId"] = queryId,
            ["UserId"] = userId,
            ["DegradedMode"] = degradedMode.ToString()
        };
        
        var metrics = new Dictionary<string, double>
        {
            ["Duration"] = duration
        };
        
        _telemetryClient.TrackEvent("QueryExecuted", properties, metrics);
    }
    
    public void TrackError(Exception exception, 
        Dictionary<string, string>? properties = null)
    {
        _telemetryClient.TrackException(exception, properties);
    }
}
```

## 9. Key Findings & Recommendations

### 9.1 Material.Components.Maui Adoption

**Recommendation**: Adopt `Material.Components.Maui` for both Admin and MobileApp MAUI applications.

**Rationale**:
- Provides comprehensive Material Design 3 components
- Fully compatible with .NET MAUI 10.0
- Consistent theming and styling across platforms
- Well-maintained and documented
- Reduces custom UI development effort

**Implementation Steps**:
1. Add `Material.Components.Maui` NuGet package to both projects
2. Initialize Material Components in `MauiProgram.cs`
3. Define Material Design 3 color scheme in `App.xaml`
4. Migrate existing pages to use Material components
5. Implement unified navigation shell (Flyout + TopAppBar)
6. Establish responsive design patterns

### 9.2 Navigation Shell Architecture

**Recommendation**: Use MAUI Shell with Flyout for left menu navigation and TopAppBar for header with profile/login.

**Rationale**:
- MAUI Shell provides built-in navigation infrastructure
- Flyout pattern is standard for desktop admin applications
- TopAppBar provides consistent header across all pages
- Responsive design adapts to desktop vs mobile
- Material.Components.Maui integrates seamlessly with Shell

**Implementation Pattern**:
- **Desktop**: Locked flyout (always visible), 300px width
- **Mobile**: Collapsible flyout (hamburger menu), 250px width
- **Header**: TopAppBar with page title (left) and profile/login (right)
- **Profile**: Avatar with initials fallback, user name, sign-out button

### 9.3 Local Processing Capabilities

**Recommendation**: Implement local chunking and vectorization using ONNX Runtime and PDF/CSV libraries.

**Rationale**:
- Reduces cloud API costs for document processing
- Improves privacy (documents processed locally)
- Faster processing for large documents
- Offline capability for document preparation

**Technology Stack**:
- **ONNX Runtime**: `Microsoft.ML.OnnxRuntime` (v1.20.1)
- **PDF Processing**: `PdfPig` (v0.1.9)
- **CSV Processing**: `CsvHelper` (v33.1.0)
- **Embedding Model**: text-embedding-3-large (ONNX format)

### 9.4 Azure AI Foundry Integration

**Recommendation**: Use Azure AI Foundry services with Semantic Kernel for agent orchestration.

**Rationale**:
- Unified AI service platform
- Scalable and managed infrastructure
- Built-in observability and monitoring
- Semantic Kernel provides agent orchestration framework

**Services**:
- **Azure OpenAI**: GPT-4o (planning), GPT-4o-mini (completion), text-embedding-3-large
- **Azure AI Search**: Hybrid vector/keyword search with semantic ranking
- **Azure Document Intelligence**: PDF OCR and document understanding
- **Semantic Kernel**: Agent orchestration and prompt engineering

### 9.5 Authentication & Authorization

**Recommendation**: Use Microsoft Entra External ID / B2C for users and Entra ID for admins.

**Rationale**:
- Enterprise-grade identity provider
- Social sign-in support (Google, GitHub, Microsoft, Facebook)
- App roles for authorization (Admin, Operator, Viewer)
- MSAL provides seamless MAUI integration

**Implementation**:
- **Users**: Entra External ID / B2C (social sign-in)
- **Admins**: Entra ID (workforce authentication)
- **Authorization**: Entra app roles in token claims
- **MAUI Integration**: MSAL.NET library

## 10. Unresolved Clarifications

**None** - All research areas have been addressed with concrete recommendations.

## 11. References

- Material.Components.Maui: https://github.com/MaterialComponents/Material.Components.Maui
- .NET MAUI Documentation: https://learn.microsoft.com/dotnet/maui/
- Azure AI Foundry: https://azure.microsoft.com/products/ai-foundry
- Semantic Kernel: https://learn.microsoft.com/semantic-kernel/
- MSAL.NET: https://learn.microsoft.com/entra/msal/dotnet/
- Polly: https://github.com/App-vNext/Polly
- ONNX Runtime: https://onnxruntime.ai/
- PdfPig: https://github.com/UglyToad/PdfPig
- CsvHelper: https://github.com/JoshClose/CsvHelper
