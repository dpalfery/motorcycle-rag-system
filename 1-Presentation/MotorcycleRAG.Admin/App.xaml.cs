using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.Admin.Services;

namespace MotorcycleRAG.Admin;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1812: Avoid uninstantiated internal classes",
    Justification = "Instantiated by MAUI framework")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "S3059:Types should not have members with visibility set higher than the type's visibility",
    Justification = "Internal class has public constructor required by MAUI DI framework")]
internal partial class App : Application {
    public App() {
        InitializeComponent();
        
        // Configure services (DI setup needed)
        // Configure services (DI setup needed)
        var authService = new AdminAuthService(
            clientId: "YOUR_CLIENT_ID",
            authority: "https://login.microsoftonline.com/YOUR_TENANT_ID",
            scopes: new[] { "api://YOUR_API_ID/.default" },
            logger: null);
        MainPage = new AppShell(authService, null, null, null);
    }
}