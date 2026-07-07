using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Xunit;
using MotorcycleRAG.IntegrationTests;


namespace MotorcycleRAG.IntegrationTests.Authorization;

public class AuthorizationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AuthorizationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MotorcycleController_QueryEndpoint_RequiresAuthentication()
    {
        // Arrange
        using var client = _factory.CreateClient();

        var request = new
        {
            Query = "What is engine size of a Honda CBR600RR?"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/motorcycles/query", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DataPipelineController_UploadEndpoint_RequiresAdminRole()
    {
        // Arrange
        using var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // This would normally configure test authentication
                // For integration tests, we'd use a test authentication handler
            });
        }).CreateClient();

        // Act - Try to access without proper authorization
        var response = await client.PostAsync("/api/datapipeline/upload", null);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MotorcycleController_HealthEndpoint_RequiresAuthentication()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/motorcycles/health");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DataPipelineController_StatusEndpoint_RequiresAdminRole()
    {
        // Arrange
        using var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/datapipeline/status/invalid-id");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WebSourcesController_GetAll_WithAdminRole_ReturnsSuccess()
    {
        // Arrange
        using var client = _factory.CreateAdminClient();

        // Act
        var response = await client.GetAsync("/api/admin/web-sources");

        // Assert
        // If the database is not set up, it might return 500, but it should NOT be 401 or 403
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Additional tests would be added here to test with authenticated users and different roles
    // These would require setting up test authentication handlers and claims
}
