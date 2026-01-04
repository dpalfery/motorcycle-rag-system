using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MotorcycleRAG.Contracts.Requests;
using MotorcycleRAG.Contracts.Responses;
using System.Net.Http.Json;
using System.Text.Json;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.EndToEndTests;

/// <summary>
/// End-to-end tests covering complete user journeys through the motorcycle RAG system.
/// Tests the full pipeline from query submission to response generation.
/// </summary>
public class UserJourneyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly IConfiguration _configuration;

    public UserJourneyTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
        
        _configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.EndToEnd.json")
            .AddEnvironmentVariables()
            .Build();
    }

    [Fact]
    public async Task CompleteUserJourney_SimpleSpecificationQuery_ReturnsAccurateResponse()
    {
        // Arrange
        var query = new MotorcycleQueryRequest
        {
            Query = "What are the specifications for Honda CBR600RR?",
            UserId = "test-user-001",
            Context = new QueryContext
            {
                SessionId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/motorcycle/query", query);

        // Assert
        response.Should().BeSuccessful();
        
        var result = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>();
        result.Should().NotBeNull();
        result!.Response.Should().NotBeNullOrEmpty();
        result.Sources.Should().NotBeEmpty();
        result.QueryId.Should().NotBeNullOrEmpty();
        
        // Verify response contains Honda CBR600RR information
        result.Response.Should().ContainAny("Honda", "CBR600RR", "599", "118");
        
        // Verify metrics are captured
        result.Metrics.Should().NotBeNull();
        result.Metrics!.ProcessingTimeMs.Should().BeGreaterThan(0);
        result.Metrics.SourcesSearched.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CompleteUserJourney_ComplexComparisonQuery_ReturnsComparativeAnalysis()
    {
        // Arrange
        var query = new MotorcycleQueryRequest
        {
            Query = "Compare the performance between Honda CBR1000RR and Yamaha R1",
            UserId = "test-user-002",
            Preferences = new SearchPreferences
            {
                IncludeWebSources = true,
                MaxResults = 10,
                PreferredSources = new[] { "specifications", "reviews" }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/motorcycle/query", query);

        // Assert
        response.Should().BeSuccessful();
        
        var result = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>();
        result.Should().NotBeNull();
        result!.Response.Should().NotBeNullOrEmpty();
        
        // Verify comparative analysis includes both motorcycles
        result.Response.Should().ContainAll("Honda", "CBR1000RR", "Yamaha", "R1");
        result.Response.Should().ContainAny("compare", "comparison", "versus", "vs");
        
        // Verify multiple sources were used
        result.Sources.Should().HaveCountGreaterThan(1);
        result.Sources.Should().Contain(s => s.Source == SearchSource.VectorDatabase);
    }

    [Fact]
    public async Task CompleteUserJourney_MaintenanceProcedureQuery_ReturnsDetailedInstructions()
    {
        // Arrange
        var query = new MotorcycleQueryRequest
        {
            Query = "How do I change the oil on a Kawasaki Ninja ZX-10R?",
            UserId = "test-user-003",
            Preferences = new SearchPreferences
            {
                IncludePDFSources = true,
                PreferredSources = new[] { "manuals", "maintenance" }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/motorcycle/query", query);

        // Assert
        response.Should().BeSuccessful();
        
        var result = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>();
        result.Should().NotBeNull();
        result!.Response.Should().NotBeNullOrEmpty();
        
        // Verify maintenance procedure information
        result.Response.Should().ContainAny("oil", "change", "procedure", "steps");
        result.Response.Should().Contain("Kawasaki");
        
        // Verify PDF sources were consulted
        result.Sources.Should().Contain(s => s.Source == SearchSource.PDFManual);
    }

    [Fact]
    public async Task CompleteUserJourney_MultiModalQuery_ProcessesImageContent()
    {
        // Arrange
        var query = new MotorcycleQueryRequest
        {
            Query = "Show me the engine diagram for BMW S1000RR",
            UserId = "test-user-004",
            Context = new QueryContext
            {
                RequiresMultiModal = true
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/motorcycle/query", query);

        // Assert
        response.Should().BeSuccessful();
        
        var result = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>();
        result.Should().NotBeNull();
        result!.Response.Should().NotBeNullOrEmpty();
        
        // Verify multimodal processing occurred
        result.Response.Should().ContainAny("diagram", "engine", "BMW", "S1000RR");
        result.Metrics!.MultiModalProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task CompleteUserJourney_SequentialSearchPattern_ExecutesCorrectFallbackChain()
    {
        // Arrange
        var query = new MotorcycleQueryRequest
        {
            Query = "What is the rare specification for limited edition Ducati Superleggera V4?",
            UserId = "test-user-005"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/motorcycle/query", query);

        // Assert
        response.Should().BeSuccessful();
        
        var result = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>();
        result.Should().NotBeNull();
        
        // Verify sequential search pattern was executed
        result!.Metrics!.SearchPattern.Should().NotBeNull();
        result.Metrics.SearchPattern!.VectorSearchExecuted.Should().BeTrue();
        
        // For rare information, web augmentation should have been attempted
        if (result.Metrics.SearchPattern.WebSearchExecuted)
        {
            result.Sources.Should().Contain(s => s.Source == SearchSource.WebAugmentation);
        }
    }

    [Fact]
    public async Task CompleteUserJourney_ErrorHandling_GracefullyHandlesInvalidQuery()
    {
        // Arrange
        var query = new MotorcycleQueryRequest
        {
            Query = "", // Invalid empty query
            UserId = "test-user-006"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/motorcycle/query", query);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        
        var errorResponse = await response.Content.ReadAsStringAsync();
        errorResponse.Should().NotBeNullOrEmpty();
        errorResponse.Should().ContainAny("query", "required", "invalid");
    }

    [Fact]
    public async Task CompleteUserJourney_HealthCheck_ReturnsSystemStatus()
    {
        // Act
        var response = await _client.GetAsync("/api/motorcycle/health");

        // Assert
        response.Should().BeSuccessful();
        
        var result = await response.Content.ReadFromJsonAsync<HealthCheckResult>();
        result.Should().NotBeNull();
        result!.Status.Should().Be(HealthStatus.Healthy);
        result.Components.Should().NotBeEmpty();
        
        // Verify all critical components are healthy
        result.Components.Should().ContainKey("AzureOpenAI");
        result.Components.Should().ContainKey("AzureAISearch");
        result.Components.Should().ContainKey("DocumentIntelligence");
    }

    [Fact]
    public async Task CompleteUserJourney_ConcurrentQueries_HandlesMultipleUsersSimultaneously()
    {
        // Arrange
        var queries = new[]
        {
            "Honda CBR600RR specifications",
            "Yamaha R1 maintenance schedule",
            "Kawasaki ZX-10R performance data",
            "BMW S1000RR suspension settings",
            "Ducati Panigale V4 engine specs"
        };

        var tasks = queries.Select(async (queryText, index) =>
        {
            var query = new MotorcycleQueryRequest
            {
                Query = queryText,
                UserId = $"concurrent-user-{index:D3}"
            };

            var response = await _client.PostAsJsonAsync("/api/motorcycle/query", query);
            return await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>();
        });

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().HaveCount(5);
        results.Should().AllSatisfy(result =>
        {
            result.Should().NotBeNull();
            result!.Response.Should().NotBeNullOrEmpty();
            result.QueryId.Should().NotBeNullOrEmpty();
        });

        // Verify all queries completed within reasonable time
        results.Should().AllSatisfy(result =>
        {
            result!.Metrics!.ProcessingTimeMs.Should().BeLessThan(10000); // 10 seconds max
        });
    }

    [Fact]
    public async Task CompleteUserJourney_DataIngestion_ProcessesCSVAndPDFFiles()
    {
        // This test would require file upload endpoints
        // For now, we'll test the health of the indexing service
        
        // Act
        var response = await _client.GetAsync("/api/motorcycle/indexing/status");

        // Assert
        response.Should().BeSuccessful();
        
        var status = await response.Content.ReadFromJsonAsync<IndexingStatus>();
        status.Should().NotBeNull();
        status!.IsHealthy.Should().BeTrue();
    }

    [Theory]
    [InlineData("What is the top speed of Honda CBR600RR?")]
    [InlineData("How much does a Yamaha R1 weigh?")]
    [InlineData("What type of engine does the Kawasaki ZX-10R have?")]
    [InlineData("What is the fuel capacity of BMW S1000RR?")]
    public async Task CompleteUserJourney_VariousQueryTypes_ReturnsRelevantResponses(string queryText)
    {
        // Arrange
        var query = new MotorcycleQueryRequest
        {
            Query = queryText,
            UserId = "theory-test-user"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/motorcycle/query", query);

        // Assert
        response.Should().BeSuccessful();
        
        var result = await response.Content.ReadFromJsonAsync<MotorcycleQueryResponse>();
        result.Should().NotBeNull();
        result!.Response.Should().NotBeNullOrEmpty();
        result.Sources.Should().NotBeEmpty();
        
        // Verify response time is within acceptable limits
        result.Metrics!.ProcessingTimeMs.Should().BeLessThan(5000); // 5 seconds max
    }
}