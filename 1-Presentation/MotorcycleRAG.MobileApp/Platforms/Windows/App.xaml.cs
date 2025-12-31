using Microsoft.UI.Xaml;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace MotorcycleRAG.MobileApp.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        this.InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
