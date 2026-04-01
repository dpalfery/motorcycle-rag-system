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
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
            if (window == null)
            {
                return;
            }

            window.Page = CreateLandingRootPage();
        });
    }

    public async Task ShowShellAsync()
    {
        var shell = _serviceProvider.GetRequiredService<AppShell>();

        // Refresh auth state first — it dispatches to the main thread internally.
        // Awaiting it here (before entering InvokeOnMainThreadAsync) avoids nesting
        // an async lambda inside a main-thread dispatch, which can deadlock.
        await shell.RefreshAuthStateAsync();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var window = Application.Current?.Windows is { Count: > 0 } windows ? windows[0] : null;
            if (window == null)
            {
                return;
            }

            window.Page = shell;
        });
    }

    public async Task OpenSettingsAsync()
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var navigationPage = Application.Current?.Windows is { Count: > 0 } windows
                ? windows[0].Page as NavigationPage
                : null;

            if (navigationPage != null)
            {
                await navigationPage.Navigation.PushAsync(_serviceProvider.GetRequiredService<Pages.SettingsPage>());
            }
        });
    }
}
