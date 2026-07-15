using Microsoft.Extensions.Configuration;
using MotorcycleRAG.API.Configuration;

namespace MotorcycleRAG.UnitTests.Presentation.API.Configuration;

public sealed class AppConfigurationExtensionsCoverageTests
{
    [Fact]
    public async Task EnsureTcpConnectivityAsync_WithDefaultOptionalValues_UsesSystemDefaultsAndSucceeds()
    {
        var calls = 0;

        await AppConfigurationExtensions.EnsureTcpConnectivityAsync(
            "https://unit-test.azconfig.io",
            (_, _) =>
            {
                calls++;
                return Task.CompletedTask;
            },
            timeProvider: null,
            maxAttempts: 1,
            connectTimeout: null,
            retryDelay: null);

        calls.Should().Be(1);
    }
}
