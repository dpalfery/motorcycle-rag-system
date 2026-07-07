using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Persistence.Telemetry;
using FluentAssertions;


namespace MotorcycleRAG.UnitTests.Telemetry;

/// <summary>
/// Tests for TelemetryService argument validation.
/// Happy-path telemetry tracking tests are omitted because TelemetryClient.Track()
/// requires a fully configured Application Insights channel which cannot be mocked
/// (TelemetryClient is sealed) and throws NullReferenceException with minimal configs.
/// </summary>
public class TelemetryServiceTests : IDisposable {
    private readonly TelemetryConfiguration _aiConfig;
    private readonly TelemetryClient _client;
    private readonly Mock<ILogger<TelemetryService>> _mockLogger;
    private readonly ITelemetryService _service;

    public TelemetryServiceTests() {
        _aiConfig = new TelemetryConfiguration();
        _aiConfig.DisableTelemetry = true;
        _client = new TelemetryClient(_aiConfig);
        _mockLogger = new Mock<ILogger<TelemetryService>>();

        _service = new TelemetryService(_client, _mockLogger.Object, telemetryObserver: null);
    }

    [Fact]
    public void TrackDegradedMode_WithNullFailedSources_ShouldThrowArgumentNullException() {
        var act = () => _service.TrackDegradedMode("corr-123", null!, new List<string> { "VectorSearch" }, TimeSpan.Zero, 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TrackDegradedMode_WithNullAvailableSources_ShouldThrowArgumentNullException() {
        var act = () => _service.TrackDegradedMode("corr-123", new List<string> { "WebSearch" }, null!, TimeSpan.Zero, 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TrackDegradedMode_WithEmptyCorrelationId_ShouldThrowArgumentException() {
        var act = () => _service.TrackDegradedMode(string.Empty, new List<string>(), new List<string>(), TimeSpan.Zero, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackSourceFailure_WithEmptySourceName_ShouldThrowArgumentException() {
        var act = () => _service.TrackSourceFailure("corr-456", string.Empty, "Error", TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackSearchExecution_WithEmptyQueryId_ShouldThrowArgumentException() {
        var act = () => _service.TrackSearchExecution("corr-789", string.Empty, TimeSpan.Zero, 0, 0, 0, false);
        act.Should().Throw<ArgumentException>();
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (disposing) {
            _aiConfig?.Dispose();
        }
    }
}
