using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models;
using Xunit;
using MotorcycleRAG.Domain.Models;

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

    [Fact]
    public async Task QueryEndpoint_ReturnsCitationsAndSources_WhenValidRequest()
    {
        var client = _factory.CreateClient();

        var request = new MotorcycleQueryRequest
        {
            Query = "What are the specifications of Honda CBR1000RR?"
        };

        var response = await client.PostAsJsonAsync("/api/motorcycles/query", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>();
        Assert.NotNull(body);

        // Verify response structure
        Assert.False(string.IsNullOrWhiteSpace(body!.Response));
        Assert.False(string.IsNullOrWhiteSpace(body.QueryId));
        Assert.NotEqual(DateTime.MinValue, body.GeneratedAt);

        // Verify citations are present in sources
        Assert.NotEmpty(body.Sources);
        foreach (var source in body.Sources)
        {
            Assert.NotNull(source.Source);
            // Note: Mock service doesn't populate citations, but we verify the structure supports them
            Assert.NotNull(source.Source.Citation); // Should be null but not throw exception
        }

        // Verify metrics are complete
        Assert.NotNull(body.Metrics);
        Assert.NotEqual(TimeSpan.Zero, body.Metrics.TotalDuration);
        Assert.Equal(body.Sources.Length, body.Metrics.ResultsFound);
    }

    [Fact]
    public async Task QueryEndpoint_HandlesNoResultsWithRefinementSuggestions()
    {
        // Create a mock that returns empty results
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IMotorcycleRAGService));
                if (descriptor is not null)
                {
                    services.Remove(descriptor);
                }

                var mockService = new Mock<IMotorcycleRAGService>();

                mockService.Setup(s => s.QueryAsync(It.IsAny<MotorcycleQueryRequest>()))
                            .ReturnsAsync((MotorcycleQueryRequest r) => new MotorcycleQueryResponse
                            {
                                QueryId = Guid.NewGuid().ToString("N"),
                                Response = "No results found. Try refining your query with more specific terms about motorcycle models, specifications, or maintenance procedures.",
                                GeneratedAt = DateTime.UtcNow,
                                Sources = Array.Empty<SearchResult>(),
                                Metrics = new QueryMetrics
                                {
                                    ResultsFound = 0,
                                    TotalDuration = TimeSpan.FromMilliseconds(100)
                                }
                            });

                services.AddSingleton(mockService.Object);
            });
        });

        var client = factory.CreateClient();
        var request = new MotorcycleQueryRequest { Query = "Some obscure query with no results" };

        var response = await client.PostAsJsonAsync("/api/motorcycles/query", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>();
        Assert.NotNull(body);

        // Verify no-results response structure
        Assert.Contains("No results found", body!.Response);
        Assert.Contains("refining your query", body.Response);
        Assert.Empty(body.Sources);
        Assert.Equal(0, body.Metrics.ResultsFound);
        Assert.False(string.IsNullOrWhiteSpace(body.QueryId));
    }

    [Fact]
    public async Task QueryEndpoint_ReturnsStableQueryIdAndCompleteMetrics()
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

        // Verify stable query ID format (GUID without hyphens)
        Assert.False(string.IsNullOrWhiteSpace(body!.QueryId));
        Assert.DoesNotContain("-", body.QueryId);
        Assert.Equal(32, body.QueryId.Length);

        // Verify complete metrics structure
        Assert.NotNull(body.Metrics);
        Assert.NotEqual(TimeSpan.Zero, body.Metrics.TotalDuration);
        Assert.NotEqual(0, body.Metrics.ProcessingTimeMs);
        Assert.False(string.IsNullOrWhiteSpace(body.Response));
        Assert.NotEqual(DateTime.MinValue, body.GeneratedAt);

        // Verify metrics include expected fields
        Assert.NotEqual(0, body.Metrics.ResultsFound);
        Assert.False(body.Metrics.CacheHit); // Should be false for mock responses
    }
}
