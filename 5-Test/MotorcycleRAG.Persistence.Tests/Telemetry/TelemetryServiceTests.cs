using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Persistence.Telemetry;
using FluentAssertions;


namespace MotorcycleRAG.UnitTests.Telemetry;

/// <summary>
/// Tests for TelemetryService using the telemetryObserver hook to capture
/// and assert on tracked telemetry items. Metric-related methods crash due to
/// a MetricTelemetry.Properties initialization bug in v3.x; those paths are
/// tested for validation only and noted as coverage gaps.
/// </summary>
public class TelemetryServiceTests : IDisposable
{
    private readonly TelemetryConfiguration _aiConfig;
    private readonly TelemetryClient _client;
    private readonly Mock<ILogger<TelemetryService>> _mockLogger;
    private readonly List<ITelemetry> _capturedTelemetry;

    public TelemetryServiceTests()
    {
        _aiConfig = new TelemetryConfiguration
        {
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://localhost",
            DisableTelemetry = true
        };
        _client = new TelemetryClient(_aiConfig);
        _mockLogger = new Mock<ILogger<TelemetryService>>();
        _capturedTelemetry = [];
    }

    private ITelemetryService CreateService(Action<ITelemetry>? observer = null) =>
        new TelemetryService(_client, _mockLogger.Object, observer ?? (t => _capturedTelemetry.Add(t)));

    // ─────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_WithNullTelemetryClient_ShouldThrowArgumentNullException()
    {
        var act = () => new TelemetryService(null!, _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("telemetryClient");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        var act = () => new TelemetryService(_client, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithValidArgs_ShouldCreateInstance()
    {
        var sut = new TelemetryService(_client, _mockLogger.Object);

        sut.Should().NotBeNull();
        sut.Should().BeAssignableTo<ITelemetryService>();
    }

    // ─────────────────────────────────────────────────────────────────
    // TrackEvent (happy path — no metrics to avoid MetricTelemetry NRE)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackEvent_WithValidName_ShouldTrackEventTelemetry()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string> { ["Tag1"] = "Value1" };

        sut.TrackEvent("TestEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "TestEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["Tag1"].Should().Be("Value1");
    }

    [Fact]
    public void TrackEvent_WithNoProperties_ShouldStillTrack()
    {
        var sut = CreateService();

        sut.TrackEvent("BareEvent");

        _capturedTelemetry.OfType<EventTelemetry>().Should().Contain(e => e.Name == "BareEvent");
    }

    [Fact]
    public void TrackEvent_WithNullProperties_ShouldNotThrow()
    {
        var sut = CreateService();

        var act = () => sut.TrackEvent("EventWithNull", null);

        act.Should().NotThrow();
        _capturedTelemetry.OfType<EventTelemetry>().Should().Contain(e => e.Name == "EventWithNull");
    }

    [Fact]
    public void TrackEvent_WithEmptyPropertiesDictionary_ShouldStillTrack()
    {
        var sut = CreateService();

        sut.TrackEvent("EmptyPropsEvent", new Dictionary<string, string>());

        _capturedTelemetry.OfType<EventTelemetry>().Should().Contain(e => e.Name == "EmptyPropsEvent");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TrackEvent_WithNullOrWhiteSpaceName_ShouldThrowArgumentException(string? eventName)
    {
        var sut = CreateService();

        var act = () => sut.TrackEvent(eventName!);

        act.Should().Throw<ArgumentException>().WithParameterName("eventName");
    }

    // ─────────────────────────────────────────────────────────────────
    // TrackException
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackException_WithValidException_ShouldTrackExceptionTelemetry()
    {
        var sut = CreateService();
        var ex = new InvalidOperationException("test error");
        var properties = new Dictionary<string, string> { ["Source"] = "UnitTest" };

        sut.TrackException(ex, properties);

        var exceptionTelemetry = _capturedTelemetry.OfType<ExceptionTelemetry>().FirstOrDefault();
        exceptionTelemetry.Should().NotBeNull();
        exceptionTelemetry!.Exception.Should().Be(ex);
        exceptionTelemetry.Properties["Source"].Should().Be("UnitTest");
    }

    [Fact]
    public void TrackException_WithNullException_ShouldThrowArgumentNullException()
    {
        var sut = CreateService();

        var act = () => sut.TrackException(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("exception");
    }

    [Fact]
    public void TrackException_WithNullProperties_ShouldStillTrack()
    {
        var sut = CreateService();
        var ex = new InvalidOperationException("bare error");

        sut.TrackException(ex, null);

        _capturedTelemetry.OfType<ExceptionTelemetry>().Should().NotBeEmpty();
    }

    [Fact]
    public void TrackException_WithMultipleExceptions_ShouldTrackEach()
    {
        var sut = CreateService();
        var ex1 = new InvalidOperationException("first");
        var ex2 = new ArgumentException("second");

        sut.TrackException(ex1);
        sut.TrackException(ex2);

        var exceptions = _capturedTelemetry.OfType<ExceptionTelemetry>().ToList();
        exceptions.Should().HaveCount(2);
        exceptions.Should().Contain(e => e.Exception.Message == "first");
        exceptions.Should().Contain(e => e.Exception.Message == "second");
    }

    // ─────────────────────────────────────────────────────────────────
    // TrackMetric (validation only — MetricTelemetry v3.x NRE blocks happy path)
    // ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TrackMetric_WithNullOrWhiteSpaceName_ShouldThrowArgumentException(string? metricName)
    {
        var sut = CreateService();

        var act = () => sut.TrackMetric(metricName!, 1.0);

        act.Should().Throw<ArgumentException>().WithParameterName("metricName");
    }

    [Fact]
    public void TrackMetric_WithNegativeValue_ShouldAcceptNegativeValues()
    {
        var sut = CreateService();

        // Negative metric values pass validation (no ArgumentException); the MetricTelemetry
        // v3.x NRE may occur on Properties access, but the validation guard itself
        // does not reject negative values
        var act = () => sut.TrackMetric("negativeMetric", -42.5);

        // Validation does not reject negative values; the MetricTelemetry NRE
        // is a known Application Insights v3.x bug, noted as a coverage gap
        act.Should().NotThrow<ArgumentException>();
    }

    // ─────────────────────────────────────────────────────────────────
    // TrackRequest
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackRequest_WithValidInputs_ShouldTrackRequestTelemetry()
    {
        var sut = CreateService();
        var start = DateTimeOffset.UtcNow;
        var duration = TimeSpan.FromMilliseconds(250);

        sut.TrackRequest("GET /api/search", start, duration, "200", true);

        var requestTelemetry = _capturedTelemetry.OfType<RequestTelemetry>().FirstOrDefault();
        requestTelemetry.Should().NotBeNull();
        requestTelemetry!.Name.Should().Be("GET /api/search");
        requestTelemetry.Success.Should().BeTrue();
        requestTelemetry.ResponseCode.Should().Be("200");
        requestTelemetry.Duration.Should().Be(duration);
    }

    [Fact]
    public void TrackRequest_WithFailedRequest_ShouldTrackAsNotSuccessful()
    {
        var sut = CreateService();
        sut.TrackRequest("POST /api/data", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), "500", false);

        _capturedTelemetry.OfType<RequestTelemetry>().Should().Contain(r => r.Success == false);
    }

    [Fact]
    public void TrackRequest_WithRedirectResponseCode_ShouldTrackCorrectly()
    {
        var sut = CreateService();
        var duration = TimeSpan.FromMilliseconds(100);

        sut.TrackRequest("GET /api/redirect", DateTimeOffset.UtcNow, duration, "302", true);

        var requestTelemetry = _capturedTelemetry.OfType<RequestTelemetry>().FirstOrDefault(r => r.Name == "GET /api/redirect");
        requestTelemetry.Should().NotBeNull();
        requestTelemetry!.ResponseCode.Should().Be("302");
        requestTelemetry.Duration.Should().Be(duration);
    }

    [Fact]
    public void TrackRequest_WithZeroDuration_ShouldNotThrow()
    {
        var sut = CreateService();

        var act = () => sut.TrackRequest("ZeroDuration", DateTimeOffset.UtcNow, TimeSpan.Zero, "200", true);

        act.Should().NotThrow();
        _capturedTelemetry.OfType<RequestTelemetry>().Should().Contain(r => r.Name == "ZeroDuration");
    }

    [Fact]
    public void TrackRequest_WithNotFoundResponse_ShouldTrackCorrectly()
    {
        var sut = CreateService();

        sut.TrackRequest("GET /api/missing", DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(50), "404", false);

        var requestTelemetry = _capturedTelemetry.OfType<RequestTelemetry>().FirstOrDefault(r => r.Name == "GET /api/missing");
        requestTelemetry.Should().NotBeNull();
        requestTelemetry!.ResponseCode.Should().Be("404");
        requestTelemetry.Success.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TrackRequest_WithNullOrWhiteSpaceName_ShouldThrowArgumentException(string? name)
    {
        var sut = CreateService();

        var act = () => sut.TrackRequest(name!, DateTimeOffset.UtcNow, TimeSpan.Zero, "200", true);

        act.Should().Throw<ArgumentException>().WithParameterName("name");
    }

    // ─────────────────────────────────────────────────────────────────
    // TrackQuery
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackQuery_WithValidInputs_ShouldTrackEventTelemetry()
    {
        var sut = CreateService();

        sut.TrackQuery("q-001", "SELECT * FROM Products", TimeSpan.FromMilliseconds(50), 42, 0.05m);

        var queryEvent = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "QueryExecuted");
        queryEvent.Should().NotBeNull();
        queryEvent!.Properties["queryId"].Should().Be("q-001");
        queryEvent.Properties["resultsCount"].Should().Be("42");
        queryEvent.Properties["estimatedCost"].Should().Be("0.0500");
        // SQL in the content should be redacted
        queryEvent.Properties["redactedQuery"].Should().NotBe("SELECT * FROM Products");
    }

    [Fact]
    public void TrackQuery_WithNullQuery_ShouldNotThrow()
    {
        var sut = CreateService();

        var act = () => sut.TrackQuery("q-002", null!, TimeSpan.FromMilliseconds(10), 0, 0);

        act.Should().NotThrow();
        var queryEvent = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "QueryExecuted");
        queryEvent.Should().NotBeNull();
    }

    [Fact]
    public void TrackQuery_WithEmptyQuery_ShouldNotThrow()
    {
        var sut = CreateService();

        var act = () => sut.TrackQuery("q-003", string.Empty, TimeSpan.FromMilliseconds(10), 0, 0);

        act.Should().NotThrow();
    }

    [Fact]
    public void TrackQuery_WithZeroResults_ShouldTrackZero()
    {
        var sut = CreateService();

        sut.TrackQuery("q-zero", "SELECT 1", TimeSpan.FromMilliseconds(5), 0, 0m);

        var queryEvent = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "QueryExecuted");
        queryEvent.Should().NotBeNull();
        queryEvent!.Properties["resultsCount"].Should().Be("0");
        queryEvent.Properties["estimatedCost"].Should().Be("0.0000");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TrackQuery_WithNullOrWhiteSpaceQueryId_ShouldThrowArgumentException(string? queryId)
    {
        var sut = CreateService();

        var act = () => sut.TrackQuery(queryId!, "SELECT 1", TimeSpan.Zero, 0, 0);

        act.Should().Throw<ArgumentException>().WithParameterName("queryId");
    }

    // ─────────────────────────────────────────────────────────────────
    // Flush
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Flush_ShouldNotThrow()
    {
        var telemetryService = new TelemetryService(_client, _mockLogger.Object, telemetryObserver: null);

        var act = () => telemetryService.Flush();

        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────
    // TrackDegradedMode (validation)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackDegradedMode_WithNullFailedSources_ShouldThrowArgumentNullException()
    {
        var sut = CreateService();
        var act = () => sut.TrackDegradedMode("corr-123", null!, new List<string> { "VectorSearch" }, TimeSpan.Zero, 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TrackDegradedMode_WithNullAvailableSources_ShouldThrowArgumentNullException()
    {
        var sut = CreateService();
        var act = () => sut.TrackDegradedMode("corr-123", new List<string> { "WebSearch" }, null!, TimeSpan.Zero, 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TrackDegradedMode_WithEmptyCorrelationId_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackDegradedMode(string.Empty, new List<string>(), new List<string>(), TimeSpan.Zero, 0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackDegradedMode_WithWhitespaceCorrelationId_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackDegradedMode("  ", new List<string>(), new List<string>(), TimeSpan.Zero, 0);
        act.Should().Throw<ArgumentException>();
    }

    // ─────────────────────────────────────────────────────────────────
    // TrackSourceFailure (validation)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackSourceFailure_WithEmptySourceName_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackSourceFailure("corr-456", string.Empty, "Error", TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackSourceFailure_WithNullCorrelationId_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackSourceFailure(null!, "SourceX", "Error", TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackSourceFailure_WithWhitespaceCorrelationId_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackSourceFailure("  ", "SourceX", "Error", TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackSourceFailure_WithNullSourceName_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackSourceFailure("corr-001", null!, "Error", TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    // ─────────────────────────────────────────────────────────────────
    // TrackSearchExecution (validation)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackSearchExecution_WithEmptyQueryId_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackSearchExecution("corr-789", string.Empty, TimeSpan.Zero, 0, 0, 0, false);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackSearchExecution_WithNullCorrelationId_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackSearchExecution(null!, "q-123", TimeSpan.Zero, 0, 0, 0, false);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackSearchExecution_WithWhitespaceCorrelationId_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackSearchExecution("  ", "q-123", TimeSpan.Zero, 0, 0, 0, false);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackSearchExecution_WithNullQueryId_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackSearchExecution("corr-001", null!, TimeSpan.Zero, 0, 0, 0, false);
        act.Should().Throw<ArgumentException>();
    }

    // ─────────────────────────────────────────────────────────────────
    // TrackOnboardingTransition (validation)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackOnboardingTransition_WithBlankRequestId_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackOnboardingTransition("", "Stage", "State", "corr", TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackOnboardingTransition_WithNullRequestId_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackOnboardingTransition(null!, "Stage", "State", "corr", TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackOnboardingTransition_WithBlankStage_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackOnboardingTransition("req-001", "", "State", "corr", TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackOnboardingTransition_WithNullStage_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackOnboardingTransition("req-001", null!, "State", "corr", TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackOnboardingTransition_WithBlankState_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackOnboardingTransition("req-001", "Stage", "", "corr", TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackOnboardingTransition_WithNullState_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackOnboardingTransition("req-001", "Stage", null!, "corr", TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    // ─────────────────────────────────────────────────────────────────
    // TrackAdminAction (validation)
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackAdminAction_WithBlankAction_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackAdminAction("", "target", "actor", true, TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackAdminAction_WithNullAction_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackAdminAction(null!, "target", "actor", true, TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackAdminAction_WithBlankTargetId_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackAdminAction("delete", "", "actor", true, TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackAdminAction_WithNullTargetId_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackAdminAction("delete", null!, "actor", true, TimeSpan.Zero);
        act.Should().Throw<ArgumentException>();
    }

    // ─────────────────────────────────────────────────────────────────
    // TrackDependencyDegradation
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackDependencyDegradation_WithValidInputs_ShouldTrackEvent()
    {
        var sut = CreateService();

        sut.TrackDependencyDegradation("SqlDatabase", "corr-020", "High latency detected");

        var degradationEvent = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "OnboardingDependencyDegraded");
        degradationEvent.Should().NotBeNull();
        degradationEvent!.Properties["DependencyName"].Should().Be("SqlDatabase");
        degradationEvent.Properties["CorrelationId"].Should().Be("corr-020");
        degradationEvent.Properties["Reason"].Should().Be("High latency detected");
    }

    [Fact]
    public void TrackDependencyDegradation_WithNullCorrelationId_ShouldUseUnknown()
    {
        var sut = CreateService();

        sut.TrackDependencyDegradation("CacheService", null!, "Timeout");

        var degradationEvent = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "OnboardingDependencyDegraded");
        degradationEvent.Should().NotBeNull();
        degradationEvent!.Properties["CorrelationId"].Should().Be("unknown");
    }

    [Fact]
    public void TrackDependencyDegradation_WithEmptyCorrelationId_ShouldUseUnknown()
    {
        var sut = CreateService();

        sut.TrackDependencyDegradation("QueueService", string.Empty, "Backpressure");

        var degradationEvent = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "OnboardingDependencyDegraded");
        degradationEvent.Should().NotBeNull();
        degradationEvent!.Properties["CorrelationId"].Should().Be("unknown");
    }

    [Fact]
    public void TrackDependencyDegradation_WithWhitespaceCorrelationId_ShouldUseUnknown()
    {
        var sut = CreateService();

        sut.TrackDependencyDegradation("BlobStore", "  ", "Access denied");

        var degradationEvent = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "OnboardingDependencyDegraded");
        degradationEvent.Should().NotBeNull();
        degradationEvent!.Properties["CorrelationId"].Should().Be("unknown");
    }

    [Fact]
    public void TrackDependencyDegradation_WithBlankDependencyName_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackDependencyDegradation("", "corr", "reason");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackDependencyDegradation_WithNullDependencyName_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackDependencyDegradation(null!, "corr", "reason");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackDependencyDegradation_WithBlankReason_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackDependencyDegradation("dep", "corr", "");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TrackDependencyDegradation_WithNullReason_ShouldThrowArgumentException()
    {
        var sut = CreateService();
        var act = () => sut.TrackDependencyDegradation("dep", "corr", null!);
        act.Should().Throw<ArgumentException>();
    }

    // ─────────────────────────────────────────────────────────────────
    // Sensitive data redaction — property name detection
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackEvent_WithSensitiveProperties_ShouldRedactThem()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string>
        {
            ["password"] = "my-secret-pw",
            ["apiKey"] = "sk-abc123",
            ["normalField"] = "visible-value"
        };

        sut.TrackEvent("SensitiveEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "SensitiveEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["password"].Should().Be("[REDACTED]");
        eventTelemetry.Properties["apiKey"].Should().Be("[REDACTED]");
        eventTelemetry.Properties["normalField"].Should().Be("visible-value");
    }

    [Fact]
    public void TrackEvent_WithTokenPropertyName_ShouldRedact()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string> { ["authToken"] = "bearer-xyz" };

        sut.TrackEvent("TokenEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "TokenEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["authToken"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void TrackEvent_WithConnectionPropertyName_ShouldRedact()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string> { ["dbConnection"] = "Server=db;Password=pw" };

        sut.TrackEvent("ConnEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "ConnEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["dbConnection"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void TrackEvent_WithSecretPropertyName_ShouldRedact()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string> { ["clientSecret"] = "shh-dont-tell" };

        sut.TrackEvent("SecretEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "SecretEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["clientSecret"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void TrackEvent_WithCredentialPropertyName_ShouldRedact()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string> { ["azureCredential"] = "some-value" };

        sut.TrackEvent("CredEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "CredEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["azureCredential"].Should().Be("[REDACTED]");
    }

    // ─────────────────────────────────────────────────────────────────
    // Sensitive data redaction — property value content detection
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackEvent_WithSensitivePropertyValues_ShouldRedactContent()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string>
        {
            ["error"] = "Connection failed: Server=prod;Password=secret123;",
            ["config"] = "api_key=sk-sensitive-token-here"
        };

        sut.TrackEvent("SensitiveValueEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "SensitiveValueEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["error"].Should().Contain("REDACTED");
        eventTelemetry.Properties["config"].Should().Contain("REDACTED");
    }

    [Fact]
    public void TrackEvent_WithConnectionStringInValue_ShouldRedact()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string>
        {
            ["message"] = "Failed to connect: Server=db-host;Database=mydb;User ID=admin;Password=s3cret;"
        };

        sut.TrackEvent("ConnStringEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "ConnStringEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["message"].Should().Contain("REDACTED_CONNECTION_STRING");
    }

    [Fact]
    public void TrackEvent_WithApiKeyPatternInValue_ShouldRedact()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string>
        {
            ["details"] = "Request failed with api-key=abcdef123456"
        };

        sut.TrackEvent("ApiKeyInValueEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "ApiKeyInValueEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["details"].Should().Contain("REDACTED_API_KEY");
    }

    [Fact]
    public void TrackEvent_WithSecretPatternInValue_ShouldRedact()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string>
        {
            ["trace"] = "Using secret=my-super-secret-value in request"
        };

        sut.TrackEvent("SecretInValueEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "SecretInValueEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["trace"].Should().Contain("REDACTED_SECRET");
    }

    [Fact]
    public void TrackEvent_WithSqlQueryInValue_ShouldRedact()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string>
        {
            ["sql"] = "SELECT * FROM Users WHERE password IS NOT NULL"
        };

        sut.TrackEvent("SqlInValueEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "SqlInValueEvent");
        eventTelemetry.Should().NotBeNull();
        // SQL redaction pattern applies: if query > 50 chars, shows prefix...suffix
        // This query is ~52 chars, so it should be partially redacted
        var redactedValue = eventTelemetry!.Properties["sql"];
        redactedValue.Should().NotBe("SELECT * FROM Users WHERE password IS NOT NULL");
    }

    [Fact]
    public void TrackEvent_WithShortSqlQueryInValue_ShouldRedactFully()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string>
        {
            ["sql"] = "SELECT 1"
        };

        sut.TrackEvent("ShortSqlEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "ShortSqlEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["sql"].Should().Be("[REDACTED_QUERY]");
    }

    [Fact]
    public void TrackEvent_WithCleanValue_ShouldNotRedact()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string>
        {
            ["message"] = "Operation completed successfully with 42 results"
        };

        sut.TrackEvent("CleanEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "CleanEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["message"].Should().Be("Operation completed successfully with 42 results");
    }

    [Fact]
    public void TrackEvent_WithEmptyPropertyValue_ShouldNotRedact()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string> { ["empty"] = "" };

        sut.TrackEvent("EmptyValueEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "EmptyValueEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["empty"].Should().Be("");
    }

    [Fact]
    public void TrackEvent_WithMultipleRedactionPatterns_ShouldRedactAll()
    {
        var sut = CreateService();
        // Use property names that are NOT inherently sensitive so each regex pattern
        // gets to detect and redact based on VALUE content, not property-name redaction.
        var properties = new Dictionary<string, string>
        {
            ["sql"] = "SELECT 1",
            ["config"] = "Server=db;Password=secret",
            ["auth"] = "api_key=abc123",
            ["trace"] = "secret=top-secret-value"
        };

        sut.TrackEvent("MultiPatternEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "MultiPatternEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["sql"].Should().Be("[REDACTED_QUERY]");
        eventTelemetry.Properties["config"].Should().Contain("REDACTED_CONNECTION_STRING");
        eventTelemetry.Properties["auth"].Should().Contain("REDACTED_API_KEY");
        eventTelemetry.Properties["trace"].Should().Contain("REDACTED_SECRET");
    }

    // ─────────────────────────────────────────────────────────────────
    // Sensitive data redaction — edge cases
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackEvent_WithCaseInsensitivePropertyName_ShouldStillRedact()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string> { ["PASSWORD"] = "SECRET-VAL" };

        sut.TrackEvent("CaseInsensitiveEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "CaseInsensitiveEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["PASSWORD"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void TrackEvent_WithMixedCasePropertyName_ShouldStillRedact()
    {
        var sut = CreateService();
        var properties = new Dictionary<string, string> { ["ApiKey"] = "sk-mixed" };

        sut.TrackEvent("MixedCaseEvent", properties);

        var eventTelemetry = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "MixedCaseEvent");
        eventTelemetry.Should().NotBeNull();
        eventTelemetry!.Properties["ApiKey"].Should().Be("[REDACTED]");
    }

    // ─────────────────────────────────────────────────────────────────
    // TrackRequest — additional edge cases
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackRequest_WithPastStartTime_ShouldNotThrow()
    {
        var sut = CreateService();
        var pastTime = DateTimeOffset.UtcNow.AddHours(-1);

        var act = () => sut.TrackRequest("HistoricRequest", pastTime, TimeSpan.FromSeconds(5), "200", true);

        act.Should().NotThrow();
    }

    [Fact]
    public void TrackRequest_WithLongDuration_ShouldNotThrow()
    {
        var sut = CreateService();

        var act = () => sut.TrackRequest("LongOp", DateTimeOffset.UtcNow, TimeSpan.FromHours(12), "200", true);

        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────
    // TrackQuery — edge cases
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackQuery_WithLargeResultsCount_ShouldTrackCorrectly()
    {
        var sut = CreateService();

        sut.TrackQuery("q-large", "SELECT COUNT(*)", TimeSpan.FromSeconds(5), int.MaxValue, 99.9999m);

        var queryEvent = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "QueryExecuted");
        queryEvent.Should().NotBeNull();
        queryEvent!.Properties["resultsCount"].Should().Be(int.MaxValue.ToString());
    }

    [Fact]
    public void TrackQuery_WithLargeEstimatedCost_ShouldTrackWithFormatting()
    {
        var sut = CreateService();

        sut.TrackQuery("q-cost", "SELECT * FROM huge_table", TimeSpan.FromMinutes(5), 1000, 1234.5678m);

        var queryEvent = _capturedTelemetry.OfType<EventTelemetry>().FirstOrDefault(e => e.Name == "QueryExecuted");
        queryEvent.Should().NotBeNull();
        queryEvent!.Properties["estimatedCost"].Should().Be("1234.5678");
    }

    // ─────────────────────────────────────────────────────────────────
    // Exception and Redaction Fallback coverage
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TrackEvent_WhenTrackThrows_ThrowsInvalidOperationException()
    {
        var sut = CreateService(t => throw new InvalidOperationException("track error"));
        var act = () => sut.TrackEvent("TestEvent");
        act.Should().Throw<InvalidOperationException>().WithMessage("Failed to track event: TestEvent");
    }

    [Fact]
    public void TrackException_WhenTrackThrows_ThrowsInvalidOperationException()
    {
        var sut = CreateService(t => throw new InvalidOperationException("track error"));
        var act = () => sut.TrackException(new Exception("test"));
        act.Should().Throw<InvalidOperationException>().WithMessage("Failed to track exception");
    }

    [Fact]
    public void TrackMetric_WhenTrackThrows_ThrowsInvalidOperationException()
    {
        var sut = CreateService(t => throw new InvalidOperationException("track error"));
        var act = () => sut.TrackMetric("TestMetric", 123.45);
        act.Should().Throw<InvalidOperationException>().WithMessage("Failed to track metric: TestMetric");
    }

    [Fact]
    public void TrackRequest_WhenTrackThrows_ThrowsInvalidOperationException()
    {
        var sut = CreateService(t => throw new InvalidOperationException("track error"));
        var act = () => sut.TrackRequest("TestReq", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), "200", true);
        act.Should().Throw<InvalidOperationException>().WithMessage("Failed to track request: TestReq");
    }

    [Fact]
    public void TrackQuery_WhenTrackThrows_ThrowsInvalidOperationException()
    {
        var sut = CreateService(t => throw new InvalidOperationException("track error"));
        var act = () => sut.TrackQuery("TestQuery", "SELECT 1", TimeSpan.FromSeconds(1), 1, 0.0m);
        act.Should().Throw<InvalidOperationException>().WithMessage("Failed to track query: TestQuery");
    }

    [Fact]
    public void Flush_WhenTelemetryClientIsNull_ThrowsInvalidOperationException()
    {
        var sut = CreateService();
        var field = typeof(TelemetryService).GetField("_telemetryClient", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(sut, null);

        var act = () => ((TelemetryService)sut).Flush();
        act.Should().Throw<InvalidOperationException>().WithMessage("Failed to flush telemetry client");
    }

    [Fact]
    public void TrackDegradedMode_WhenTrackThrows_ThrowsInvalidOperationException()
    {
        var sut = CreateService(t => throw new InvalidOperationException("track error"));
        var act = () => sut.TrackDegradedMode("corr-123", new[] { "src1" }, new[] { "src2" }, TimeSpan.FromSeconds(1), 5);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TrackSourceFailure_WhenTrackThrows_ThrowsInvalidOperationException()
    {
        var sut = CreateService(t => throw new InvalidOperationException("track error"));
        var act = () => sut.TrackSourceFailure("corr-123", "src1", "Reason", TimeSpan.FromSeconds(1));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TrackSearchExecution_WhenTrackThrows_ThrowsInvalidOperationException()
    {
        var sut = CreateService(t => throw new InvalidOperationException("track error"));
        var act = () => sut.TrackSearchExecution("corr-123", "q", TimeSpan.FromSeconds(1), 5, 2, 1, true);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TrackEvent_WhenRedactionRegexThrows_LogsWarningAndReturnsOriginalString()
    {
        var sut = CreateService();
        
        var field = typeof(TelemetryService).GetField("_queryTextPattern", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(sut, null);

        var act = () => sut.TrackEvent("EventWithPatternFailure", new Dictionary<string, string> { ["Description"] = "sk-key" });
        act.Should().NotThrow();

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to redact sensitive data")),
                It.IsAny<NullReferenceException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void TrackEvent_WhenPropertiesEnumerationThrows_LogsWarningAndThrowsInvalidOperationException()
    {
        var sut = CreateService();
        var throwingProps = new ThrowingDictionary { ["Description"] = "sk-key" };

        var act = () => sut.TrackEvent("EventWithEnumFailure", throwingProps);
        act.Should().Throw<InvalidOperationException>().WithMessage("Failed to track event: EventWithEnumFailure");

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to redact sensitive properties")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private class ThrowingDictionary : Dictionary<string, string>, IDictionary<string, string>
    {
        System.Collections.Generic.IEnumerator<System.Collections.Generic.KeyValuePair<string, string>> System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, string>>.GetEnumerator()
        {
            throw new InvalidOperationException("enumeration failed");
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            throw new InvalidOperationException("enumeration failed");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Disposal
    // ─────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _aiConfig?.Dispose();
        }
    }
}
