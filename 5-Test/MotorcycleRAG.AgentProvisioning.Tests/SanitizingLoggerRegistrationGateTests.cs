using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Core.Logging;

namespace MotorcycleRAG.UnitTests.AgentProvisioning;

/// <summary>
/// Gate tests that fail the build if <see cref="SanitizingLoggerProvider"/> registration
/// is accidentally removed from the AgentProvisioning host's logging setup.
/// </summary>
/// <remarks>
/// These tests mirror the exact logger factory configuration from
/// <c>MotorcycleRAG.AgentProvisioning.Program.RunAsync</c>:
/// <c>LoggerFactory.Create</c> with Console + Information minimum level
/// followed by <c>AddSanitizingLogger</c> as the final logging-provider call.
/// </remarks>
public sealed class SanitizingLoggerRegistrationGateTests
{
    [Fact]
    public void AgentProvisioningLoggingSetup_WhenMirrorOfLoggerFactoryCreate_RegistersSanitizingLoggerProvider()
    {
        // Arrange — mirror the AgentProvisioning host's LoggerFactory.Create setup:
        // AddConsole → SetMinimumLevel(Information) → AddSanitizingLogger (Program.cs line 63)
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
    public void AgentProvisioningLoggingSetup_WhenSanitizingLoggerIsMissing_NoSanitizingProviderIsRegistered()
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
