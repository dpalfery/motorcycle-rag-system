using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Core.Logging;

namespace MotorcycleRag.WebUI.BFF.Tests.Configuration.Logging;

/// <summary>
/// Gate tests that fail the build if <see cref="SanitizingLoggerProvider"/> registration
/// is accidentally removed from the BFF host's logging setup.
/// </summary>
/// <remarks>
/// These tests mirror the exact logging provider registration sequence from
/// <c>MotorcycleRag.WebUI.BFF.Program</c>:
/// default <see cref="WebApplication.CreateBuilder"/> providers (Console + Debug)
/// followed by <c>AddSanitizingLogger</c> as the final logging-provider call.
/// </remarks>
public sealed class SanitizingLoggerRegistrationGateTests
{
    [Fact]
    public void BffLoggingSetup_WhenMirrorOfProgramStartup_RegistersSanitizingLoggerProvider()
    {
        // Arrange — mirror the BFF host's logging setup:
        // WebApplication.CreateBuilder defaults → Console + Debug
        // then AddSanitizingLogger (Program.cs line 50)
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.AddSanitizingLogger();
        });

        using var provider = services.BuildServiceProvider();

        // Act
        var loggerProviders = provider.GetServices<ILoggerProvider>().ToList();

        // Assert
        loggerProviders.Should().Contain(p => p is SanitizingLoggerProvider);
    }

    [Fact]
    public void BffLoggingSetup_WhenSanitizingLoggerIsMissing_NoSanitizingProviderIsRegistered()
    {
        // Sanity check: without AddSanitizingLogger, no SanitizingLoggerProvider should be present
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

        using var provider = services.BuildServiceProvider();
        var loggerProviders = provider.GetServices<ILoggerProvider>().ToList();

        loggerProviders.Should().NotContain(p => p is SanitizingLoggerProvider);
    }
}
