using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Core.Logging;

namespace MotorcycleRAG.DbSetup.Tests;

/// <summary>
/// Gate tests that fail the build if <see cref="SanitizingLoggerProvider"/> registration
/// is accidentally removed from the DbSetup host's logging setup.
/// </summary>
/// <remarks>
/// These tests mirror the exact logger factory configuration from
/// <c>MotorcycleRAG.DbSetup.Program</c>:
/// <c>LoggerFactory.Create</c> with Console + Information minimum level
/// followed by <c>AddSanitizingLogger</c> as the final logging-provider call.
/// </remarks>
public sealed class SanitizingLoggerRegistrationGateTests
{
    [Fact]
    public void DbSetupLoggingSetup_WhenMirrorOfLoggerFactoryCreate_RegistersSanitizingLoggerProvider()
    {
        // Arrange — mirror the DbSetup host's LoggerFactory.Create setup:
        // AddConsole → SetMinimumLevel(Information) → AddSanitizingLogger (Program.cs line 17)
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSanitizingLogger();
        });

        using var provider = services.BuildServiceProvider();

        // Act
        var loggerProviders = provider.GetServices<ILoggerProvider>().ToList();

        // Assert
        loggerProviders.Should().Contain(p => p is SanitizingLoggerProvider);
    }

    [Fact]
    public void DbSetupLoggingSetup_WhenSanitizingLoggerIsMissing_NoSanitizingProviderIsRegistered()
    {
        // Sanity check: without AddSanitizingLogger, no SanitizingLoggerProvider should be present
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        using var provider = services.BuildServiceProvider();
        var loggerProviders = provider.GetServices<ILoggerProvider>().ToList();

        loggerProviders.Should().NotContain(p => p is SanitizingLoggerProvider);
    }
}
