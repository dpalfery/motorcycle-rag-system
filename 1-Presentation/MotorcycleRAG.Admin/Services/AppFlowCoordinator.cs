namespace MotorcycleRAG.Admin.Services;

internal sealed class AppFlowCoordinator : IAppFlowCoordinator
{
    private readonly IServiceProvider _serviceProvider;

    public AppFlowCoordinator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public Page CreateLandingRootPage() =>
        new NavigationPage(_serviceProvider.GetRequiredService<Pages.LandingPage>());

    public void ShowLandingPage()
    {
        var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
        if (window == null)
        {
            return;
        }

        window.Page = CreateLandingRootPage();
    }

    public async Task ShowShellAsync()
    {
        var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
        if (window == null)
        {
            return;
        }

        var shell = _serviceProvider.GetRequiredService<AppShell>();
        await shell.RefreshAuthStateAsync().ConfigureAwait(false);
        window.Page = shell;
    }

    public async Task OpenSettingsAsync()
    {
        var navigationPage = Application.Current?.Windows is { Count: > 0 } windows
            ? windows[0].Page as NavigationPage
            : null;

        if (navigationPage != null)
        {
            await navigationPage.Navigation.PushAsync(_serviceProvider.GetRequiredService<Pages.SettingsPage>()).ConfigureAwait(false);
        }
    }
}
