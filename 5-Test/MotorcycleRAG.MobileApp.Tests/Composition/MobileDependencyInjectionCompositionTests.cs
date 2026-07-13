using FluentAssertions;
using System.Text.RegularExpressions;

namespace MotorcycleRAG.MobileApp.Tests.Composition;

/// <summary>
/// Source-level composition guards used while the Mac Catalyst test host is unavailable.
/// </summary>
public sealed class MobileDependencyInjectionCompositionTests
{
    [Fact]
    public void MauiProgram_RegistersPublicClientAndAuthenticationServiceAsSingletons()
    {
        var source = ReadRepositoryFile("1-Presentation", "MotorcycleRAG.MobileApp", "MauiProgram.cs");

        source.Should().MatchRegex(@"AddSingleton<IPublicClientApplication>\s*\(", RegexOptions.CultureInvariant);
        source.Should().MatchRegex(@"AddSingleton<IAuthenticationService\s*,\s*AuthenticationService>\s*\(\)", RegexOptions.CultureInvariant);
        source.Should().Contain("AddOptions<AuthenticationOptions>().ValidateOnStart()");
    }

    [Fact]
    public void App_UsesConstructorInjectedAppShellWithoutAServiceProvider()
    {
        var source = ReadRepositoryFile("1-Presentation", "MotorcycleRAG.MobileApp", "App.xaml.cs");

        source.Should().MatchRegex(@"App\s*\(\s*AppShell\s+appShell\s*\)", RegexOptions.CultureInvariant);
        source.Should().Contain("new Window(_appShell)");
        source.Should().NotContain("IServiceProvider");
        source.Should().NotContain("GetRequiredService");
    }

    [Fact]
    public void MauiProgram_RegistersPdfRendererAndViewerAsTransients()
    {
        var source = ReadRepositoryFile("1-Presentation", "MotorcycleRAG.MobileApp", "MauiProgram.cs");

        source.Should().Contain("AddTransient<IPdfRenderer, PdfRenderer>()");
        source.Should().Contain("AddTransient<IPdfViewerService, PdfViewerService>()");
        source.Should().NotContain("AddSingleton<IPdfViewerService, PdfViewerService>()");
    }

    private static string ReadRepositoryFile(params string[] relativePath)
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MotorcycleRAG.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must execute from a MotorcycleRAG checkout");
        return File.ReadAllText(Path.Combine(directory!.FullName, Path.Combine(relativePath)));
    }
}
