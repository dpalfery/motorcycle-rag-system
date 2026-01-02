using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin.Pages;

public partial class ToolsPage : ContentPage
{
    private readonly INavigationService _navigationService;

    public ToolsPage(INavigationService navigationService)
    {
        InitializeComponent();
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    }
}
