using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Moq;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Infrastructure.Telemetry;
using System.Collections.Concurrent;
using FluentAssertions;

namespace MotorcycleRAG.UnitTests.Telemetry;

public class TelemetryServiceTests
{
    private readonly StubTelemetryChannel _channel;
    private readonly TelemetryClient _client;
    private readonly Mock<ICorrelationService> _mockCorrelation;
    private readonly ITelemetryService _service;

    public TelemetryServiceTests()
    {
        _channel = new StubTelemetryChannel();
        var config = new TelemetryConfiguration("00000000-0000-0000-0000-000000000000", _channel);
        _client = new TelemetryClient(config);
        _mockCorrelation = new Mock<ICorrelationService>();
        _mockCorrelation.Setup(c => c.GetOrCreateCorrelationId()).Returns("corr-test");
        _service = new TelemetryService(_client, _mockCorrelation.Object);
    }

    [Fact]
    public void TrackQuery_ShouldSendTelemetryEvent()
    {
        // Act
        _service.TrackQuery("query1", "tell me about bikes", TimeSpan.FromMilliseconds(123), 5, 0.002m);

        // Assert
        var ev = _channel.Telemetries.OfType<Microsoft.ApplicationInsights.DataContracts.EventTelemetry>().Single();
        ev.Name.Should().Be("MotorcycleQuery");
        ev.Properties["QueryId"].Should().Be("query1");
        ev.Properties["CorrelationId"].Should().Be("corr-test");
        ev.Metrics["DurationMs"].Should().BeApproximately(123d, 0.0001);
        ev.Metrics["ResultsCount"].Should().Be(5d);
    }

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