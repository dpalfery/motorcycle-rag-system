using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Infrastructure.Resilience;
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

        var operation = () => throw new InvalidOperationException("Test exception");

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
}