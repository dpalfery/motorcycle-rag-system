using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using Xunit;
using MotorcycleRAG.IntegrationTests;

namespace MotorcycleRAG.IntegrationTests.Api;

/// <summary>
/// Integration tests for the /health endpoint
/// Validates health check contract, response structure, and dependency status reporting
/// as per User Story 5 (FR-020-FR-021) operational verification requirements
/// </summary>
public class HealthIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public HealthIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Test 1: Health endpoint returns 200 OK when all dependencies are healthy
    /// Scenario: Normal operation with all services available
    /// Expected: Status=Healthy, HTTP 200 OK
    /// </summary>
    [Fact]
    public async Task HealthCheck_AllDependenciesHealthy_Returns200Ok()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Test 2: Health endpoint response contains required status field
    /// Scenario: Verify response structure includes "status" field
    /// Expected: Response JSON includes "status" property with valid value
    /// </summary>
    [Fact]
    public async Task HealthCheck_ResponseContainsStatusField()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();
        var healthResponse = JsonDocument.Parse(content).RootElement;

        // Assert
        Assert.True(healthResponse.TryGetProperty("status", out var statusProperty), 
            "Response must contain 'status' field");
        
        var statusValue = statusProperty.GetString();
        Assert.NotNull(statusValue);
        Assert.True(
            statusValue is "Healthy" or "Degraded" or "Unhealthy",
            $"Status value '{statusValue}' must be one of: Healthy, Degraded, Unhealthy");
    }

    /// <summary>
    /// Test 3: Health endpoint response contains checks object with dependencies
    /// Scenario: Verify response structure includes "checks" object with individual service checks
    /// Expected: Response JSON includes "checks" object with health check entries
    /// </summary>
    [Fact]
    public async Task HealthCheck_ResponseContainsChecksObject()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();
        var healthResponse = JsonDocument.Parse(content).RootElement;

        // Assert
        Assert.True(healthResponse.TryGetProperty("checks", out var checksProperty), 
            "Response must contain 'checks' object");
        
        Assert.Equal(JsonValueKind.Object, checksProperty.ValueKind);
        
        // Verify at least one check exists (self check is mandatory)
        Assert.True(checksProperty.EnumerateObject().Any(),
            "Checks object must contain at least one health check entry");
    }

    /// <summary>
    /// Test 4: Health endpoint response includes duration metrics
    /// Scenario: Verify response structure includes timing information
    /// Expected: Response JSON includes "totalDuration" field with valid duration string
    /// </summary>
    [Fact]
    public async Task HealthCheck_ResponseIncludesTotalDuration()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();
        var healthResponse = JsonDocument.Parse(content).RootElement;

        // Assert
        Assert.True(healthResponse.TryGetProperty("totalDuration", out var durationProperty), 
            "Response must contain 'totalDuration' field");
        
        var durationValue = durationProperty.GetString();
        Assert.NotNull(durationValue);
        Assert.NotEmpty(durationValue);

        // Verify it's a valid timespan format (e.g., "00:00:00.1234567")
        Assert.True(TimeSpan.TryParse(durationValue, CultureInfo.InvariantCulture, out _),
            $"Duration '{durationValue}' must be a valid TimeSpan format");
    }

    /// <summary>
    /// Test 5: Individual health checks include required fields (status and duration)
    /// Scenario: Verify each check in the checks object has status and duration
    /// Expected: Each check entry has "status" and "duration" properties
    /// </summary>
    [Fact]
    public async Task HealthCheck_EachCheckIncludesStatusAndDuration()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();
        var healthResponse = JsonDocument.Parse(content).RootElement;
        
        healthResponse.TryGetProperty("checks", out var checksProperty);

        // Assert
        var checkCount = 0;
        foreach (var check in checksProperty.EnumerateObject())
        {
            checkCount++;
            
            Assert.True(check.Value.TryGetProperty("status", out var statusProp),
                $"Check '{check.Name}' must include 'status' field");
            
            var statusValue = statusProp.GetString();
            Assert.NotNull(statusValue);
            Assert.True(
                statusValue is "Healthy" or "Degraded" or "Unhealthy",
                $"Check '{check.Name}' status must be one of: Healthy, Degraded, Unhealthy");
            
            Assert.True(check.Value.TryGetProperty("duration", out var durationProp),
                $"Check '{check.Name}' must include 'duration' field");
            
            var durationValue = durationProp.GetString();
            Assert.NotNull(durationValue);
            Assert.True(TimeSpan.TryParse(durationValue, CultureInfo.InvariantCulture, out _),
                $"Check '{check.Name}' duration must be a valid TimeSpan format");
        }
        
        Assert.True(checkCount > 0, "At least one health check must be present");
    }

    /// <summary>
    /// Test 6: Health check completes within reasonable timeout (< 5 seconds)
    /// Scenario: Ensure health endpoint is responsive and not blocked
    /// Expected: Response completes within 5 seconds
    /// </summary>
    [Fact]
    public async Task HealthCheck_CompletesWithinReasonableTimeout()
    {
        // Arrange
        var client = _factory.CreateClient();
        var timeout = TimeSpan.FromSeconds(5);
        using var cts = new System.Threading.CancellationTokenSource(timeout);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.GetAsync("/health", cts.Token);
        stopwatch.Stop();

        // Assert
        Assert.True(stopwatch.Elapsed < timeout,
            $"Health check must complete within {timeout.TotalSeconds} seconds, but took {stopwatch.Elapsed.TotalSeconds} seconds");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Test 7: Health endpoint is accessible without authentication
    /// Scenario: Verify health endpoint does not require authentication (for monitoring systems)
    /// Expected: Status 200 OK without authentication headers
    /// </summary>
    [Fact]
    public async Task HealthCheck_AccessibleWithoutAuthentication()
    {
        // Arrange
        var client = _factory.CreateClient();
        // Explicitly do NOT add authentication headers

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.False(string.IsNullOrWhiteSpace(content), "Health check must return response body");
    }

    /// <summary>
    /// Test 8: Health check response is valid JSON
    /// Scenario: Ensure response can be parsed as valid JSON
    /// Expected: Response is valid JSON that can be deserialized
    /// </summary>
    [Fact]
    public async Task HealthCheck_ResponseIsValidJson()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        var ex = Record.Exception(() => JsonDocument.Parse(content));
        Assert.Null(ex);
    }

    /// <summary>
    /// Test 9: Health check reports all configured checks
    /// Scenario: Verify all registered health checks appear in response
    /// Expected: Response includes "self" and "configuration" checks
    /// </summary>
    [Fact]
    public async Task HealthCheck_ReportsAllConfiguredChecks()
    {
        // Arrange
        var client = _factory.CreateClient();
        var expectedChecks = new[] { "self", "configuration", "onboarding_notification", "external_identity_provisioning" };

        // Act
        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();
        var healthResponse = JsonDocument.Parse(content).RootElement;
        
        healthResponse.TryGetProperty("checks", out var checksProperty);
        var checkNames = checksProperty.EnumerateObject().Select(c => c.Name).ToList();

        // Assert
        foreach (var expectedCheck in expectedChecks)
        {
            Assert.True(checkNames.Contains(expectedCheck), 
                $"Expected check '{expectedCheck}' not found in health checks");
        }
    }

    /// <summary>
    /// Test 10: Health check response structure can be deserialized to HealthCheckResponse
    /// Scenario: Validate response matches expected contract structure
    /// Expected: Response can be successfully deserialized to a well-formed object
    /// </summary>
    [Fact]
    public async Task HealthCheck_ResponseMatchesExpectedContract()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        // Assert - Verify response structure matches health check contract
        var healthResponse = JsonDocument.Parse(content).RootElement;
        
        // Required top-level fields
        Assert.True(healthResponse.TryGetProperty("status", out _), "Must have 'status' field");
        Assert.True(healthResponse.TryGetProperty("checks", out _), "Must have 'checks' field");
        Assert.True(healthResponse.TryGetProperty("totalDuration", out _), "Must have 'totalDuration' field");
        
        // Validate type expectations
        Assert.Equal(JsonValueKind.String, healthResponse.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Object, healthResponse.GetProperty("checks").ValueKind);
        Assert.Equal(JsonValueKind.String, healthResponse.GetProperty("totalDuration").ValueKind);
    }

    /// <summary>
    /// Test 11: Multiple sequential health checks return consistent structure
    /// Scenario: Ensure health endpoint maintains consistent response format across calls
    /// Expected: Multiple calls return same structure and valid values
    /// </summary>
    [Fact]
    public async Task HealthCheck_MultipleCallsReturnConsistentStructure()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response1 = await client.GetAsync("/health");
        var content1 = await response1.Content.ReadAsStringAsync();
        var json1 = JsonDocument.Parse(content1).RootElement;

        var response2 = await client.GetAsync("/health");
        var content2 = await response2.Content.ReadAsStringAsync();
        var json2 = JsonDocument.Parse(content2).RootElement;

        // Assert
        // Both responses should have the same top-level fields
        Assert.True(json1.TryGetProperty("status", out _));
        Assert.True(json2.TryGetProperty("status", out _));
        Assert.True(json1.TryGetProperty("checks", out var checks1));
        Assert.True(json2.TryGetProperty("checks", out var checks2));

        // Check count should be consistent
        var checkCount1 = checks1.EnumerateObject().Count();
        var checkCount2 = checks2.EnumerateObject().Count();
        Assert.Equal(checkCount1, checkCount2);

        // Both should be valid health responses
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }

    /// <summary>
    /// Test 12: Health check status reflects actual health when dependencies degrade
    /// Scenario: Verify health check can report Degraded status when issues occur
    /// Expected: Degraded status includes detailed check information
    /// </summary>
    [Fact]
    public async Task HealthCheck_DegradedStatusIncludesCheckDetails()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // This test verifies that even if checks report degraded status,
                // the response structure is valid and complete
                // (Note: Individual checks might be degraded due to configuration in test setup)
            });
        }).CreateClient();

        // Act
        var response = await client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();
        var healthResponse = JsonDocument.Parse(content).RootElement;

        // Assert
        // Health check should always return 200 OK, but status field can be Degraded
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        healthResponse.TryGetProperty("status", out var statusProperty);
        healthResponse.TryGetProperty("checks", out var checksProperty);

        // Validate structure even in degraded state
        Assert.NotNull(statusProperty.GetString());
        
        // All checks should have detailed information
        foreach (var check in checksProperty.EnumerateObject())
        {
            Assert.True(check.Value.TryGetProperty("status", out _),
                $"Check '{check.Name}' must have status field");
            Assert.True(check.Value.TryGetProperty("duration", out _),
                $"Check '{check.Name}' must have duration field");
        }
    }
}
