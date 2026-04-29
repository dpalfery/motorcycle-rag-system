using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.Services;
using MotorcycleRAG.Admin.Services.Dtos;
using MotorcycleRAG.Admin.Utilities;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// View model for the admin user-management page.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI DI")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Visibility", Justification = "Used by compiled bindings")]
internal partial class UserManagementViewModel : ObservableObject {
    private const int DefaultPageSize = 100;
    private readonly ApiClient _apiClient;
    private readonly IAdminAuthService _authService;
    private readonly IConfigurationStateService _configService;
    private readonly ILogger<UserManagementViewModel> _logger;
    private readonly Func<Func<Task>, Task> _runOnMainThreadAsync;
    private readonly Func<Func<Task>, Task> _runOffMainThreadAsync;
    private readonly Func<string, string, Task> _showWarningAsync;
    private readonly Func<string, string, Task> _showSuccessAsync;
    private readonly Func<string, string, Task> _showErrorAsync;
    private readonly Func<string, string, string, string, Task<bool>> _showConfirmAsync;

    [ObservableProperty]
    private ObservableCollection<UserManagementRowViewModel> rows = new();

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private int totalCount;

    public UserManagementViewModel(
        ApiClient apiClient,
        IAdminAuthService authService,
        IConfigurationStateService configService,
        ILogger<UserManagementViewModel> logger,
        Func<Func<Task>, Task>? runOnMainThreadAsync = null,
        Func<Func<Task>, Task>? runOffMainThreadAsync = null,
        Func<string, string, Task>? showWarningAsync = null,
        Func<string, string, Task>? showSuccessAsync = null,
        Func<string, string, Task>? showErrorAsync = null,
        Func<string, string, string, string, Task<bool>>? showConfirmAsync = null) {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _runOnMainThreadAsync = runOnMainThreadAsync ?? MauiThreading.RunOnMainThreadAsync;
        _runOffMainThreadAsync = runOffMainThreadAsync ?? (action => MauiThreading.RunOffMainThreadAsync(action));
        _showWarningAsync = showWarningAsync ?? ErrorPresenter.ShowWarningAsync;
        _showSuccessAsync = showSuccessAsync ?? ErrorPresenter.ShowSuccessAsync;
        _showErrorAsync = showErrorAsync ?? ErrorPresenter.ShowErrorAsync;
        _showConfirmAsync = showConfirmAsync ?? ErrorPresenter.ShowConfirmAsync;
    }

    public string SummaryText => TotalCount <= 0
        ? "No rows loaded"
        : $"Showing {Rows.Count} of {TotalCount} rows";

    [RelayCommand]
    internal Task InitializeAsync() => LoadRowsAsync();

    [RelayCommand]
    internal Task RefreshAsync() => LoadRowsAsync();

    [RelayCommand]
    internal Task SearchAsync() => LoadRowsAsync();

    [RelayCommand]
    internal async Task ApproveAsync(UserManagementRowViewModel? row) {
        if (row is null || string.IsNullOrWhiteSpace(row.AccessRequestId)) {
            return;
        }

        await ExecuteRowActionAsync(
            row,
            async () => await _apiClient.ApproveAccessRequestAsync(
                row.AccessRequestId,
                new ApproveAccessRequestDto {
                    Tier = row.SelectedTier,
                    ExpectedRowVersion = row.RowVersion
                }).ConfigureAwait(false),
            "Access approved.").ConfigureAwait(false);
    }

    [RelayCommand]
    internal async Task RetryOnboardingAsync(UserManagementRowViewModel? row) {
        if (row is null || string.IsNullOrWhiteSpace(row.AccessRequestId)) {
            return;
        }

        await ExecuteRowActionAsync(
            row,
            async () => await _apiClient.RetryAccessRequestOnboardingAsync(
                row.AccessRequestId,
                new RetryOnboardingDto {
                    ExpectedRowVersion = row.RowVersion
                }).ConfigureAwait(false),
            "Onboarding retry queued.").ConfigureAwait(false);
    }

    [RelayCommand]
    internal async Task ChangeTierAsync(UserManagementRowViewModel? row) {
        if (row is null || string.IsNullOrWhiteSpace(row.ManagedUserId)) {
            return;
        }

        await ExecuteRowActionAsync(
            row,
            async () => await _apiClient.ChangeManagedUserTierAsync(
                row.ManagedUserId,
                new ChangeManagedUserTierDto {
                    Tier = row.SelectedTier,
                    ExpectedRowVersion = row.RowVersion,
                    Reason = NormalizeReason(row.ActionReason)
                }).ConfigureAwait(false),
            "Tier updated.").ConfigureAwait(false);
    }

    [RelayCommand]
    internal async Task CancelAsync(UserManagementRowViewModel? row) {
        if (row is null) {
            return;
        }

        if (string.IsNullOrWhiteSpace(row.ActionReason)) {
            await SetErrorAsync("A cancellation reason is required.").ConfigureAwait(false);
            return;
        }

        var confirmed = await _showConfirmAsync(
            "Cancel access",
            $"Cancel access for {row.Email}?",
            "Confirm",
            "Keep").ConfigureAwait(false);
        if (!confirmed) {
            return;
        }

        if (!string.IsNullOrWhiteSpace(row.AccessRequestId)) {
            await ExecuteRowActionAsync(
                row,
                async () => await _apiClient.CancelAccessRequestAsync(
                    row.AccessRequestId,
                    new CancelManagementItemDto {
                        ExpectedRowVersion = row.RowVersion,
                        Reason = row.ActionReason.Trim()
                    }).ConfigureAwait(false),
                "Access request cancelled.").ConfigureAwait(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(row.ManagedUserId)) {
            await ExecuteRowActionAsync(
                row,
                async () => await _apiClient.CancelManagedUserAsync(
                    row.ManagedUserId,
                    new CancelManagementItemDto {
                        ExpectedRowVersion = row.RowVersion,
                        Reason = row.ActionReason.Trim()
                    }).ConfigureAwait(false),
                "Managed user cancelled.").ConfigureAwait(false);
        }
    }

    private async Task LoadRowsAsync() {
        await _runOnMainThreadAsync(() => {
            IsLoading = true;
            ErrorMessage = null;
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        try {
            if (!await EnsureAuthorizedAsync().ConfigureAwait(false)) {
                await _runOnMainThreadAsync(() => {
                    Rows.Clear();
                    TotalCount = 0;
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
                return;
            }

            UserManagementListResponseDto? response = null;
            await _runOffMainThreadAsync(async () => {
                response = await _apiClient.GetUserManagementAsync(
                    search: string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                    page: 1,
                    pageSize: DefaultPageSize).ConfigureAwait(false);
            }).ConfigureAwait(false);

            var result = response ?? new UserManagementListResponseDto();
            await _runOnMainThreadAsync(() => {
                Rows.Clear();
                foreach (var row in result.Rows) {
                    Rows.Add(new UserManagementRowViewModel(row));
                }

                TotalCount = result.TotalCount;
                OnPropertyChanged(nameof(SummaryText));
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
        catch (Exception ex) {
            var sanitized = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            _logger.LogError(ex, "Failed to load user-management rows");
            await SetErrorAsync($"Failed to load user management rows: {sanitized}").ConfigureAwait(false);
            await _showErrorAsync("User Management", ErrorMessage ?? "Failed to load user management rows.").ConfigureAwait(false);
        }
        finally {
            await _runOnMainThreadAsync(() => {
                IsLoading = false;
                OnPropertyChanged(nameof(SummaryText));
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
    }

    private async Task ExecuteRowActionAsync(
        UserManagementRowViewModel row,
        Func<Task<UserManagementRowDto>> action,
        string successMessage) {
        await _runOnMainThreadAsync(() => {
            row.IsBusy = true;
            ErrorMessage = null;
            return Task.CompletedTask;
        }).ConfigureAwait(false);

        try {
            UserManagementRowDto? updatedRow = null;
            await _runOffMainThreadAsync(async () => {
                updatedRow = await action().ConfigureAwait(false);
            }).ConfigureAwait(false);

            if (updatedRow is null) {
                return;
            }

            await _runOnMainThreadAsync(() => {
                row.Apply(updatedRow);
                row.ActionReason = string.Empty;
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            await _showSuccessAsync("User Management", successMessage).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict) {
            var sanitized = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            _logger.LogWarning(ex, "User-management action conflicted for row {RowId}", row.RowId);

            await LoadRowsAsync().ConfigureAwait(false);
            await SetErrorAsync(sanitized).ConfigureAwait(false);
            await _showErrorAsync("User Management", ErrorMessage ?? sanitized).ConfigureAwait(false);
        }
        catch (Exception ex) {
            var sanitized = ErrorPresenter.SanitizeErrorMessage(ex.Message);
            _logger.LogWarning(ex, "User-management action failed for row {RowId}", row.RowId);
            await SetErrorAsync(sanitized).ConfigureAwait(false);
            await _showErrorAsync("User Management", ErrorMessage ?? sanitized).ConfigureAwait(false);
        }
        finally {
            await _runOnMainThreadAsync(() => {
                row.IsBusy = false;
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }
    }

    private async Task<bool> EnsureAuthorizedAsync() {
        if (!_configService.IsApiConfigured) {
            _logger.LogWarning("User management blocked: API not configured");
            await _showWarningAsync(
                "Configuration required",
                "API is not configured. Go to Settings to configure the API base URL.").ConfigureAwait(false);
            return false;
        }

        if (!_authService.IsSignedIn()) {
            _logger.LogWarning("User management blocked: user not signed in");
            await _showWarningAsync(
                "Sign in required",
                "Please sign in to manage requests and users.").ConfigureAwait(false);
            return false;
        }

        var isAuthorized = await _authService.IsAuthorizedAdminAsync().ConfigureAwait(false);
        if (!isAuthorized) {
            _logger.LogWarning("User management blocked: user lacks admin permissions");
            await _showWarningAsync(
                "Access denied",
                "You do not have permission to manage requests and users.").ConfigureAwait(false);
            return false;
        }

        return true;
    }

    private async Task SetErrorAsync(string message) {
        await _runOnMainThreadAsync(() => {
            ErrorMessage = message;
            OnPropertyChanged(nameof(SummaryText));
            return Task.CompletedTask;
        }).ConfigureAwait(false);
    }

    private static string? NormalizeReason(string reason) {
        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}