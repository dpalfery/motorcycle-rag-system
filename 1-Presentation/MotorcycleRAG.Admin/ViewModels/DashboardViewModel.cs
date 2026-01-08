using System.Windows.Input;
using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin.ViewModels;

/// <summary>
/// ViewModel for the admin dashboard page.
/// Handles navigation commands and displays system overview information.
/// </summary>
public class DashboardViewModel {
    private readonly INavigationService _navigationService;

    /// <summary>Command to navigate to the upload page</summary>
    internal ICommand NavigateToUploadCommand { get; }

    /// <summary>Command to navigate to the jobs page</summary>
    internal ICommand NavigateToJobsCommand { get; }

    /// <summary>Command to navigate to web sources page</summary>
    internal ICommand NavigateToWebSourcesCommand { get; }

    /// <summary>Command to navigate to the tools page</summary>
    internal ICommand NavigateToToolsCommand { get; }

    public DashboardViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));

        // Initialize navigation commands
        NavigateToUploadCommand = new Command(async () => await _navigationService.NavigateToAsync("uploadpage"));
        NavigateToJobsCommand = new Command(async () => await _navigationService.NavigateToAsync("jobspage"));
        NavigateToWebSourcesCommand = new Command(async () => await _navigationService.NavigateToAsync("websourcespage"));
        NavigateToToolsCommand = new Command(async () => await _navigationService.NavigateToAsync("toolspage"));
    }
}


