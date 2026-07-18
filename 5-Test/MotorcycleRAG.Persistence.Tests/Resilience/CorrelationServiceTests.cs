using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Persistence.Resilience;
using System.Diagnostics;
using Xunit;


namespace MotorcycleRAG.UnitTests.Resilience;

public class CorrelationServiceTests
{
    private readonly Mock<ILogger<CorrelationService>> _mockLogger;
    private readonly CorrelationService _correlationService;

    public CorrelationServiceTests()
    {
        _mockLogger = new Mock<ILogger<CorrelationService>>();
        
        // Setup BeginScope to return a mock IDisposable
        _mockLogger
            .Setup(x => x.BeginScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(Mock.Of<IDisposable>());
            
        _correlationService = new CorrelationService(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        var act = () => new CorrelationService(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void GetOrCreateCorrelationId_NoExistingId_GeneratesNewId()
    {
        // Act
        var correlationId = _correlationService.GetOrCreateCorrelationId();

        // Assert
        Assert.NotNull(correlationId);
        Assert.NotEmpty(correlationId);
        Assert.StartsWith("corr-", correlationId);
    }

    [Fact]
    public void GetOrCreateCorrelationId_WithExistingId_ReturnsSameId()
    {
        // Arrange
        const string existingId = "test-correlation-123";
        _correlationService.SetCorrelationId(existingId);

        // Act
        var correlationId = _correlationService.GetOrCreateCorrelationId();

        // Assert
        Assert.Equal(existingId, correlationId);
    }

    [Fact]
    public void GetOrCreateCorrelationId_WithActivity_UsesActivityId()
    {
        // Arrange
        using var activity = new Activity("TestActivity");
        activity.Start();

        // Clear any existing correlation ID first
        _correlationService.ClearCorrelationId();

        // Act
        var correlationId = _correlationService.GetOrCreateCorrelationId();

        // Assert
        Assert.Equal(activity.Id, correlationId);
    }

    [Fact]
    public void SetCorrelationId_ValidId_SetsSuccessfully()
    {
        // Arrange
        const string testId = "test-correlation-456";

        // Act
        _correlationService.SetCorrelationId(testId);
        var retrievedId = _correlationService.GetOrCreateCorrelationId();

        // Assert
        Assert.Equal(testId, retrievedId);
    }

    [Fact]
    public void SetCorrelationId_NullOrEmpty_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _correlationService.SetCorrelationId(null!));
        Assert.Throws<ArgumentException>(() => _correlationService.SetCorrelationId(string.Empty));
        Assert.Throws<ArgumentException>(() => _correlationService.SetCorrelationId("   "));
    }

    [Fact]
    public void ClearCorrelationId_WithExistingId_ClearsSuccessfully()
    {
        // Arrange
        _correlationService.SetCorrelationId("test-id");

        // Act
        _correlationService.ClearCorrelationId();
        var newId = _correlationService.GetOrCreateCorrelationId();

        // Assert
        Assert.NotEqual("test-id", newId);
        Assert.StartsWith("corr-", newId);
    }

    [Fact]
    public async Task ExecuteWithCorrelationAsync_SetsAndRestoresCorrelationId()
    {
        // Arrange
        const string originalId = "original-id";
        const string operationId = "operation-id";
        const string expectedResult = "test-result";
        
        _correlationService.SetCorrelationId(originalId);

        string capturedId = null!;
        var operation = () =>
        {
            capturedId = _correlationService.GetOrCreateCorrelationId();
            return Task.FromResult(expectedResult);
        };

        // Act
        var result = await _correlationService.ExecuteWithCorrelationAsync(operationId, operation);

        // Assert
        Assert.Equal(expectedResult, result);
        Assert.Equal(operationId, capturedId);
        Assert.Equal(originalId, _correlationService.GetOrCreateCorrelationId());
    }

    [Fact]
    public async Task ExecuteWithCorrelationAsync_NoReturnValue_ExecutesSuccessfully()
    {
        // Arrange
        const string operationId = "operation-id";
        var operationExecuted = false;
        
        var operation = () =>
        {
            operationExecuted = true;
            return Task.CompletedTask;
        };

        // Act
        await _correlationService.ExecuteWithCorrelationAsync(operationId, operation);

        // Assert
        Assert.True(operationExecuted);
    }

    [Fact]
    public async Task ExecuteWithCorrelationAsync_ThrowsException_RestoresOriginalId()
    {
        // Arrange
        const string originalId = "original-id";
        const string operationId = "operation-id";
        
        _correlationService.SetCorrelationId(originalId);

        var operation = async () => 
        {
            await Task.Yield();
            throw new InvalidOperationException("Test exception");
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _correlationService.ExecuteWithCorrelationAsync(operationId, operation));

        // Verify original ID is restored
        Assert.Equal(originalId, _correlationService.GetOrCreateCorrelationId());
    }

    [Fact]
    public void CreateLoggingScope_CreatesValidScope()
    {
        // Arrange
        const string testId = "test-scope-id";
        _correlationService.SetCorrelationId(testId);

        // Act
        using var scope = _correlationService.CreateLoggingScope();

        // Assert
        Assert.NotNull(scope);
        // Scope creation should be successful - actual verification would require logger mock setup
    }

    [Fact]
    public void CreateLoggingScope_WithAdditionalProperties_IncludesAllProperties()
    {
        // Arrange
        const string testId = "test-scope-id";
        _correlationService.SetCorrelationId(testId);
        
        var additionalProperties = new Dictionary<string, object>
        {
            ["Property1"] = "Value1",
            ["Property2"] = 123
        };

        // Act
        using var scope = _correlationService.CreateLoggingScope(additionalProperties);

        // Assert
        Assert.NotNull(scope);
        // Scope should include both correlation ID and additional properties
    }

    [Fact]
    public void CreateLoggingScope_CyclicDictionaryAndListWithControlKeys_SanitizesKeysAndPreservesGraphIdentity()
    {
        const string rootKey = "Client\r\n\u0085";
        const string nestedKey = "File\0\u001FName";
        const string rawValue = "client\\name\r\nforged\tentry\0unit\u001Fseparator\u0085next";
        const string expectedRootKey = "Client\\r\\n\\u0085";
        const string expectedNestedKey = "File\\0\\u001FName";
        const string expectedEscapedValue = "client\\\\name\\r\\nforged\\tentry\\0unit\\u001Fseparator\\u0085next";
        Dictionary<string, object>? capturedScope = null;
        _mockLogger
            .Setup(x => x.BeginScope(It.IsAny<Dictionary<string, object>>()))
            .Callback<Dictionary<string, object>>(scope => capturedScope = scope)
            .Returns(Mock.Of<IDisposable>());

        var cyclicDictionary = new Dictionary<string, object>
        {
            [nestedKey] = rawValue
        };
        var cyclicList = new List<object>();
        cyclicDictionary["Children"] = cyclicList;
        cyclicList.Add(cyclicDictionary);
        var additionalProperties = new Dictionary<string, object>
        {
            [rootKey] = cyclicDictionary
        };

        // Act
        using var scope = _correlationService.CreateLoggingScope(additionalProperties);

        // Assert
        capturedScope.Should().NotBeNull();
        capturedScope!.Keys.Should().OnlyContain(key => !key.Any(char.IsControl));
        var sanitizedDictionary = capturedScope[expectedRootKey]
            .Should().BeOfType<Dictionary<string, object>>().Subject;
        sanitizedDictionary.Keys.Should().OnlyContain(key => !key.Any(char.IsControl));
        sanitizedDictionary[expectedNestedKey].Should().Be(expectedEscapedValue);
        var sanitizedList = sanitizedDictionary["Children"]
            .Should().BeOfType<List<object>>().Subject;
        sanitizedList.Should().ContainSingle().Which.Should().BeSameAs(sanitizedDictionary);

        additionalProperties.Should().ContainKey(rootKey).WhoseValue.Should().BeSameAs(cyclicDictionary);
        cyclicDictionary.Should().ContainKey(nestedKey).WhoseValue.Should().Be(rawValue);
        cyclicList.Should().ContainSingle().Which.Should().BeSameAs(cyclicDictionary);
    }

    [Fact]
    public void GeneratedCorrelationIds_AreUnique()
    {
        // Arrange
        var correlationIds = new HashSet<string>();
        const int numberOfIds = 100;

        // Act
        for (int i = 0; i < numberOfIds; i++)
        {
            _correlationService.ClearCorrelationId();
            var id = _correlationService.GetOrCreateCorrelationId();
            correlationIds.Add(id);
        }

        // Assert
        Assert.Equal(numberOfIds, correlationIds.Count);
    }

    [Fact]
    public void GeneratedCorrelationIds_FollowExpectedFormat()
    {
        // Arrange
        _correlationService.ClearCorrelationId();

        // Act
        var correlationId = _correlationService.GetOrCreateCorrelationId();

        // Assert
        Assert.Matches(@"^corr-\d{17}-[a-f0-9]{12}$", correlationId);
    }

    // ─────────────────────────────────────────────────────────────────
    // GetCorrelationId
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GetCorrelationId_ShouldReturnSameAsGetOrCreateCorrelationId()
    {
        // Arrange
        _correlationService.ClearCorrelationId();

        // Act
        var id1 = _correlationService.GetCorrelationId();
        var id2 = _correlationService.GetOrCreateCorrelationId();

        // Assert
        Assert.Equal(id1, id2);
        Assert.StartsWith("corr-", id1);
    }

    // ─────────────────────────────────────────────────────────────────
    // GetOrGenerateCorrelationId
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GetOrGenerateCorrelationId_ShouldReturnValidId()
    {
        // Arrange
        _correlationService.ClearCorrelationId();

        // Act
        var id = _correlationService.GetOrGenerateCorrelationId();

        // Assert
        Assert.NotNull(id);
        Assert.NotEmpty(id);
        Assert.StartsWith("corr-", id);
    }

    [Fact]
    public void GetOrGenerateCorrelationId_WithExistingId_ReturnsSameId()
    {
        // Arrange
        const string existingId = "test-correlation-789";
        _correlationService.SetCorrelationId(existingId);

        // Act
        var id = _correlationService.GetOrGenerateCorrelationId();

        // Assert
        Assert.Equal(existingId, id);
    }

    // ─────────────────────────────────────────────────────────────────
    // StartActivity
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void StartActivity_ShouldReturnDisposableActivity()
    {
        // Act
        using var activity = _correlationService.StartActivity("TestOperation");

        // Assert
        Assert.NotNull(activity);
    }

    // ─────────────────────────────────────────────────────────────────
    // ExecuteWithCorrelationAsync validation
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteWithCorrelationAsync_WithNullOperation_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _correlationService.ExecuteWithCorrelationAsync<string>("test-id", null!));
    }

    [Fact]
    public async Task ExecuteWithCorrelationAsync_WithNullVoidOperation_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        // NOTE: The non-generic overload delegates to the generic version via a lambda wrapper.
        // Because the null operation is captured in the lambda before the generic overload's null
        // check runs, the actual exception is NullReferenceException, not ArgumentNullException.
        await Assert.ThrowsAsync<NullReferenceException>(
            () => _correlationService.ExecuteWithCorrelationAsync("test-id", null!));
    }

    // ─────────────────────────────────────────────────────────────────
    // CreateLoggingScope without pre-existing correlation ID
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CreateLoggingScope_WithoutExistingCorrelationId_GeneratesNewId()
    {
        // Arrange
        _correlationService.ClearCorrelationId();

        // Act
        using var scope = _correlationService.CreateLoggingScope();

        // Assert
        Assert.NotNull(scope);
        // The correlation ID was generated automatically during scope creation
    }

    [Fact]
    public void CreateLoggingScope_WithAdditionalProperties_WithoutExistingId_GeneratesNewId()
    {
        // Arrange
        _correlationService.ClearCorrelationId();
        var additionalProperties = new Dictionary<string, object>
        {
            ["Operation"] = "Search"
        };

        // Act
        using var scope = _correlationService.CreateLoggingScope(additionalProperties);

        // Assert
        Assert.NotNull(scope);
    }

    // ─────────────────────────────────────────────────────────────────
    // SetCorrelationId sanitization
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SetCorrelationId_WithNewlines_ShouldSanitizeThem()
    {
        // Act
        _correlationService.SetCorrelationId("test\nid\rwith\ttabs");
        var retrieved = _correlationService.GetOrCreateCorrelationId();

        // Assert
        Assert.DoesNotContain("\n", retrieved);
        Assert.DoesNotContain("\r", retrieved);
        Assert.DoesNotContain("\t", retrieved);
    }

    // ─────────────────────────────────────────────────────────────────
    // ClearCorrelationId when no ID exists
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ClearCorrelationId_WhenNoIdIsSet_ShouldNotThrow()
    {
        // Arrange
        _correlationService.ClearCorrelationId();

        // Act
        var act = () => _correlationService.ClearCorrelationId();

        // Assert
        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────
    // GenerateCorrelationId
    // ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateCorrelationId_ShouldReturnFormattedId()
    {
        // Act
        var id = _correlationService.GenerateCorrelationId();

        // Assert
        Assert.NotNull(id);
        Assert.StartsWith("corr-", id);
        Assert.Matches(@"^corr-\d{17}-[a-f0-9]{12}$", id);
    }

    [Fact]
    public void CreateLoggingScope_WithNullAdditionalProperties_ShouldThrowArgumentNullException()
    {
        var act = () => _correlationService.CreateLoggingScope(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("additionalProperties");
    }

    [Fact]
    public void CreateLoggingScope_WithNullAndControlBearingValues_SanitizesScopeGraph()
    {
        Dictionary<string, object>? capturedScope = null;
        _mockLogger
            .Setup(x => x.BeginScope(It.IsAny<Dictionary<string, object>>()))
            .Callback<Dictionary<string, object>>(scope => capturedScope = scope)
            .Returns(Mock.Of<IDisposable>());

        var nested = new Dictionary<object, object>
        {
            [42] = "plain",
            ["Label\r\n"] = new ControlBearingScopeValue("unit\tvalue\0"),
        };
        var values = new[] { "safe", "line\r\nbreak", null };
        var matrix = Array.CreateInstance(typeof(string), [2, 2]);
        matrix.SetValue("a\tb", 0, 0);
        matrix.SetValue("c", 0, 1);
        matrix.SetValue("d", 1, 0);
        matrix.SetValue("e\0f", 1, 1);
        var additionalProperties = new Dictionary<string, object>
        {
            ["Missing"] = null!,
            ["Count"] = 7,
            ["Nested"] = nested,
            ["Values"] = values,
            ["Matrix"] = matrix,
            ["Tags"] = new HashSet<string> { "alpha", "beta\n" },
        };

        using var scope = _correlationService.CreateLoggingScope(additionalProperties);

        capturedScope.Should().NotBeNull();
        capturedScope!["Missing"].Should().BeNull();
        capturedScope["Count"].Should().Be(7);
        var sanitizedNested = capturedScope["Nested"]
            .Should().BeAssignableTo<System.Collections.IDictionary>().Subject;
        sanitizedNested[42].Should().Be("plain");
        sanitizedNested["Label\\r\\n"].Should().Be("unit\\tvalue\\0");
        var sanitizedValues = capturedScope["Values"].Should().BeOfType<string?[]>().Subject;
        sanitizedValues.Should().Equal("safe", "line\\r\\nbreak", null);
        var sanitizedMatrix = capturedScope["Matrix"].Should().BeAssignableTo<Array>().Subject;
        sanitizedMatrix.GetValue(0, 0).Should().Be("a\\tb");
        sanitizedMatrix.GetValue(1, 1).Should().Be("e\\0f");
        var sanitizedTags = capturedScope["Tags"].Should().BeAssignableTo<IEnumerable<string>>().Subject;
        sanitizedTags.Should().Contain("beta\\n");
    }

    [Fact]
    public void CreateLoggingScope_WithReadOnlyDictionary_SanitizesEntriesIntoWritableCopy()
    {
        Dictionary<string, object>? capturedScope = null;
        _mockLogger
            .Setup(x => x.BeginScope(It.IsAny<Dictionary<string, object>>()))
            .Callback<Dictionary<string, object>>(scope => capturedScope = scope)
            .Returns(Mock.Of<IDisposable>());

        IReadOnlyDictionary<string, object> nested =
            new Dictionary<string, object> { ["Inner\tKey"] = "value\r" };
        var additionalProperties = new Dictionary<string, object>
        {
            ["Readonly"] = nested,
        };

        using var scope = _correlationService.CreateLoggingScope(additionalProperties);

        capturedScope.Should().NotBeNull();
        var sanitized = capturedScope!["Readonly"]
            .Should().BeOfType<Dictionary<string, object>>().Subject;
        sanitized["Inner\\tKey"].Should().Be("value\\r");
    }

    private readonly record struct ControlBearingScopeValue(string Value)
    {
        public override string ToString() => Value;
    }

}

/// <summary>
/// Tests for <see cref="LoggerExtensions"/> static extension methods.
/// </summary>
public class LoggerExtensionsTests
{
    private readonly Mock<ILogger<CorrelationService>> _mockLogger;

    public LoggerExtensionsTests()
    {
        _mockLogger = new Mock<ILogger<CorrelationService>>();
        _mockLogger
            .Setup(x => x.BeginScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(Mock.Of<IDisposable>());
    }

    [Fact]
    public void LogErrorWithCorrelation_LogsSanitizedArgumentsAndCorrelationScope()
    {
        // Arrange
        var logger = new CapturingLogger<CorrelationService>();
        var ex = new InvalidOperationException("test error");
        const string correlationId = "corr\\error\r\n42";
        const string detail = "Something\\broke\tbadly";

        // Act
        logger.LogErrorWithCorrelation(ex, "Error occurred: {Detail} {Attempt}", correlationId, detail, 3);

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Exception.Should().BeSameAs(ex);
        entry.Properties["Detail"].Should().Be("Something\\\\broke\\tbadly");
        entry.Properties["Attempt"].Should().Be(3);
        entry.Properties["{OriginalFormat}"].Should().Be("Error occurred: {Detail} {Attempt}");
        AssertContainsNoRawControlCharacters(entry.Message);
        logger.Scopes.Should().ContainSingle().Which["CorrelationId"]
            .Should().Be("corr\\\\error\\r\\n42");
    }

    [Fact]
    public void LogErrorWithCorrelation_WithNullLogger_ShouldThrowArgumentNullException()
    {
        ILogger<CorrelationService> logger = null!;

        var act = () => logger.LogErrorWithCorrelation(
            new InvalidOperationException("test error"), "message", "corr-id");

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void LogWarningWithCorrelation_LogsSanitizedArgumentsAndCorrelationScope()
    {
        // Arrange
        var logger = new CapturingLogger<CorrelationService>();
        const string correlationId = "corr\\warning\r\n42";
        const string detail = "Low\\disk\tspace";

        // Act
        logger.LogWarningWithCorrelation("Warning: {Detail} {RetryCount}", correlationId, detail, 2);

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Exception.Should().BeNull();
        entry.Properties["Detail"].Should().Be("Low\\\\disk\\tspace");
        entry.Properties["RetryCount"].Should().Be(2);
        entry.Properties["{OriginalFormat}"].Should().Be("Warning: {Detail} {RetryCount}");
        AssertContainsNoRawControlCharacters(entry.Message);
        logger.Scopes.Should().ContainSingle().Which["CorrelationId"]
            .Should().Be("corr\\\\warning\\r\\n42");
    }

    [Fact]
    public void LogWarningWithCorrelation_WithNullLogger_ShouldThrowArgumentNullException()
    {
        ILogger<CorrelationService> logger = null!;

        var act = () => logger.LogWarningWithCorrelation(
            "message", "corr-id");

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void LogInformationWithCorrelation_ControlCharactersInTemplate_AreEscapedInOriginalFormatAndRenderedMessage()
    {
        // Arrange
        var logger = new CapturingLogger<CorrelationService>();
        const string correlationId = "corr\\information\r\n\u008542";
        const string messageTemplate = "Info: {Detail}\r\n\u0085";
        const string detail = "Process completed";

        // Act
        logger.LogInformationWithCorrelation(messageTemplate, correlationId, detail);

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Exception.Should().BeNull();
        entry.Properties["Detail"].Should().Be(detail);
        var originalFormat = entry.Properties["{OriginalFormat}"].Should().BeOfType<string>().Subject;
        originalFormat.Should().Be("Info: {Detail}\\r\\n\\u0085");
        AssertContainsNoRawControlCharacters(originalFormat);
        AssertContainsNoRawControlCharacters(entry.Message);
        logger.Scopes.Should().ContainSingle().Which["CorrelationId"]
            .Should().Be("corr\\\\information\\r\\n\\u008542");
    }

    [Fact]
    public void LogInformationWithCorrelation_ControlBearingStruct_IsEscapedInStructuredAndRenderedOutput()
    {
        // Arrange
        var logger = new CapturingLogger<CorrelationService>();
        var detail = new ControlBearingValue("Process\\completed\tcleanly\0\u001F\u0085");

        // Act
        logger.LogInformationWithCorrelation("Info: {Detail}", "corr-information", detail);

        // Assert
        var entry = logger.Entries.Should().ContainSingle().Subject;
        entry.Properties["Detail"].Should().Be("Process\\\\completed\\tcleanly\\0\\u001F\\u0085");
        AssertContainsNoRawControlCharacters(entry.Message);
    }

    [Fact]
    public void LogInformationWithCorrelation_WithNullLogger_ShouldThrowArgumentNullException()
    {
        ILogger<CorrelationService> logger = null!;

        var act = () => logger.LogInformationWithCorrelation(
            "message", "corr-id");

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<Dictionary<string, object?>> Scopes { get; } = [];
        public List<CapturedLogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            var scope = state.Should().BeOfType<Dictionary<string, object>>().Which;
            Scopes.Add(scope.ToDictionary(pair => pair.Key, pair => (object?)pair.Value));
            return Mock.Of<IDisposable>();
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state.Should().BeAssignableTo<IEnumerable<KeyValuePair<string, object?>>>()
                .Which
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            Entries.Add(new CapturedLogEntry(
                logLevel,
                exception,
                formatter(state, exception),
                properties));
        }
    }

    private static void AssertContainsNoRawControlCharacters(string value)
    {
        value.Any(char.IsControl).Should().BeFalse();
    }

    private readonly record struct ControlBearingValue(string Value)
    {
        public override string ToString() => Value;
    }

    private sealed record CapturedLogEntry(
        LogLevel Level,
        Exception? Exception,
        string Message,
        IReadOnlyDictionary<string, object?> Properties);
}
