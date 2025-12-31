# Data Model: Motorcycle RAG System

**Date**: 2025-12-31  
**Phase**: 1 (Design)  
**Purpose**: Define data models for domain entities, UI view models, and API contracts.

## Executive Summary

This document defines the complete data model for the Motorcycle RAG System, including:

- **Domain Entities**: Core business entities with behavior (User, Document, IngestionJob, etc.)
- **Value Objects**: Immutable types without identity (SearchQuery, Embedding, etc.)
- **DTOs**: Data transfer objects for API contracts
- **UI View Models**: MAUI-specific models for Admin and MobileApp
- **Navigation Models**: Shell navigation and routing models

## 1. Domain Entities

### 1.1 User Entity

```csharp
namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Aggregate root for user accounts
/// </summary>
public class User
{
    public string Id { get; private set; }
    public string Email { get; private set; }
    public string DisplayName { get; private set; }
    public string FirstName { get; private set; }
    public string LastName { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTime CreatedDate { get; private set; }
    public DateTime? LastUpdatedDate { get; private set; }
    public string PlanId { get; private set; }
    public string AuthProvider { get; private set; }
    public string ProviderUserId { get; private set; }
    
    // Navigation properties
    public UserPlan Plan { get; private set; }
    public ICollection<Usage> UsageRecords { get; private set; }
    
    // Domain behavior
    public void UpdateProfile(string displayName, string firstName, string lastName)
    {
        DisplayName = displayName;
        FirstName = firstName;
        LastName = lastName;
        LastUpdatedDate = DateTime.UtcNow;
    }
    
    public void ChangePlan(string newPlanId)
    {
        PlanId = newPlanId;
        LastUpdatedDate = DateTime.UtcNow;
    }
    
    public void Disable()
    {
        IsEnabled = false;
        LastUpdatedDate = DateTime.UtcNow;
    }
    
    public void Enable()
    {
        IsEnabled = true;
        LastUpdatedDate = DateTime.UtcNow;
    }
}
```

### 1.2 UserPlan Entity

```csharp
namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Subscription plan definition
/// </summary>
public class UserPlan
{
    public string Id { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public int DailyRequestLimit { get; private set; }
    public bool IsPaid { get; private set; }
    public DateTime CreatedDate { get; private set; }
    
    // Navigation properties
    public ICollection<User> Users { get; private set; }
    
    // Domain behavior
    public bool IsWithinLimit(int dailyUsage)
    {
        return DailyRequestLimit == -1 || dailyUsage < DailyRequestLimit;
    }
}
```

### 1.3 Usage Entity

```csharp
namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Per-user, per-day usage tracking
/// </summary>
public class Usage
{
    public string Id { get; private set; }
    public string UserId { get; private set; }
    public DateTime Date { get; private set; }
    public int RequestCount { get; private set; }
    public DateTime CreatedDate { get; private set; }
    
    // Navigation properties
    public User User { get; private set; }
    
    // Domain behavior
    public void IncrementRequestCount()
    {
        RequestCount++;
    }
    
    public bool IsLimitExceeded(int dailyLimit)
    {
        return dailyLimit != -1 && RequestCount >= dailyLimit;
    }
}
```

### 1.4 IngestionJob Entity

```csharp
namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Document ingestion job tracking
/// </summary>
public class IngestionJob
{
    public string Id { get; private set; }
    public string JobType { get; private set; }
    public string Status { get; private set; }
    public DateTime CreatedDate { get; private set; }
    public DateTime? StartedDate { get; private set; }
    public DateTime? CompletedDate { get; private set; }
    public int TotalFiles { get; private set; }
    public int ProcessedFiles { get; private set; }
    public int FailedFiles { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? ExecutionId { get; private set; }
    
    // Navigation properties
    public ICollection<IngestionJobFile> Files { get; private set; }
    
    // Domain behavior
    public void Start()
    {
        Status = "Running";
        StartedDate = DateTime.UtcNow;
    }
    
    public void Complete(int processed, int failed)
    {
        Status = "Completed";
        ProcessedFiles = processed;
        FailedFiles = failed;
        CompletedDate = DateTime.UtcNow;
    }
    
    public void Fail(string errorMessage)
    {
        Status = "Failed";
        ErrorMessage = errorMessage;
        CompletedDate = DateTime.UtcNow;
    }
    
    public void Cancel()
    {
        Status = "Cancelled";
        CompletedDate = DateTime.UtcNow;
    }
    
    public bool CanCancel => Status == "Running" || Status == "Pending";
    
    public double ProgressPercentage => TotalFiles == 0 ? 0 : 
        (double)ProcessedFiles / TotalFiles * 100;
}
```

### 1.5 WebSource Entity

```csharp
namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Approved website for scraping and indexing
/// </summary>
public class WebSource
{
    public string Id { get; private set; }
    public string Url { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string TrustTier { get; private set; } // "A", "B", or "C"
    public bool IsEnabled { get; private set; }
    public DateTime CreatedDate { get; private set; }
    public DateTime? LastIndexedDate { get; private set; }
    
    // Domain behavior
    public void Enable()
    {
        IsEnabled = true;
    }
    
    public void Disable()
    {
        IsEnabled = false;
    }
    
    public void UpdateIndexDate()
    {
        LastIndexedDate = DateTime.UtcNow;
    }
}
```

### 1.6 MotorcycleDocument Entity

```csharp
namespace MotorcycleRAG.Domain.Entities;

/// <summary>
/// Indexed motorcycle document (specification, manual, or web content)
/// </summary>
public class MotorcycleDocument
{
    public string Id { get; private set; }
    public string DocumentType { get; private set; } // "Specification", "Manual", "Web"
    public string Title { get; private set; }
    public string Content { get; private set; }
    public float[]? Embedding { get; private set; }
    public DateTime CreatedDate { get; private set; }
    public DateTime? LastUpdatedDate { get; private set; }
    
    // Specification-specific fields
    public string? MotorcycleMake { get; private set; }
    public string? MotorcycleModel { get; private set; }
    public int? MotorcycleYear { get; private set; }
    
    // Manual-specific fields
    public string? ManualIdentifier { get; private set; }
    public string? SectionHeading { get; private set; }
    public int? PageNumber { get; private set; }
    
    // Web-specific fields
    public string? SourceUrl { get; private set; }
    public string? PageTitle { get; private set; }
    public DateTime? RetrievalDate { get; private set; }
    
    // Dataset-specific fields
    public string? DatasetName { get; private set; }
    public string? DatasetVersion { get; private set; }
    public string? RecordKey { get; private set; }
}
```

## 2. Value Objects

### 2.1 SearchQuery

```csharp
namespace MotorcycleRAG.Domain.ValueObjects;

/// <summary>
/// Immutable search query with preferences
/// </summary>
public record SearchQuery
{
    public string QueryText { get; init; }
    public SearchPreferences Preferences { get; init; }
    public string? UserId { get; init; }
    public QueryContext Context { get; init; }
    
    public SearchQuery(string queryText, SearchPreferences preferences, 
        string? userId = null, QueryContext? context = null)
    {
        QueryText = queryText ?? throw new ArgumentNullException(nameof(queryText));
        Preferences = preferences ?? new();
        UserId = userId;
        Context = context ?? new();
    }
}
```

### 2.2 Embedding

```csharp
namespace MotorcycleRAG.Domain.ValueObjects;

/// <summary>
/// Immutable vector embedding
/// </summary>
public record Embedding
{
    public float[] Vector { get; init; }
    public int Dimension => Vector.Length;
    public string ModelName { get; init; }
    
    public Embedding(float[] vector, string modelName = "text-embedding-3-large")
    {
        Vector = vector ?? throw new ArgumentNullException(nameof(vector));
        ModelName = modelName;
    }
    
    public double CosineSimilarity(Embedding other)
    {
        if (Dimension != other.Dimension)
            throw new ArgumentException("Embeddings must have same dimension");
        
        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;
        
        for (int i = 0; i < Dimension; i++)
        {
            dotProduct += Vector[i] * other.Vector[i];
            magnitudeA += Vector[i] * Vector[i];
            magnitudeB += other.Vector[i] * other.Vector[i];
        }
        
        return dotProduct / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }
}
```

### 2.3 Citation

```csharp
namespace MotorcycleRAG.Domain.ValueObjects;

/// <summary>
/// Immutable citation with source attribution
/// </summary>
public record Citation
{
    public string Id { get; init; }
    public string Content { get; init; }
    public double RelevanceScore { get; init; }
    public CitationMetadata Metadata { get; init; }
    
    public Citation(string id, string content, double relevanceScore, 
        CitationMetadata metadata)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        RelevanceScore = relevanceScore;
        Metadata = metadata ?? new();
    }
}

/// <summary>
/// Citation metadata for source attribution
/// </summary>
public record CitationMetadata
{
    // Manual/PDF sources
    public string? MotorcycleMake { get; init; }
    public string? MotorcycleModel { get; init; }
    public int? MotorcycleYear { get; init; }
    public string? ManualIdentifier { get; init; }
    public string? SectionHeading { get; init; }
    public int? PageNumber { get; init; }
    
    // Web sources
    public string? SourceUrl { get; init; }
    public string? PageTitle { get; init; }
    public DateTime? RetrievalDate { get; init; }
    
    // Dataset sources
    public string? DatasetName { get; init; }
    public string? DatasetVersion { get; init; }
    public string? RecordKey { get; init; }
}
```

## 3. UI View Models (MAUI)

### 3.1 Admin Application View Models

#### 3.1.1 AuthenticationViewModel

```csharp
namespace MotorcycleRAG.Admin.ViewModels;

public class AuthenticationViewModel : ObservableObject
{
    private readonly IAdminAuthService _authService;
    private readonly IAuthenticationState _authState;
    
    private bool _isBusy;
    private string? _errorMessage;
    
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }
    
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }
    
    public bool IsAuthenticated => _authState.IsAuthenticated;
    
    public ICommand SignInCommand { get; }
    
    public AuthenticationViewModel(IAdminAuthService authService, 
        IAuthenticationState authState)
    {
        _authService = authService;
        _authState = authState;
        SignInCommand = new Command(async () => await SignInAsync());
        
        _authState.AuthenticationChanged += (s, e) => OnPropertyChanged(nameof(IsAuthenticated));
    }
    
    private async Task SignInAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            
            var result = await _authService.SignInAsync();
            
            var userProfile = new UserProfile
            {
                Id = result.UniqueId,
                Name = result.Account?.Username ?? "Admin",
                Email = result.Account?.Username ?? string.Empty
            };
            
            _authState.SetUser(userProfile);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Sign in failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
```

#### 3.1.2 DashboardViewModel

```csharp
namespace MotorcycleRAG.Admin.ViewModels;

public class DashboardViewModel : ObservableObject
{
    private readonly IApiClient _apiClient;
    
    private int _totalDocuments;
    private int _totalQueries;
    private int _activeJobs;
    private int _webSources;
    
    public int TotalDocuments
    {
        get => _totalDocuments;
        set => SetProperty(ref _totalDocuments, value);
    }
    
    public int TotalQueries
    {
        get => _totalQueries;
        set => SetProperty(ref _totalQueries, value);
    }
    
    public int ActiveJobs
    {
        get => _activeJobs;
        set => SetProperty(ref _activeJobs, value);
    }
    
    public int WebSources
    {
        get => _webSources;
        set => SetProperty(ref _webSources, value);
    }
    
    public ICommand RefreshCommand { get; }
    
    public DashboardViewModel(IApiClient apiClient)
    {
        _apiClient = apiClient;
        RefreshCommand = new Command(async () => await LoadDashboardAsync());
        
        LoadDashboardAsync();
    }
    
    private async Task LoadDashboardAsync()
    {
        try
        {
            var metrics = await _apiClient.GetDashboardMetricsAsync();
            TotalDocuments = metrics.TotalDocuments;
            TotalQueries = metrics.TotalQueries;
            ActiveJobs = metrics.ActiveJobs;
            WebSources = metrics.WebSources;
        }
        catch (Exception ex)
        {
            // Handle error
        }
    }
}
```

#### 3.1.3 UploadViewModel

```csharp
namespace MotorcycleRAG.Admin.ViewModels;

public class UploadViewModel : ObservableObject
{
    private readonly IApiClient _apiClient;
    private readonly IFilePicker _filePicker;
    
    private bool _isBusy;
    private string? _selectedFilePath;
    private string _uploadStatus;
    private double _uploadProgress;
    
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }
    
    public string? SelectedFilePath
    {
        get => _selectedFilePath;
        set => SetProperty(ref _selectedFilePath, value);
    }
    
    public string UploadStatus
    {
        get => _uploadStatus;
        set => SetProperty(ref _uploadStatus, value);
    }
    
    public double UploadProgress
    {
        get => _uploadProgress;
        set => SetProperty(ref _uploadProgress, value);
    }
    
    public ICommand SelectFileCommand { get; }
    public ICommand UploadCommand { get; }
    public ICommand ProcessLocallyCommand { get; }
    
    public UploadViewModel(IApiClient apiClient, IFilePicker filePicker)
    {
        _apiClient = apiClient;
        _filePicker = filePicker;
        SelectFileCommand = new Command(async () => await SelectFileAsync());
        UploadCommand = new Command(async () => await UploadAsync(), 
            () => !string.IsNullOrEmpty(SelectedFilePath) && !IsBusy);
        ProcessLocallyCommand = new Command(async () => await ProcessLocallyAsync(), 
            () => !string.IsNullOrEmpty(SelectedFilePath) && !IsBusy);
    }
    
    private async Task SelectFileAsync()
    {
        var result = await _filePicker.PickAsync(new PickOptions
        {
            FileTypes = new FilePickerFileType(
                new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.WinUI] = new[] { ".pdf", ".csv" },
                    [DevicePlatform.MacCatalyst] = new[] { "pdf", "csv" }
                })
        });
        
        if (result != null)
        {
            SelectedFilePath = result.FullPath;
            UploadStatus = "File selected. Ready to upload.";
        }
    }
    
    private async Task UploadAsync()
    {
        try
        {
            IsBusy = true;
            UploadStatus = "Uploading...";
            UploadProgress = 0;
            
            var result = await _apiClient.UploadFileAsync(SelectedFilePath!, 
                progress => UploadProgress = progress);
            
            UploadStatus = result.Success ? 
                $"Upload complete. Job ID: {result.JobId}" : 
                $"Upload failed: {result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            UploadStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
    
    private async Task ProcessLocallyAsync()
    {
        try
        {
            IsBusy = true;
            UploadStatus = "Processing locally...";
            UploadProgress = 0;
            
            // Local processing logic (chunking, vectorization)
            var extension = Path.GetExtension(SelectedFilePath).ToLower();
            
            List<DocumentChunk> chunks;
            if (extension == ".pdf")
            {
                chunks = await new PdfChunker().ChunkPdfAsync(SelectedFilePath!);
            }
            else if (extension == ".csv")
            {
                chunks = await new CsvChunker().ChunkCsvAsync<MotorcycleSpecification>(SelectedFilePath!);
            }
            else
            {
                throw new NotSupportedException($"File type {extension} not supported");
            }
            
            // Generate embeddings locally
            var embeddingService = new OnnxEmbeddingService();
            foreach (var chunk in chunks)
            {
                chunk.Embedding = await embeddingService.GenerateEmbeddingAsync(chunk.Content);
                UploadProgress = (double)chunks.IndexOf(chunk) / chunks.Count * 100;
            }
            
            // Upload processed artifacts
            var result = await _apiClient.UploadProcessedArtifactsAsync(chunks);
            UploadStatus = result.Success ? 
                $"Processing complete. Job ID: {result.JobId}" : 
                $"Processing failed: {result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            UploadStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
```

#### 3.1.4 JobsViewModel

```csharp
namespace MotorcycleRAG.Admin.ViewModels;

public class JobsViewModel : ObservableObject
{
    private readonly IApiClient _apiClient;
    
    private ObservableCollection<IngestionJobItem> _jobs;
    private bool _isLoading;
    
    public ObservableCollection<IngestionJobItem> Jobs
    {
        get => _jobs;
        set => SetProperty(ref _jobs, value);
    }
    
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }
    
    public ICommand RefreshCommand { get; }
    public ICommand CancelJobCommand { get; }
    
    public JobsViewModel(IApiClient apiClient)
    {
        _apiClient = apiClient;
        Jobs = new ObservableCollection<IngestionJobItem>();
        RefreshCommand = new Command(async () => await LoadJobsAsync());
        CancelJobCommand = new Command<string>(async id => await CancelJobAsync(id));
        
        LoadJobsAsync();
    }
    
    private async Task LoadJobsAsync()
    {
        try
        {
            IsLoading = true;
            var jobs = await _apiClient.GetIngestionJobsAsync();
            
            Jobs.Clear();
            foreach (var job in jobs)
            {
                Jobs.Add(new IngestionJobItem(job));
            }
        }
        catch (Exception ex)
        {
            // Handle error
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private async Task CancelJobAsync(string jobId)
    {
        try
        {
            await _apiClient.CancelIngestionJobAsync(jobId);
            await LoadJobsAsync();
        }
        catch (Exception ex)
        {
            // Handle error
        }
    }
}

public class IngestionJobItem : ObservableObject
{
    private readonly IngestionJob _job;
    
    public string Id => _job.Id;
    public string JobType => _job.JobType;
    public string Status => _job.Status;
    public DateTime CreatedDate => _job.CreatedDate;
    public int TotalFiles => _job.TotalFiles;
    public int ProcessedFiles => _job.ProcessedFiles;
    public double Progress => _job.ProgressPercentage;
    public bool CanCancel => _job.CanCancel;
    public string? ErrorMessage => _job.ErrorMessage;
    
    public IngestionJobItem(IngestionJob job)
    {
        _job = job;
    }
}
```

#### 3.1.5 WebSourcesViewModel

```csharp
namespace MotorcycleRAG.Admin.ViewModels;

public class WebSourcesViewModel : ObservableObject
{
    private readonly IApiClient _apiClient;
    
    private ObservableCollection<WebSourceItem> _webSources;
    private bool _isLoading;
    private string _newSourceUrl;
    
    public ObservableCollection<WebSourceItem> WebSources
    {
        get => _webSources;
        set => SetProperty(ref _webSources, value);
    }
    
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }
    
    public string NewSourceUrl
    {
        get => _newSourceUrl;
        set => SetProperty(ref _newSourceUrl, value);
    }
    
    public ICommand RefreshCommand { get; }
    public ICommand AddSourceCommand { get; }
    public ICommand DeleteSourceCommand { get; }
    
    public WebSourcesViewModel(IApiClient apiClient)
    {
        _apiClient = apiClient;
        WebSources = new ObservableCollection<WebSourceItem>();
        RefreshCommand = new Command(async () => await LoadWebSourcesAsync());
        AddSourceCommand = new Command(async () => await AddWebSourceAsync(), 
            () => !string.IsNullOrEmpty(NewSourceUrl) && !IsLoading);
        DeleteSourceCommand = new Command<string>(async id => await DeleteWebSourceAsync(id));
        
        LoadWebSourcesAsync();
    }
    
    private async Task LoadWebSourcesAsync()
    {
        try
        {
            IsLoading = true;
            var sources = await _apiClient.GetWebSourcesAsync();
            
            WebSources.Clear();
            foreach (var source in sources)
            {
                WebSources.Add(new WebSourceItem(source));
            }
        }
        catch (Exception ex)
        {
            // Handle error
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private async Task AddWebSourceAsync()
    {
        try
        {
            IsLoading = true;
            var result = await _apiClient.AddWebSourceAsync(NewSourceUrl);
            
            if (result.Success)
            {
                NewSourceUrl = string.Empty;
                await LoadWebSourcesAsync();
            }
        }
        catch (Exception ex)
        {
            // Handle error
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private async Task DeleteWebSourceAsync(string id)
    {
        try
        {
            await _apiClient.DeleteWebSourceAsync(id);
            await LoadWebSourcesAsync();
        }
        catch (Exception ex)
        {
            // Handle error
        }
    }
}

public class WebSourceItem : ObservableObject
{
    private readonly WebSource _source;
    
    public string Id => _source.Id;
    public string Url => _source.Url;
    public string Name => _source.Name;
    public string? Description => _source.Description;
    public string TrustTier => _source.TrustTier;
    public bool IsEnabled => _source.IsEnabled;
    public DateTime? LastIndexedDate => _source.LastIndexedDate;
    
    public WebSourceItem(WebSource source)
    {
        _source = source;
    }
}
```

#### 3.1.6 ToolsViewModel (MCP Configuration)

```csharp
namespace MotorcycleRAG.Admin.ViewModels;

public class ToolsViewModel : ObservableObject
{
    private readonly IApiClient _apiClient;
    
    private ObservableCollection<ToolConfigItem> _tools;
    private bool _isLoading;
    
    public ObservableCollection<ToolConfigItem> Tools
    {
        get => _tools;
        set => SetProperty(ref _tools, value);
    }
    
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }
    
    public ICommand RefreshCommand { get; }
    public ICommand SaveToolCommand { get; }
    
    public ToolsViewModel(IApiClient apiClient)
    {
        _apiClient = apiClient;
        Tools = new ObservableCollection<ToolConfigItem>();
        RefreshCommand = new Command(async () => await LoadToolsAsync());
        SaveToolCommand = new Command<ToolConfigItem>(async tool => await SaveToolAsync(tool));
        
        LoadToolsAsync();
    }
    
    private async Task LoadToolsAsync()
    {
        try
        {
            IsLoading = true;
            var tools = await _apiClient.GetMcpToolsAsync();
            
            Tools.Clear();
            foreach (var tool in tools)
            {
                Tools.Add(new ToolConfigItem(tool));
            }
        }
        catch (Exception ex)
        {
            // Handle error
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private async Task SaveToolAsync(ToolConfigItem tool)
    {
        try
        {
            await _apiClient.UpdateMcpToolAsync(tool.ToDto());
            await LoadToolsAsync();
        }
        catch (Exception ex)
        {
            // Handle error
        }
    }
}

public class ToolConfigItem : ObservableObject
{
    private readonly McpToolConfig _config;
    private bool _isEnabled;
    
    public string Id => _config.Id;
    public string Name => _config.Name;
    public string Description => _config.Description;
    public string ServerUrl => _config.ServerUrl;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            SetProperty(ref _isEnabled, value);
            _config.IsEnabled = value;
        }
    }
    
    public ToolConfigItem(McpToolConfig config)
    {
        _config = config;
        _isEnabled = config.IsEnabled;
    }
    
    public McpToolConfig ToDto() => _config;
}
```

### 3.2 Mobile Application View Models

#### 3.2.1 ChatViewModel

```csharp
namespace MotorcycleRAG.MobileApp.ViewModels;

public class ChatViewModel : ObservableObject
{
    private readonly IMotorcycleRagApiClient _apiClient;
    private readonly IAuthenticationState _authState;
    
    private ObservableCollection<ChatMessage> _messages;
    private string _queryText;
    private bool _isSending;
    private bool _isLoadingHistory;
    
    public ObservableCollection<ChatMessage> Messages
    {
        get => _messages;
        set => SetProperty(ref _messages, value);
    }
    
    public string QueryText
    {
        get => _queryText;
        set => SetProperty(ref _queryText, value);
    }
    
    public bool IsSending
    {
        get => _isSending;
        set => SetProperty(ref _isSending, value);
    }
    
    public bool IsLoadingHistory
    {
        get => _isLoadingHistory;
        set => SetProperty(ref _isLoadingHistory, value);
    }
    
    public ICommand SendQueryCommand { get; }
    public ICommand LoadHistoryCommand { get; }
    
    public ChatViewModel(IMotorcycleRagApiClient apiClient, 
        IAuthenticationState authState)
    {
        _apiClient = apiClient;
        _authState = authState;
        Messages = new ObservableCollection<ChatMessage>();
        SendQueryCommand = new Command(async () => await SendQueryAsync(), 
            () => !string.IsNullOrEmpty(QueryText) && !IsSending);
        LoadHistoryCommand = new Command(async () => await LoadHistoryAsync());
        
        LoadHistoryAsync();
    }
    
    private async Task SendQueryAsync()
    {
        try
        {
            IsSending = true;
            
            var userMessage = new ChatMessage
            {
                Content = QueryText,
                Sender = MessageSender.User,
                Timestamp = DateTime.UtcNow
            };
            Messages.Add(userMessage);
            
            var request = new MotorcycleQueryRequest
            {
                Query = QueryText,
                UserId = _authState.UserId
            };
            
            var response = await _apiClient.QueryMotorcyclesAsync(request);
            
            var assistantMessage = new ChatMessage
            {
                Content = response.Response,
                Sender = MessageSender.Assistant,
                Timestamp = DateTime.UtcNow,
                Citations = response.Sources.Select(s => new SourceCitation
                {
                    Id = s.Id,
                    Content = s.Content,
                    SourceUrl = s.SourceUrl,
                    SourceName = s.SourceName,
                    RelevanceScore = s.RelevanceScore,
                    Metadata = s.Metadata
                }).ToList()
            };
            Messages.Add(assistantMessage);
            
            QueryText = string.Empty;
        }
        catch (Exception ex)
        {
            // Handle error
        }
        finally
        {
            IsSending = false;
        }
    }
    
    private async Task LoadHistoryAsync()
    {
        try
        {
            IsLoadingHistory = true;
            var history = await _apiClient.GetConversationHistoryAsync();
            
            Messages.Clear();
            foreach (var msg in history)
            {
                Messages.Add(new ChatMessage
                {
                    Content = msg.Content,
                    Sender = msg.Sender,
                    Timestamp = msg.Timestamp,
                    Citations = msg.Citations
                });
            }
        }
        catch (Exception ex)
        {
            // Handle error
        }
        finally
        {
            IsLoadingHistory = false;
        }
    }
}
```

#### 3.2.2 UserProfileViewModel

```csharp
namespace MotorcycleRAG.MobileApp.ViewModels;

public class UserProfileViewModel : ObservableObject
{
    private readonly IMotorcycleRagApiClient _apiClient;
    private readonly IAuthenticationState _authState;
    
    private UserProfile _userProfile;
    private DailyUsageSummary _usageSummary;
    private bool _isLoading;
    
    public UserProfile UserProfile
    {
        get => _userProfile;
        set => SetProperty(ref _userProfile, value);
    }
    
    public DailyUsageSummary UsageSummary
    {
        get => _usageSummary;
        set => SetProperty(ref _usageSummary, value);
    }
    
    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }
    
    public ICommand RefreshCommand { get; }
    public ICommand SignOutCommand { get; }
    
    public UserProfileViewModel(IMotorcycleRagApiClient apiClient, 
        IAuthenticationState authState)
    {
        _apiClient = apiClient;
        _authState = authState;
        RefreshCommand = new Command(async () => await LoadProfileAsync());
        SignOutCommand = new Command(async () => await SignOutAsync());
        
        LoadProfileAsync();
    }
    
    private async Task LoadProfileAsync()
    {
        try
        {
            IsLoading = true;
            var profile = await _apiClient.GetUserProfileAsync();
            var usage = await _apiClient.GetUserUsageAsync();
            
            UserProfile = profile;
            UsageSummary = usage;
        }
        catch (Exception ex)
        {
            // Handle error
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    private async Task SignOutAsync()
    {
        try
        {
            await _authState.ClearUser();
            // Navigate to login page
        }
        catch (Exception ex)
        {
            // Handle error
        }
    }
}
```

### 3.3 Navigation Models

#### 3.3.1 NavigationItem

```csharp
namespace MotorcycleRAG.Admin.Models;

public class NavigationItem
{
    public string Title { get; set; }
    public string Icon { get; set; }
    public Type TargetType { get; set; }
    public bool IsEnabled { get; set; } = true;
    
    public NavigationItem(string title, string icon, Type targetType, 
        bool isEnabled = true)
    {
        Title = title;
        Icon = icon;
        TargetType = targetType;
        IsEnabled = isEnabled;
    }
}

public static class AdminNavigationItems
{
    public static List<NavigationItem> Items => new()
    {
        new NavigationItem("Dashboard", "dashboard.png", typeof(DashboardPage)),
        new NavigationItem("Upload", "upload.png", typeof(UploadPage)),
        new NavigationItem("Jobs", "jobs.png", typeof(JobsPage)),
        new NavigationItem("Web Sources", "web.png", typeof(WebSourcesPage)),
        new NavigationItem("Tools", "tools.png", typeof(ToolsPage))
    };
}

public static class MobileNavigationItems
{
    public static List<NavigationItem> Items => new()
    {
        new NavigationItem("Chat", "chat.png", typeof(ChatPage)),
        new NavigationItem("Conversations", "history.png", typeof(ConversationListPage)),
        new NavigationItem("Profile", "profile.png", typeof(UserProfilePage))
    };
}
```

#### 3.3.2 UserProfile (UI Model)

```csharp
namespace MotorcycleRAG.Admin.Models;

public class UserProfile
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string? AvatarUrl { get; set; }
    public string UserInitials
    {
        get
        {
            if (string.IsNullOrEmpty(Name))
                return "?";
            
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"{parts[0][0]}{parts[1][0]}";
            return parts[0][0].ToString().ToUpper();
        }
    }
}
```

## 4. API Contracts (DTOs)

### 4.1 Request DTOs

#### MotorcycleQueryRequest

```csharp
namespace MotorcycleRAG.Domain.DTOs;

public class MotorcycleQueryRequest
{
    [Required]
    [StringLength(1000)]
    public string Query { get; set; } = string.Empty;
    
    public SearchPreferences Preferences { get; set; } = new();
    
    [StringLength(100)]
    public string UserId { get; set; } = string.Empty;
    
    public QueryContext Context { get; set; } = new();
}

public class SearchPreferences
{
    public bool IncludeWebSources { get; set; } = true;
    public bool IncludePDFSources { get; set; } = true;
    public int MaxResults { get; set; } = 10;
    public double MinRelevanceScore { get; set; } = 0.5;
    public string[] PreferredSources { get; set; } = Array.Empty<string>();
}

public class QueryContext
{
    public string? SessionId { get; set; }
    public string[] PreviousQueries { get; set; } = Array.Empty<string>();
    public string Language { get; set; } = "en";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool RequiresMultiModal { get; set; } = false;
    public string? CorrelationId { get; set; }
}
```

### 4.2 Response DTOs

#### MotorcycleQueryResponse

```csharp
namespace MotorcycleRAG.Domain.DTOs;

public class MotorcycleQueryResponse
{
    [Required]
    public string Response { get; set; } = string.Empty;
    
    public SearchResult[] Sources { get; set; } = Array.Empty<SearchResult>();
    
    public QueryMetrics Metrics { get; set; } = new();
    
    [Required]
    public string QueryId { get; set; } = string.Empty;
    
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public class SearchResult
{
    public string Id { get; set; }
    public string Content { get; set; }
    public double RelevanceScore { get; set; }
    public SourceMetadata Source { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string[] Highlights { get; set; }
}

public class SourceMetadata
{
    public string AgentType { get; set; }
    public string SourceName { get; set; }
    public string SourceUrl { get; set; }
    public string DocumentId { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class QueryMetrics
{
    public double TotalDuration { get; set; }
    public double VectorSearchDuration { get; set; }
    public double WebSearchDuration { get; set; }
    public double GenerationDuration { get; set; }
    public int SourcesRetrieved { get; set; }
    public bool DegradedMode { get; set; }
}
```

## 5. Data Relationships

### 5.1 Entity Relationship Diagram (ERD)

```
User (1) ----< (N) Usage
  |
  +----< (1) UserPlan

IngestionJob (1) ----< (N) IngestionJobFile

WebSource (1) ----< (N) WebScrapeRun

MotorcycleDocument (N) ----< (1) IngestionJob
```

### 5.2 Navigation Flow

```
Admin App:
├── Dashboard (Overview)
├── Upload (File upload & local processing)
├── Jobs (Ingestion job monitoring)
├── Web Sources (Website management)
└── Tools (MCP configuration)

Mobile App:
├── Chat (Query interface)
├── Conversations (History)
└── Profile (User info & usage)
```

## 6. Material.Components.Maui Integration

### 6.1 Component Mapping

| UI Element | Material Component | Usage |
|------------|-------------------|--------|
| Left Menu | Flyout | Navigation menu |
| Top Header | TopAppBar | Page header with profile |
| Profile Avatar | Avatar | User profile display |
| Sign Out Button | IconButton | Logout action |
| Upload Button | Button (Filled) | Primary action |
| File Picker | TextField + Button | File selection |
| Progress Bar | ProgressBar | Upload/processing progress |
| Status Cards | Card | Job status display |
| Source List | Card Collection | Web sources display |
| Tool Toggle | Switch | Enable/disable tools |
| Chat Bubbles | Card | Message display |
| Query Input | TextField | User input |
| Send Button | IconButton (Filled) | Submit query |

### 6.2 Navigation Shell Configuration

```xml
<!-- AppShell.xaml -->
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
       xmlns:mc="clr-namespace:Material.Components.Maui;assembly=Material.Components.Maui"
       xmlns:pages="clr-namespace:MotorcycleRAG.Admin.Pages"
       FlyoutBehavior="Locked"
       FlyoutWidth="300">

    <Shell.FlyoutHeader>
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

## 7. Summary

This data model document provides:

1. **Domain Entities**: Core business entities with behavior (User, UserPlan, Usage, IngestionJob, WebSource, MotorcycleDocument)
2. **Value Objects**: Immutable types (SearchQuery, Embedding, Citation)
3. **UI View Models**: MAUI-specific models for Admin and MobileApp (Authentication, Dashboard, Upload, Jobs, WebSources, Tools, Chat, UserProfile)
4. **Navigation Models**: Navigation items and user profile models
5. **API Contracts**: Request/response DTOs for API communication
6. **Material.Components.Maui Integration**: Component mapping and navigation shell configuration

All models follow Clean Architecture principles with clear separation between domain entities, UI view models, and API contracts.
