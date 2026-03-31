using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Utilities;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for managing web sources in the admin panel.
/// Handles loading, creating, and deleting web sources with proper error handling and state management.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812: Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "For data binding")]
internal partial class WebSourcesViewModel : ObservableObject
{
    private readonly ApiClient _apiClient;
    private readonly IAdminAuthService _authService;
    private readonly IConfigurationStateService _configService;
    private readonly ILogger<WebSourcesViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<WebSourceViewModel> webSources = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool showAddSourceForm;

    [ObservableProperty]
    private string newSourceName = string.Empty;

    [ObservableProperty]
    private string newSourceUrl = string.Empty;

    [ObservableProperty]
    private string newSourceDescription = string.Empty;

    [ObservableProperty]
    private int selectedTrustTier = (int)WebTrustTier.TierB;

    [ObservableProperty]
    private bool newSourceIsEnabled = true;

    [ObservableProperty]
    private bool newSourceIncludeInSearch = true;

    public WebSourcesViewModel(
        ApiClient apiClient,
        IAdminAuthService authService,
        IConfigurationStateService configService,
        ILogger<WebSourcesViewModel> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [RelayCommand]
    internal async Task LoadSourcesAsync()
    {
        await MauiThreading.RunOnMainThreadAsync(() =>
        {
            IsLoading = true;
            ErrorMessage = null;
        }).ConfigureAwait(false);

        try
        {
            var sources = await MauiThreading.RunOffMainThreadAsync(async () =>
            {
                var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
                if (!authorized)
                {
                    return null;
                }

                return await _apiClient.GetWebSourcesAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

            if (sources is null)
            {
                return;
            }

            await MauiThreading.RunOnMainThreadAsync(() =>
            {
                WebSources.Clear();
                foreach (var source in sources)
                {
                    WebSources.Add(new WebSourceViewModel(source));
                }
            }).ConfigureAwait(false);

            _logger.LogInformation("Loaded {Count} web sources", sources.Count);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "API not available for loading web sources");
            await MauiThreading.RunOnMainThreadAsync(() => WebSources.Clear()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            _logger.LogError(ex, "Error loading web sources");

            await MauiThreading.RunOnMainThreadAsync(() =>
                ErrorMessage = $"Failed to load web sources: {sanitizedMessage}").ConfigureAwait(false);
            await ErrorPresenter.ShowErrorAsync("Error", ErrorMessage ?? "Failed to load web sources.").ConfigureAwait(false);
        }
        finally
        {
            await MauiThreading.RunOnMainThreadAsync(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    internal void OpenAddSourceForm()
    {
        ResetForm();
        ShowAddSourceForm = true;
    }

    [RelayCommand]
    internal void CloseAddSourceForm()
    {
        ShowAddSourceForm = false;
        ResetForm();
    }

    [RelayCommand]
    internal async Task AddSourceAsync()
    {
        await MauiThreading.RunOnMainThreadAsync(() =>
        {
            IsLoading = true;
            ErrorMessage = null;
        }).ConfigureAwait(false);

        try
        {
            var validationError = ValidateNewSource();
            if (!string.IsNullOrWhiteSpace(validationError))
            {
                await MauiThreading.RunOnMainThreadAsync(() => ErrorMessage = validationError).ConfigureAwait(false);
                return;
            }

            var createdSource = await MauiThreading.RunOffMainThreadAsync(async () =>
            {
                var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
                if (!authorized)
                {
                    return null;
                }

                var newSource = new WebSource
                {
                    Name = NewSourceName,
                    Url = NewSourceUrl,
                    Description = NewSourceDescription,
                    TrustTier = SelectedTrustTier,
                    IsEnabled = NewSourceIsEnabled,
                    IncludeInSearch = NewSourceIncludeInSearch,
                    CrawlFrequencyHours = 24,
                    MaxCrawlDepth = 2
                };

                return await _apiClient.AddWebSourceAsync(newSource).ConfigureAwait(false);
            }).ConfigureAwait(false);

            if (createdSource is null)
            {
                return;
            }

            await MauiThreading.RunOnMainThreadAsync(() =>
            {
                WebSources.Add(new WebSourceViewModel(createdSource));
                ShowAddSourceForm = false;
                ResetForm();
            }).ConfigureAwait(false);

            _logger.LogInformation("Added web source: {SourceName}", createdSource.Name);
            await ErrorPresenter.ShowSuccessAsync("Success", "Web source added successfully").ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            await MauiThreading.RunOnMainThreadAsync(() =>
                ErrorMessage = "You do not have permission to add web sources").ConfigureAwait(false);
            _logger.LogWarning(ex, "User not authorized to add web sources");
        }
        catch (InvalidOperationException ex)
        {
            await MauiThreading.RunOnMainThreadAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
            _logger.LogWarning(ex, "Invalid operation while adding web source");
        }
        catch (Exception ex)
        {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            _logger.LogError(ex, "Error adding web source");

            await MauiThreading.RunOnMainThreadAsync(() =>
                ErrorMessage = $"Failed to add web source: {sanitizedMessage}").ConfigureAwait(false);
            await ErrorPresenter.ShowErrorAsync("Error", ErrorMessage ?? "Failed to add web source.").ConfigureAwait(false);
        }
        finally
        {
            await MauiThreading.RunOnMainThreadAsync(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    internal async Task DeleteSourceAsync(int sourceId)
    {
        var sourceToDelete = WebSources.FirstOrDefault(source => source.Id == sourceId);
        if (sourceToDelete is null)
        {
            return;
        }

        var confirm = await ErrorPresenter.ShowConfirmAsync(
            "Delete Web Source",
            $"Are you sure you want to delete '{sourceToDelete.Name}'?",
            "Yes",
            "No").ConfigureAwait(false);

        if (!confirm)
        {
            return;
        }

        await MauiThreading.RunOnMainThreadAsync(() =>
        {
            IsLoading = true;
            ErrorMessage = null;
        }).ConfigureAwait(false);

        try
        {
            var deleted = await MauiThreading.RunOffMainThreadAsync(async () =>
            {
                var authorized = await EnsureAuthorizedAsync().ConfigureAwait(false);
                if (!authorized)
                {
                    return false;
                }

                await _apiClient.DeleteWebSourceAsync(sourceId).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);

            if (!deleted)
            {
                return;
            }

            await MauiThreading.RunOnMainThreadAsync(() => WebSources.Remove(sourceToDelete)).ConfigureAwait(false);
            _logger.LogInformation("Deleted web source with ID: {SourceId}", sourceId);
            await ErrorPresenter.ShowSuccessAsync("Success", "Web source deleted successfully").ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            await MauiThreading.RunOnMainThreadAsync(() =>
                ErrorMessage = "You do not have permission to delete web sources").ConfigureAwait(false);
            _logger.LogWarning(ex, "User not authorized to delete web sources");
            await ErrorPresenter.ShowErrorAsync("Error", ErrorMessage ?? "Access denied.").ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            await MauiThreading.RunOnMainThreadAsync(() => ErrorMessage = ex.Message).ConfigureAwait(false);
            _logger.LogWarning(ex, "Invalid operation while deleting web source");
            await ErrorPresenter.ShowErrorAsync("Error", ErrorMessage ?? "Delete failed.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            _logger.LogError(ex, "Error deleting web source {SourceId}", sourceId);

            await MauiThreading.RunOnMainThreadAsync(() =>
                ErrorMessage = $"Failed to delete web source: {sanitizedMessage}").ConfigureAwait(false);
            await ErrorPresenter.ShowErrorAsync("Error", ErrorMessage ?? "Failed to delete web source.").ConfigureAwait(false);
        }
        finally
        {
            await MauiThreading.RunOnMainThreadAsync(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    internal Task RefreshAsync() => LoadSourcesAsync();

    internal async Task InitializeAsync()
    {
        try
        {
            await LoadSourcesAsync().ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "API not available for loading web sources");
        }
    }

    private async Task<bool> EnsureAuthorizedAsync()
    {
        if (!_configService.IsApiConfigured)
        {
            _logger.LogWarning("Web sources page blocked: API not configured");
            await ErrorPresenter.ShowWarningAsync(
                "Configuration Required",
                "API is not configured. Go to Settings to configure the API base URL.").ConfigureAwait(false);
            return false;
        }

        if (!_authService.IsSignedIn())
        {
            _logger.LogWarning("Web sources page blocked: user not signed in");
            await ErrorPresenter.ShowWarningAsync(
                "Sign In Required",
                "Please sign in to manage web sources.").ConfigureAwait(false);
            return false;
        }

        var isAuthorized = await _authService.IsAuthorizedAdminAsync().ConfigureAwait(false);
        if (!isAuthorized)
        {
            _logger.LogWarning("Web sources page blocked: user lacks admin permissions");
            await ErrorPresenter.ShowWarningAsync(
                "Access Denied",
                "You do not have permission to manage web sources.").ConfigureAwait(false);
            return false;
        }

        return true;
    }

    private string? ValidateNewSource()
    {
        if (string.IsNullOrWhiteSpace(NewSourceName))
        {
            return "Source name is required";
        }

        if (string.IsNullOrWhiteSpace(NewSourceUrl))
        {
            return "Source URL is required";
        }

        if (!Uri.TryCreate(NewSourceUrl, UriKind.Absolute, out var uri))
        {
            return "Source URL must be a valid absolute URL";
        }

        if (!UrlValidator.IsValidUrl(uri))
        {
            return "Invalid or disallowed URL format (SSRF protection)";
        }

        return null;
    }

    private void ResetForm()
    {
        NewSourceName = string.Empty;
        NewSourceUrl = string.Empty;
        NewSourceDescription = string.Empty;
        SelectedTrustTier = (int)WebTrustTier.TierB;
        NewSourceIsEnabled = true;
        NewSourceIncludeInSearch = true;
    }
}

/// <summary>
/// ViewModel for a single web source in the list.
/// </summary>
internal class WebSourceViewModel : ObservableObject
{
    private readonly WebSource _source;

    internal WebSourceViewModel(WebSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    internal int Id => _source.Id;
    internal string Name => _source.Name;
    internal Uri Url => new Uri(_source.Url, UriKind.Absolute);
    internal string Description => _source.Description;
    internal bool IsEnabled => _source.IsEnabled;
    internal int TrustTier => _source.TrustTier;
    internal DateTime CreatedDate => _source.CreatedDate;
    internal DateTime? LastUpdatedDate => _source.LastUpdatedDate;
    internal DateTime? LastCrawledDate => _source.LastCrawledDate;
    internal int CrawlFrequencyHours => _source.CrawlFrequencyHours;
    internal bool IncludeInSearch => _source.IncludeInSearch;
    internal int MaxCrawlDepth => _source.MaxCrawlDepth;

    internal string TrustTierLabel => TrustTier switch
    {
        (int)WebTrustTier.TierA => "Tier A - OEM",
        (int)WebTrustTier.TierB => "Tier B - Media",
        (int)WebTrustTier.TierC => "Tier C - Community",
        _ => "Unknown"
    };

    internal Color TrustTierColor => TrustTier switch
    {
        (int)WebTrustTier.TierA => Colors.Green,
        (int)WebTrustTier.TierB => Colors.Blue,
        (int)WebTrustTier.TierC => Colors.Orange,
        _ => Colors.Gray
    };

    internal string StatusLabel => IsEnabled ? "Enabled" : "Disabled";
}
