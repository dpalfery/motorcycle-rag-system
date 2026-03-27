namespace MotorcycleRAG.Admin.Services;

internal interface IAppFlowCoordinator
{
    Page CreateLandingRootPage();

    void ShowLandingPage();

    Task ShowShellAsync();

    Task OpenSettingsAsync();
}
