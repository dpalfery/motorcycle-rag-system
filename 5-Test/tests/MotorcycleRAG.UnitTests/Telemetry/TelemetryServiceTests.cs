using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Core.Options; 
using MotorcycleRAG.Persistence.Telemetry;
using System.Collections.Concurrent;
using FluentAssertions;


namespace MotorcycleRAG.UnitTests.Telemetry;

public class TelemetryServiceTests : IDisposable
{
    private readonly StubTelemetryChannel _channel;
    private readonly TelemetryClient _client;
    private readonly Mock<ICorrelationService> _mockCorrelation;
    private readonly Mock<ILogger<TelemetryService>> _mockLogger;
    private readonly ITelemetryService _service;

    public TelemetryServiceTests()
    {
        _channel = new StubTelemetryChannel();
        var aiConfig = new Microsoft.ApplicationInsights.Extensibility.TelemetryConfiguration
        {
            TelemetryChannel = _channel,
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000"
        };
        _client = new TelemetryClient(aiConfig);
        // Do not dispose TelemetryConfiguration here; TelemetryClient depends on it for the test lifetime.
        _mockCorrelation = new Mock<ICorrelationService>();
        _mockLogger = new Mock<ILogger<TelemetryService>>();
        _mockCorrelation.Setup(c => c.GetOrCreateCorrelationId()).Returns("corr-test");

        // Create options for telemetryConfig (domain model) and sqlOptions


        _service = new TelemetryService(_client, _mockLogger.Object);
    }

    [Fact]
    public void TrackQuery_ShouldSendTelemetryEvent()
    {
        // Act
        _service.TrackQuery("query1", "tell me about bikes", TimeSpan.FromMilliseconds(123), 5, 0.002m);

        // Assert
        var ev = _channel.Telemetries.OfType<Microsoft.ApplicationInsights.DataContracts.EventTelemetry>().Single();
        ev.Name.Should().Be("QueryExecuted");
        ev.Properties["queryId"].Should().Be("query1");
        ev.Properties["redactedQuery"].Should().Be("tell me about bikes");
        ev.Properties["resultsCount"].Should().Be("5");
        ev.Properties["estimatedCost"].Should().Be("0.0020");
    }

    [Fact]
    public void TrackDegradedMode_ShouldSendDegradedModeEvent()
    {
        // Arrange
        var failedSources = new List<string> { "WebSearch", "PDFSearch" };
        var availableSources = new List<string> { "VectorSearch" };
        var duration = TimeSpan.FromMilliseconds(500);
        var resultsFound = 3;

        // Act
        _service.TrackDegradedMode("corr-123", failedSources, availableSources, duration, resultsFound);

        // Assert
        var ev = _channel.Telemetries.OfType<Microsoft.ApplicationInsights.DataContracts.EventTelemetry>().Single();
        ev.Name.Should().Be("SearchDegradedMode");
        ev.Properties["CorrelationId"].Should().Be("corr-123");
        ev.Properties["FailedSources"].Should().Be("WebSearch,PDFSearch");
        ev.Properties["AvailableSources"].Should().Be("VectorSearch");
        ev.Properties["FailureCount"].Should().Be("2");
        ev.Properties["AvailableSourceCount"].Should().Be("1");
        ev.Metrics["DurationMs"].Should().Be(500d);
        ev.Metrics["ResultsFound"].Should().Be(3d);
    }

    [Fact]
    public void TrackDegradedMode_WithNullFailedSources_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => _service.TrackDegradedMode("corr-123", null!, new List<string> { "VectorSearch" }, TimeSpan.Zero, 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TrackDegradedMode_WithNullAvailableSources_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => _service.TrackDegradedMode("corr-123", new List<string> { "WebSearch" }, null!, TimeSpan.Zero, 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TrackDegradedMode_WithEmptyCorrelationId_ShouldThrowArgumentException()
    {
        // Act & Assert
        var act = () => _service.TrackDegradedMode(string.Empty, new List<string>(), new List<string>(), TimeSpan.Zero, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackSourceFailure_ShouldSendSourceFailureEvent()
    {
        // Arrange
        var duration = TimeSpan.FromMilliseconds(250);

        // Act
        _service.TrackSourceFailure("corr-456", "WebSearch", "Connection timeout", duration);

        // Assert
        var ev = _channel.Telemetries.OfType<Microsoft.ApplicationInsights.DataContracts.EventTelemetry>().Single();
        ev.Name.Should().Be("SourceFailure");
        ev.Properties["CorrelationId"].Should().Be("corr-456");
        ev.Properties["SourceName"].Should().Be("WebSearch");
        ev.Properties["ErrorMessage"].Should().NotBeNullOrEmpty();
        ev.Metrics["DurationMs"].Should().Be(250d);
    }

    [Fact]
    public void TrackSourceFailure_WithEmptySourceName_ShouldThrowArgumentException()
    {
        // Act & Assert
        var act = () => _service.TrackSourceFailure("corr-456", string.Empty, "Error", TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackSearchExecution_ShouldSendSearchExecutionEvent()
    {
        // Arrange
        var duration = TimeSpan.FromMilliseconds(1000);

        // Act
        _service.TrackSearchExecution("corr-789", "query-1", duration, 10, 2, 1, true);

        // Assert
        var ev = _channel.Telemetries.OfType<Microsoft.ApplicationInsights.DataContracts.EventTelemetry>().Single();
        ev.Name.Should().Be("SearchExecution");
        ev.Properties["CorrelationId"].Should().Be("corr-789");
        ev.Properties["QueryId"].Should().Be("query-1");
        ev.Properties["SuccessfulSources"].Should().Be("2");
        ev.Properties["FailedSources"].Should().Be("1");
        ev.Properties["DegradedMode"].Should().Be("True");
        ev.Metrics["TotalDurationMs"].Should().Be(1000d);
        ev.Metrics["TotalResults"].Should().Be(10d);
        ev.Metrics["SourceSuccessRate"].Should().BeApproximately(66.666666d, 0.1d);
    }

    [Fact]
    public void TrackSearchExecution_WithEmptyQueryId_ShouldThrowArgumentException()
    {
        // Act & Assert
        var act = () => _service.TrackSearchExecution("corr-789", string.Empty, TimeSpan.Zero, 0, 0, 0, false);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackSearchExecution_CalculatesSuccessRateCorrectly()
    {
        // Arrange
        var duration = TimeSpan.FromMilliseconds(500);

        // Act - All sources successful
        _service.TrackSearchExecution("corr-test", "query-1", duration, 5, 3, 0, false);

        // Assert
        var ev = _channel.Telemetries.OfType<Microsoft.ApplicationInsights.DataContracts.EventTelemetry>().Single();
        ev.Metrics["SourceSuccessRate"].Should().Be(100d);
    }

    private sealed class StubTelemetryChannel : ITelemetryChannel
    {
        public ConcurrentBag<ITelemetry> Telemetries { get; } = new();
        public void Send(ITelemetry item) => Telemetries.Add(item);
        public void Flush() { }
        public bool? DeveloperMode { get; set; }
        public string? EndpointAddress { get; set; }
        public void Dispose() {
            GC.SuppressFinalize(this);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _channel?.Dispose();
        }
    }
}