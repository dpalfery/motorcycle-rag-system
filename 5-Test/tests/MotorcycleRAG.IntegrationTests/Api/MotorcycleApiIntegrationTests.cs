using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using Xunit;

namespace MotorcycleRAG.IntegrationTests.Api;

/// <summary>
/// Integration tests for the MotorcycleController REST API.
/// </summary>
public class MotorcycleApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MotorcycleApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        // Override IMotorcycleRAGService with a mocked implementation so that tests do not call external services.
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Remove existing registration (if any).
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IMotorcycleRAGService));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                // Register mock service.
                var mockService = new Mock<IMotorcycleRAGService>();

                mockService.Setup(s => s.QueryAsync(It.IsAny<MotorcycleQueryRequest>()))
                            .ReturnsAsync((MotorcycleQueryRequest r) => new MotorcycleQueryResponse
                            {
                                QueryId = Guid.NewGuid().ToString("N"),
                                Response = $"Echo: {r.Query}",
                                GeneratedAt = DateTime.UtcNow,
                                Sources = Array.Empty<SearchResult>(),
                                Metrics = new QueryMetrics()
                            });

                mockService.Setup(s => s.GetHealthAsync())
                            .ReturnsAsync(new HealthCheckResult { IsHealthy = true, Status = "OK" });

                services.AddSingleton(mockService.Object);
            });
        });
    }

    [Fact]
    public async Task QueryEndpoint_ReturnsOkAndResponseBody()
    {
        var client = _factory.CreateClient();

        var request = new MotorcycleQueryRequest
        {
            Query = "What is the top speed of Ducati Panigale V4?"
        };

        var response = await client.PostAsJsonAsync("/api/motorcycles/query", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>();
        Assert.NotNull(body);
        Assert.StartsWith("Echo:", body!.Response);
    }

    [Fact]
    public async Task QueryEndpoint_InvalidModel_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();

        var request = new MotorcycleQueryRequest { Query = string.Empty }; // Invalid due to [Required]

        var response = await client.PostAsJsonAsync("/api/motorcycles/query", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/motorcycles/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthCheckResult>();
        Assert.NotNull(body);
        Assert.True(body!.IsHealthy);
    }
}