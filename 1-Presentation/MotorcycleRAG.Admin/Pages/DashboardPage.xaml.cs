using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin.Pages;

public partial class DashboardPage : ContentPage
{
    private readonly INavigationService _navigationService;

    public DashboardPage(INavigationService navigationService)
    {
        InitializeComponent();
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    }
}
