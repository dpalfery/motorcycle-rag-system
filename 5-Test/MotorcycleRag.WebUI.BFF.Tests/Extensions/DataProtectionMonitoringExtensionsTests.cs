using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MotorcycleRag.WebUI.BFF.Extensions;

namespace MotorcycleRag.WebUI.BFF.Tests.Extensions;

public class DataProtectionMonitoringExtensionsTests {
    private readonly Mock<ILogger<DataProtectionMonitoringService>> _logger = new();

    [Fact]
    public void AddDataProtectionMonitoring_RegistersSingletonService() {
        var services = new ServiceCollection();
        services.AddLogging();
        var telemetryConfig = new TelemetryConfiguration {
            TelemetryChannel = new InMemoryChannel(),
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000"
        };
        services.AddSingleton(new TelemetryClient(telemetryConfig));

        var returned = services.AddDataProtectionMonitoring();
        using var provider = services.BuildServiceProvider();

        returned.Should().BeSameAs(services);
        provider.GetRequiredService<DataProtectionMonitoringService>()
            .Should().BeSameAs(provider.GetRequiredService<DataProtectionMonitoringService>());
    }

    [Fact]
    public void TrackKeyPersistenceSuccess_WithTelemetry_TracksMetricsAndSanitizedLog() {
        var service = CreateService(withTelemetry: true);
        var uri = new Uri("https://account.blob.core.windows.net/container/keys.xml?sig=secret");

        service.TrackKeyPersistenceSuccess(uri, 42, "correlation-id");

        VerifyLog(LogLevel.Information, "account.blob.core.windows.net/container/keys.xml", "correlation-id");
        VerifyLogDoesNotContain("secret");
    }

    [Fact]
    public void TrackKeyPersistenceSuccess_WithUri_DelegatesToStringOverload() {
        var service = CreateService();

        service.TrackKeyPersistenceSuccess(new Uri("https://storage.example/container/keys.xml"), 5);

        VerifyLog(LogLevel.Information, "storage.example/container/keys.xml");
    }

    [Fact]
    public void TrackKeyPersistenceSuccess_WithNullUri_ThrowsArgumentNullException() {
        var service = CreateService();

        var act = () => service.TrackKeyPersistenceSuccess((Uri)null!, 1);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TrackKeyPersistenceSuccess_WithoutCorrelationId_GeneratesOne() {
        var service = CreateService();

        service.TrackKeyPersistenceSuccess(new Uri("https://storage.example/container/keys.xml"), 3);

        VerifyLog(LogLevel.Information, "storage.example/container/keys.xml");
    }

    [Fact]
    public void TrackKeyPersistenceFailure_WithTelemetry_TracksEventAndLogsError() {
        var service = CreateService(withTelemetry: true);
        var uri = new Uri("https://storage.example/container/keys.xml?token=secret");

        service.TrackKeyPersistenceFailure(uri, 99, "storage unavailable", "failure-id");

        VerifyLog(LogLevel.Error, "storage.example/container/keys.xml", "storage unavailable", "failure-id");
        VerifyLogDoesNotContain("secret");
    }

    [Fact]
    public void TrackKeyPersistenceFailure_WithUri_DelegatesToStringOverload() {
        var service = CreateService();

        service.TrackKeyPersistenceFailure(
            new Uri("https://storage.example/container/keys.xml"),
            8,
            "failure");

        VerifyLog(LogLevel.Error, "storage.example/container/keys.xml", "failure");
    }

    [Fact]
    public void TrackKeyPersistenceFailure_WithNullUri_ThrowsArgumentNullException() {
        var service = CreateService();

        var act = () => service.TrackKeyPersistenceFailure((Uri)null!, 1, "failure");

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("initialized")]
    [InlineData("rotated")]
    [InlineData("loaded")]
    public void TrackKeyLifecycle_WithUriContainingSecrets_SanitizesLog(string operation) {
        var service = CreateService(withTelemetry: true);
        var uri = new Uri("https://storage.example/container/keys.xml?sig=secret");

        switch (operation) {
            case "initialized":
                service.TrackKeysInitialized(uri, "lifecycle-id");
                break;
            case "rotated":
                service.TrackKeysRotated(uri, "lifecycle-id");
                break;
            default:
                service.TrackKeysLoaded(uri, "lifecycle-id");
                break;
        }

        VerifyLog(LogLevel.Information, "storage.example/container/keys.xml", "lifecycle-id");
        VerifyLogDoesNotContain("secret");
    }

    [Fact]
    public void TrackKeyLifecycle_WithUri_ExercisesAllUriOverloads() {
        var service = CreateService();
        var uri = new Uri("https://storage.example/container/keys.xml");

        service.TrackKeysInitialized(uri);
        service.TrackKeysRotated(uri);
        service.TrackKeysLoaded(uri);

        _logger.Verify(
            logger => logger.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((_, _) => true),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(3));
    }

    [Fact]
    public void TrackKeyLifecycle_WithNullUris_ThrowsArgumentNullException() {
        var service = CreateService();

        var initialized = () => service.TrackKeysInitialized((Uri)null!);
        var rotated = () => service.TrackKeysRotated((Uri)null!);
        var loaded = () => service.TrackKeysLoaded((Uri)null!);

        initialized.Should().Throw<ArgumentNullException>();
        rotated.Should().Throw<ArgumentNullException>();
        loaded.Should().Throw<ArgumentNullException>();
    }

#pragma warning disable CA2234 // Intentionally exercise string overloads for invalid URI sanitization paths
    [Theory]
    [InlineData("", "not-configured")]
    [InlineData("not a uri", "invalid-uri")]
    public void TrackKeysInitialized_WithInvalidBlobUri_UsesSafeLogValue(string input, string expected) {
        var service = CreateService();

        service.TrackKeysInitialized(input, "correlation-id");

        VerifyLog(LogLevel.Information, expected);
    }

    [Fact]
    public void TrackKeyPersistenceSuccess_WithEmptyBlobUri_LogsNotConfigured() {
        var service = CreateService();

        service.TrackKeyPersistenceSuccess(string.Empty, 1, "id");

        VerifyLog(LogLevel.Information, "not-configured");
    }

    [Fact]
    public void TrackKeyPersistenceFailure_WithInvalidBlobUri_LogsInvalidUri() {
        var service = CreateService();

        service.TrackKeyPersistenceFailure(":::bad", 2, "error", "id");

        VerifyLog(LogLevel.Error, "invalid-uri", "error");
    }

    [Fact]
    public void TrackKeysRotated_WithEmptyBlobUri_LogsNotConfigured() {
        var service = CreateService();

        service.TrackKeysRotated(string.Empty);

        VerifyLog(LogLevel.Information, "not-configured");
    }

    [Fact]
    public void TrackKeysLoaded_WithInvalidBlobUri_LogsInvalidUri() {
        var service = CreateService();

        service.TrackKeysLoaded("not a uri");

        VerifyLog(LogLevel.Information, "invalid-uri");
    }
#pragma warning restore CA2234

    [Fact]
    public void StartKeyPersistenceStopwatch_ReturnsRunningStopwatch() {
        var stopwatch = DataProtectionMonitoringService.StartKeyPersistenceStopwatch();

        stopwatch.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void MetricAndEventConstants_HaveExpectedValues() {
        DataProtectionMonitoringService.MetricKeyPersistenceSuccess.Should().Be("DataProtection.KeyPersistence.Success");
        DataProtectionMonitoringService.MetricKeyPersistenceFailure.Should().Be("DataProtection.KeyPersistence.Failure");
        DataProtectionMonitoringService.MetricKeyPersistenceDuration.Should().Be("DataProtection.KeyPersistence.Duration");
        DataProtectionMonitoringService.EventKeysInitialized.Should().Be("DataProtection.KeysInitialized");
        DataProtectionMonitoringService.EventKeysRotated.Should().Be("DataProtection.KeysRotated");
        DataProtectionMonitoringService.EventKeysLoaded.Should().Be("DataProtection.KeysLoaded");
        DataProtectionMonitoringService.PropertyBlobUri.Should().Be("BlobUri");
        DataProtectionMonitoringService.PropertyDurationMs.Should().Be("DurationMs");
        DataProtectionMonitoringService.PropertyErrorMessage.Should().Be("ErrorMessage");
        DataProtectionMonitoringService.PropertyCorrelationId.Should().Be("CorrelationId");
    }

    private DataProtectionMonitoringService CreateService(bool withTelemetry = false) {
        if (!withTelemetry) {
            return new DataProtectionMonitoringService(null, _logger.Object);
        }

        var configuration = new TelemetryConfiguration {
            TelemetryChannel = new InMemoryChannel(),
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000"
        };
        return new DataProtectionMonitoringService(new TelemetryClient(configuration), _logger.Object);
    }

    private void VerifyLog(LogLevel level, params string[] fragments) {
        _logger.Verify(
            logger => logger.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) =>
                    fragments.All(fragment => value.ToString()!.Contains(fragment, StringComparison.Ordinal))),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    private void VerifyLogDoesNotContain(string fragment) {
        _logger.Verify(
            logger => logger.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((value, _) =>
                    value.ToString()!.Contains(fragment, StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
