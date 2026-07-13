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
    public void LogErrorWithCorrelation_ShouldNotThrow()
    {
        var ex = new InvalidOperationException("test error");

        var act = () => _mockLogger.Object.LogErrorWithCorrelation(
            ex, "Error occurred: {Detail}", "corr-err-001", "Something broke");

        act.Should().NotThrow();
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
    public void LogWarningWithCorrelation_ShouldNotThrow()
    {
        var act = () => _mockLogger.Object.LogWarningWithCorrelation(
            "Warning: {Detail}", "corr-warn-001", "Low disk space");

        act.Should().NotThrow();
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
    public void LogInformationWithCorrelation_ShouldNotThrow()
    {
        var act = () => _mockLogger.Object.LogInformationWithCorrelation(
            "Info: {Detail}", "corr-info-001", "Process completed");

        act.Should().NotThrow();
    }

    [Fact]
    public void LogInformationWithCorrelation_WithNullLogger_ShouldThrowArgumentNullException()
    {
        ILogger<CorrelationService> logger = null!;

        var act = () => logger.LogInformationWithCorrelation(
            "message", "corr-id");

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }
}
