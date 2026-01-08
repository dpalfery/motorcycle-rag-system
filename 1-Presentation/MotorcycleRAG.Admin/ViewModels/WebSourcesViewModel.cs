using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Utilities;
using MotorcycleRAG.Admin.Constants;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for managing web sources in the admin panel.
/// Handles loading, creating, and deleting web sources with proper error handling and state management.
/// </summary>
public partial class WebSourcesViewModel : ObservableObject {
    private readonly ApiClient _apiClient;
    private readonly IAdminAuthService _authService;
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

    public WebSourcesViewModel(ApiClient apiClient, IAdminAuthService authService, ILogger<WebSourcesViewModel> logger) {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Commands

    /// <summary>
    /// Command to load web sources from the API
    /// </summary>
    [RelayCommand]
    internal async Task LoadSourcesAsync() {
        IsLoading = true;
        ErrorMessage = null;

        try {
            await EnsureAuthorizedAsync();
            var sources = await _apiClient.GetWebSourcesAsync();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                WebSources.Clear();
                foreach (var source in sources)
                {
                    WebSources.Add(new WebSourceViewModel(source));
                }
            }).ConfigureAwait(false);

            _logger.LogInformation("Loaded {Count} web sources", sources.Count);
        }
        catch (UnauthorizedAccessException ex) {
            // User not authorized - expected in demo mode
            _logger.LogWarning(ex, "User not authorized to view web sources");
            await MainThread.InvokeOnMainThreadAsync(() => WebSources.Clear());
        }
        catch (HttpRequestException ex) {
            // API not available - silently fail
            _logger.LogWarning(ex, "API not available for loading web sources");
            await MainThread.InvokeOnMainThreadAsync(() => WebSources.Clear());
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            _logger.LogError(ex, "Error loading web sources");

            await MainThread.InvokeOnMainThreadAsync(async () => {
                ErrorMessage = $"Failed to load web sources: {sanitizedMessage}";
                var window = Application.Current?.Windows?.FirstOrDefault();
                if (window?.Page != null) {
                    await window.Page.DisplayAlertAsync("Error", ErrorMessage, "OK");
                }
            });
        }
        finally {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Command to show the add source form
    /// </summary>
    [RelayCommand]
    internal void OpenAddSourceForm() {
        ResetForm();
        ShowAddSourceForm = true;
    }

    /// <summary>
    /// Command to hide the add source form
    /// </summary>
    [RelayCommand]
    internal void CloseAddSourceForm() {
        ShowAddSourceForm = false;
        ResetForm();
    }

    /// <summary>
    /// Command to add a new web source
    /// </summary>
    [RelayCommand]
    internal async Task AddSourceAsync() {
        IsLoading = true;
        ErrorMessage = null;

        try {
            // Validate form
            if (string.IsNullOrWhiteSpace(NewSourceName)) {
                ErrorMessage = "Source name is required";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewSourceUrl)) {
                ErrorMessage = "Source URL is required";
                return;
            }

            if (!UrlValidator.IsValidUrl(NewSourceUrl)) {
                ErrorMessage = "Invalid or disallowed URL format (SSRF protection)";
                return;
            }

            await EnsureAuthorizedAsync();

            var newSource = new WebSource {
                Name = NewSourceName,
                Url = NewSourceUrl,
                Description = NewSourceDescription,
                TrustTier = SelectedTrustTier,
                IsEnabled = NewSourceIsEnabled,
                IncludeInSearch = NewSourceIncludeInSearch,
                CrawlFrequencyHours = 24,
                MaxCrawlDepth = 2
            };

            var createdSource = await _apiClient.AddWebSourceAsync(newSource);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                WebSources.Add(new WebSourceViewModel(createdSource));
                ShowAddSourceForm = false;
                ResetForm();
            }).ConfigureAwait(false);

            _logger.LogInformation("Added web source: {SourceName}", createdSource.Name);

            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window?.Page != null) {
                await window.Page.DisplayAlertAsync("Success", "Web source added successfully", "OK");
            }
        }
        catch (UnauthorizedAccessException ex) {
            ErrorMessage = "You do not have permission to add web sources";
            _logger.LogWarning(ex, "User not authorized to add web sources");
        }
        catch (InvalidOperationException ex) {
            ErrorMessage = ex.Message;
            _logger.LogWarning(ex, "Invalid operation while adding web source");
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            ErrorMessage = $"Failed to add web source: {sanitizedMessage}";
            _logger.LogError(ex, "Error adding web source");

            await MainThread.InvokeOnMainThreadAsync(async () => {
                var window = Application.Current?.Windows?.FirstOrDefault();
                if (window?.Page != null) {
                    await window.Page.DisplayAlertAsync("Error", ErrorMessage, "OK");
                }
            });
        }
        finally {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Command to delete a web source
    /// </summary>
    [RelayCommand]
    internal async Task DeleteSourceAsync(int sourceId) {
        try {
            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window?.Page == null)
                return;

            var sourceToDelete = WebSources.FirstOrDefault(s => s.Id == sourceId);
            if (sourceToDelete == null)
                return;

            var confirm = await window.Page.DisplayAlertAsync(
                "Delete Web Source",
                $"Are you sure you want to delete '{sourceToDelete.Name}'?",
                "Yes",
                "No");

            if (!confirm)
                return;

            IsLoading = true;
            ErrorMessage = null;

            await EnsureAuthorizedAsync();
            await _apiClient.DeleteWebSourceAsync(sourceId);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                WebSources.Remove(sourceToDelete);
            }).ConfigureAwait(false);

            _logger.LogInformation("Deleted web source with ID: {SourceId}", sourceId);

            await window.Page.DisplayAlertAsync("Success", "Web source deleted successfully", "OK");
        }
        catch (UnauthorizedAccessException ex) {
            ErrorMessage = "You do not have permission to delete web sources";
            _logger.LogWarning(ex, "User not authorized to delete web sources");

            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window?.Page != null) {
                await window.Page.DisplayAlertAsync("Error", ErrorMessage, "OK");
            }
        }
        catch (InvalidOperationException ex) {
            ErrorMessage = ex.Message;
            _logger.LogWarning(ex, "Invalid operation while deleting web source");

            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window?.Page != null) {
                await window.Page.DisplayAlertAsync("Error", ErrorMessage, "OK");
            }
        }
        catch (Exception ex) {
            var sanitizedMessage = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            ErrorMessage = $"Failed to delete web source: {sanitizedMessage}";
            _logger.LogError(ex, "Error deleting web source {SourceId}", sourceId);

            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window?.Page != null) {
                await window.Page.DisplayAlertAsync("Error", ErrorMessage, "OK");
            }
        }
        finally {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Command to refresh the web sources list
    /// </summary>
    [RelayCommand]
    internal async Task RefreshAsync() {
        await LoadSourcesAsync();
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Initialize the view model
    /// </summary>
    internal async Task InitializeAsync() {
        try {
            await EnsureAuthorizedAsync();
            await LoadSourcesAsync();
        }
        catch (UnauthorizedAccessException ex) {
            // User not authorized - this is expected in demo mode
            _logger.LogDebug(ex, "User not authorized to view web sources");
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Ensure the user is authorized to manage web sources
    /// </summary>
    private async Task EnsureAuthorizedAsync() {
        if (!_authService.IsSignedIn()) {
            throw new UnauthorizedAccessException("User is not signed in");
        }

        var roles = await _authService.GetUserRolesAsync();
        var isAdmin = AdminRoles.GetValidAdminRoles(roles).Any();
        if (!isAdmin) {
            throw new UnauthorizedAccessException("User does not have admin permissions");
        }
    }

    /// <summary>
    /// Reset the add source form to default values
    /// </summary>
    private void ResetForm() {
        NewSourceName = string.Empty;
        NewSourceUrl = string.Empty;
        NewSourceDescription = string.Empty;
        SelectedTrustTier = (int)WebTrustTier.TierB;
        NewSourceIsEnabled = true;
        NewSourceIncludeInSearch = true;
    }

    #endregion
}

/// <summary>
/// ViewModel for a single web source in the list
/// </summary>
public class WebSourceViewModel : ObservableObject {
    private readonly WebSource _source;

    public WebSourceViewModel(WebSource source) {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public int Id => _source.Id;
    public string Name => _source.Name;
    public string Url => _source.Url;
    public string Description => _source.Description;
    public bool IsEnabled => _source.IsEnabled;
    public int TrustTier => _source.TrustTier;
    public DateTime CreatedDate => _source.CreatedDate;
    public DateTime? LastUpdatedDate => _source.LastUpdatedDate;
    public DateTime? LastCrawledDate => _source.LastCrawledDate;
    public int CrawlFrequencyHours => _source.CrawlFrequencyHours;
    public bool IncludeInSearch => _source.IncludeInSearch;
    public int MaxCrawlDepth => _source.MaxCrawlDepth;

    /// <summary>
    /// Get the display label for the trust tier
    /// </summary>
    public string TrustTierLabel => TrustTier switch {
        (int)WebTrustTier.TierA => "Tier A - OEM",
        (int)WebTrustTier.TierB => "Tier B - Media",
        (int)WebTrustTier.TierC => "Tier C - Community",
        _ => "Unknown"
    };

    /// <summary>
    /// Get the color for the trust tier badge
    /// </summary>
    public Color TrustTierColor => TrustTier switch {
        (int)WebTrustTier.TierA => Colors.Green,
        (int)WebTrustTier.TierB => Colors.Blue,
        (int)WebTrustTier.TierC => Colors.Orange,
        _ => Colors.Gray
    };

    /// <summary>
    /// Status display label
    /// </summary>
    public string StatusLabel => IsEnabled ? "Enabled" : "Disabled";
}


