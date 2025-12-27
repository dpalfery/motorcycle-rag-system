using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Options;
using MotorcycleRAG.Persistence.Telemetry;
using System.Collections.Concurrent;
using FluentAssertions;

namespace MotorcycleRAG.UnitTests.Telemetry;

public class TelemetryServiceTests
{
    private readonly StubTelemetryChannel _channel;
    private readonly TelemetryClient _client;
    private readonly Mock<ICorrelationService> _mockCorrelation;
    private readonly Mock<ILogger<TelemetryService>> _mockLogger;
    private readonly ITelemetryService _service;

    public TelemetryServiceTests()
    {
        _channel = new StubTelemetryChannel();
        var config = new TelemetryConfiguration
        {
            TelemetryChannel = _channel,
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000"
        };
        _client = new TelemetryClient(config);
        _mockCorrelation = new Mock<ICorrelationService>();
        _mockLogger = new Mock<ILogger<TelemetryService>>();
        _mockCorrelation.Setup(c => c.GetOrCreateCorrelationId()).Returns("corr-test");
        
        // Create options for telemetryConfig and sqlOptions
        var telemetryOptions = Options.Create(config);
        var sqlOptions = Options.Create(new SqlOptions());
        
        _service = new TelemetryService(_client, _mockLogger.Object, telemetryOptions, sqlOptions);
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

    /* TrackCost test commented out - method not implemented in ITelemetryService interface
    [Fact]
    public void TrackCost_ShouldSendTelemetryEvent()
    {
        // Act
        _service.TrackCost("query1", 0.01m, 500);

        // Assert
        var ev = _channel.Telemetries.OfType<Microsoft.ApplicationInsights.DataContracts.EventTelemetry>().Single(e => e.Name == "QueryCost");
        ev.Properties["QueryId"].Should().Be("query1");
        ev.Metrics["EstimatedCost"].Should().Be(0.01d);
        ev.Metrics["TokensUsed"].Should().Be(500d);
    }
    */

    private sealed class StubTelemetryChannel : ITelemetryChannel
    {
        public ConcurrentBag<ITelemetry> Telemetries { get; } = new();
        public void Send(ITelemetry item) => Telemetries.Add(item);
        public void Flush() { }
        public bool? DeveloperMode { get; set; }
        public string? EndpointAddress { get; set; }
        public void Dispose() { }
    }
}