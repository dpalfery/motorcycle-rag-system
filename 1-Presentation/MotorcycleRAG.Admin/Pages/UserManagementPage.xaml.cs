using Microsoft.Extensions.Logging;
using MotorcycleRAG.Admin.ViewModels;

namespace MotorcycleRAG.Admin.Pages;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "Instantiated by MAUI DI")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "S3059:Types should not have members with visibility set higher than the type's visibility", Justification = "Public constructor required by MAUI DI")]
internal partial class UserManagementPage : ContentPage {
    private readonly UserManagementViewModel _viewModel;
    private readonly ILogger<UserManagementPage> _logger;

    public UserManagementPage(UserManagementViewModel viewModel, ILogger<UserManagementPage> logger) {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing() {
        base.OnAppearing();

        try {
            await _viewModel.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to initialize User Management page");
            await Utilities.ErrorPresenter.ShowErrorAsync(
                "Initialization Error",
                "Failed to load user-management data. Check API and admin authentication settings.").ConfigureAwait(false);
        }
    }
}