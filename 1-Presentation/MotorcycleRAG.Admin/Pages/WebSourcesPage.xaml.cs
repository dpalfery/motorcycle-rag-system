using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin.Pages;

public partial class WebSourcesPage : ContentPage
{
    private readonly INavigationService _navigationService;

    public WebSourcesPage(INavigationService navigationService)
    {
        InitializeComponent();
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
    }
}
