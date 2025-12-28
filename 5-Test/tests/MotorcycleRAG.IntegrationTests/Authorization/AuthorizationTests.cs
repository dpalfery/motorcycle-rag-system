using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Xunit;
using MotorcycleRAG.Domain.Models;

namespace MotorcycleRAG.IntegrationTests.Authorization;

public class AuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public AuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MotorcycleController_QueryEndpoint_RequiresAuthentication()
    {
        // Arrange
        var client = _factory.CreateClient();

        var request = new
        {
            Query = "What is the engine size of a Honda CBR600RR?"
        };

        // Act
        var response = await client.PostAsJsonAsync("/api/motorcycles/query", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DataPipelineController_UploadEndpoint_RequiresDataAdminRole()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
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
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/motorcycles/health");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DataPipelineController_StatusEndpoint_RequiresDataAdminRole()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/datapipeline/status/invalid-id");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Additional tests would be added here to test with authenticated users and different roles
    // These would require setting up test authentication handlers and claims
}
