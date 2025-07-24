using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Contrib.HttpClient;
using MotorcycleRAG.Core.Agents;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using System.Net;
using System.Text;
using Xunit;

namespace MotorcycleRAG.UnitTests.Agents;

/// <summary>
/// Unit tests for WebSearchAgent
/// Tests web scraping functionality, rate limiting, and credibility validation
/// </summary>
public class WebSearchAgentTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<IAzureOpenAIClient> _mockOpenAIClient;
    private readonly Mock<ILogger<WebSearchAgent>> _mockLogger;
    private readonly IOptions<WebSearchConfiguration> _webSearchConfig;
    private readonly WebSearchAgent _webSearchAgent;

    public WebSearchAgentTests()
    {
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpHandler.Object);
        _mockOpenAIClient = new Mock<IAzureOpenAIClient>();
        _mockLogger = new Mock<ILogger<WebSearchAgent>>();
        
        _webSearchConfig = Options.Create(new WebSearchConfiguration
        {
            MaxConcurrentRequests = 3,
            MinRequestIntervalMs = 100, // Reduced for testing
            RequestTimeoutSeconds = 30,
            MinCredibilityScore = 0.6f,
            SearchTermModel = "gpt-4o-mini",
            ValidationModel = "gpt-4o-mini",
            TrustedSources = new List<TrustedSource>
            {
                new TrustedSource
                {
                    Name = "Test Motorcycle Site",
                    BaseUrl = "https://test-motorcycle.com",
                    SearchUrlTemplate = "https://test-motorcycle.com/search?q={query}",
                    ContentSelector = "//article//p",
                    CredibilityScore = 0.9f
                }
            }
        });

        _webSearchAgent = new WebSearchAgent(
            _httpClient,
            _mockOpenAIClient.Object,
            _webSearchConfig,
            _mockLogger.Object);
    }

    [Fact]
    public void AgentType_ShouldReturnWebSearch()
    {
        // Act
        var agentType = _webSearchAgent.AgentType;

        // Assert
        Assert.Equal(SearchAgentType.WebSearch, agentType);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenRequiredParametersAreNull()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new WebSearchAgent(
            null!, _mockOpenAIClient.Object, _webSearchConfig, _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() => new WebSearchAgent(
            _httpClient, null!, _webSearchConfig, _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() => new WebSearchAgent(
            _httpClient, _mockOpenAIClient.Object, null!, _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() => new WebSearchAgent(
            _httpClient, _mockOpenAIClient.Object, _webSearchConfig, null!));
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmptyArray_WhenQueryIsEmpty()
    {
        // Arrange
        var searchOptions = CreateDefaultSearchOptions();

        // Act
        var results = await _webSearchAgent.SearchAsync("", searchOptions);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmptyArray_WhenQueryIsNull()
    {
        // Arrange
        var searchOptions = CreateDefaultSearchOptions();

        // Act
        var results = await _webSearchAgent.SearchAsync(null!, searchOptions);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ShouldExecuteWebSearch_WhenValidQueryProvided()
    {
        // Arrange
        var query = "Honda CBR1000RR specifications";
        var searchOptions = CreateDefaultSearchOptions();
        
        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, result => 
        {
            Assert.NotEmpty(result.Id);
            Assert.NotEmpty(result.Content);
            Assert.True(result.RelevanceScore > 0);
            Assert.Equal(SearchAgentType.WebSearch, result.Source.AgentType);
            Assert.Contains("[Web Source:", result.Content);
        });
    }

    [Fact]
    public async Task SearchAsync_ShouldApplyRateLimiting_WhenMultipleRequestsMade()
    {
        // Arrange
        var query = "Yamaha R1 engine specs";
        var searchOptions = CreateDefaultSearchOptions();
        
        SetupMockHttpClient();
        SetupMockOpenAIClient();

        var startTime = DateTime.UtcNow;

        // Act - Make multiple requests
        var task1 = _webSearchAgent.SearchAsync(query, searchOptions);
        var task2 = _webSearchAgent.SearchAsync(query + " performance", searchOptions);
        
        await Task.WhenAll(task1, task2);

        var endTime = DateTime.UtcNow;
        var duration = endTime - startTime;

        // Assert - Should take at least the minimum interval time
        Assert.True(duration.TotalMilliseconds >= 100); // MinRequestIntervalMs from config
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnCachedResults_WhenCachingEnabledAndResultsExist()
    {
        // Arrange
        var query = "Kawasaki Ninja performance";
        var searchOptions = new SearchOptions
        {
            MaxResults = 5,
            MinRelevanceScore = 0.0f,
            EnableCaching = true
        };
        
        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act - First search to populate cache
        var firstResults = await _webSearchAgent.SearchAsync(query, searchOptions);
        
        // Act - Second search should use cache
        var secondResults = await _webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.NotEmpty(firstResults);
        Assert.NotEmpty(secondResults);
        Assert.Equal(firstResults.Length, secondResults.Length);
    }

    [Fact]
    public async Task SearchAsync_ShouldApplyMinRelevanceScoreFilter_WhenFilterSet()
    {
        // Arrange
        var query = "Ducati Panigale features";
        var searchOptions = new SearchOptions
        {
            MaxResults = 10,
            MinRelevanceScore = 0.8f,
            IncludeMetadata = true
        };
        
        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.All(results, result => 
            Assert.True(result.RelevanceScore >= searchOptions.MinRelevanceScore));
    }

    [Fact]
    public async Task SearchAsync_ShouldRespectMaxResultsLimit_WhenLimitSet()
    {
        // Arrange
        var query = "BMW S1000RR specifications";
        var searchOptions = new SearchOptions
        {
            MaxResults = 3,
            MinRelevanceScore = 0.0f,
            IncludeMetadata = true
        };
        
        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.True(results.Length <= searchOptions.MaxResults);
    }

    [Fact]
    public async Task SearchAsync_ShouldIncludeWebSourceMetadata_WhenIncludeMetadataIsTrue()
    {
        // Arrange
        var query = "Suzuki GSX-R1000 features";
        var searchOptions = new SearchOptions
        {
            MaxResults = 5,
            MinRelevanceScore = 0.0f,
            IncludeMetadata = true
        };
        
        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.All(results, result => 
        {
            Assert.Contains("searchTerm", result.Metadata.Keys);
            Assert.Contains("sourceType", result.Metadata.Keys);
            Assert.Contains("credibilityScore", result.Metadata.Keys);
            Assert.Contains("integrationType", result.Metadata.Keys);
            Assert.Equal("web", result.Metadata["sourceType"]);
            Assert.Equal("webAugmentation", result.Metadata["integrationType"]);
        });
    }

    [Fact]
    public async Task SearchAsync_ShouldHandleHttpErrors_WhenWebRequestFails()
    {
        // Arrange
        var query = "KTM Duke specifications";
        var searchOptions = CreateDefaultSearchOptions();
        
        SetupMockHttpClientFailure();
        SetupMockOpenAIClient();

        // Act
        var results = await _webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        // Should handle errors gracefully and return empty results
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ShouldHandleOpenAIFailure_WhenSearchTermGenerationFails()
    {
        // Arrange
        var query = "Aprilia RSV4 specifications";
        var searchOptions = CreateDefaultSearchOptions();
        
        SetupMockHttpClient();
        SetupMockOpenAIClientFailure();

        // Act & Assert
        // Should not throw an exception even when OpenAI fails
        var exception = await Record.ExceptionAsync(async () =>
        {
            var results = await _webSearchAgent.SearchAsync(query, searchOptions);
        });
        
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("Honda CBR1000RR")]
    [InlineData("Yamaha YZF-R1")]
    [InlineData("Kawasaki Ninja ZX-10R")]
    [InlineData("Ducati Panigale V4")]
    public async Task SearchAsync_ShouldHandleMotorcycleSpecificQueries_WhenDifferentBrandsQueried(string query)
    {
        // Arrange
        var searchOptions = CreateDefaultSearchOptions();
        
        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, result => 
        {
            Assert.NotEmpty(result.Content);
            Assert.True(result.RelevanceScore > 0);
            Assert.Equal(SearchAgentType.WebSearch, result.Source.AgentType);
            Assert.Contains("[Web Source:", result.Content);
        });
    }

    [Fact]
    public async Task SearchAsync_ShouldValidateSourceCredibility_WhenCredibilityCheckEnabled()
    {
        // Arrange
        var query = "Triumph Street Triple specifications";
        var searchOptions = CreateDefaultSearchOptions();
        
        SetupMockHttpClient();
        SetupMockOpenAIClientWithValidation();

        // Act
        var results = await _webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.All(results, result => 
        {
            Assert.Contains("contentQuality", result.Metadata.Keys);
            Assert.Contains("validationPassed", result.Metadata.Keys);
            Assert.True((bool)result.Metadata["validationPassed"]);
        });
    }

    #region Helper Methods

    private SearchOptions CreateDefaultSearchOptions()
    {
        return new SearchOptions
        {
            MaxResults = 10,
            MinRelevanceScore = 0.5f,
            IncludeMetadata = true,
            Filters = new Dictionary<string, object>(),
            EnableCaching = false // Disable caching for most tests
        };
    }

    private void SetupMockHttpClient()
    {
        var mockHtmlContent = @"
            <html>
                <body>
                    <article>
                        <p>Honda CBR1000RR specifications include a 999cc inline-four engine producing 217 horsepower. The motorcycle features advanced electronics and aerodynamics for superior performance on track and street.</p>
                        <p>Performance testing shows excellent acceleration and handling characteristics. The bike weighs 201kg and has a top speed of over 300 km/h.</p>
                    </article>
                </body>
            </html>";

        _mockHttpHandler.SetupAnyRequest()
            .ReturnsResponse(HttpStatusCode.OK, mockHtmlContent, "text/html");
    }

    private void SetupMockHttpClientFailure()
    {
        _mockHttpHandler.SetupAnyRequest()
            .ReturnsResponse(HttpStatusCode.NotFound);
    }

    private void SetupMockOpenAIClient()
    {
        // Setup search term generation
        _mockOpenAIClient.Setup(x => x.GetChatCompletionAsync(
            It.Is<string>(s => s == "gpt-4o-mini"), 
            It.IsAny<string>(), 
            It.Is<CancellationToken>(ct => ct == CancellationToken.None)))
            .ReturnsAsync("Honda CBR1000RR specifications\nHonda CBR performance\nCBR1000RR engine specs");

        // Setup content validation with simple response
        _mockOpenAIClient.Setup(x => x.GetChatCompletionAsync(
            It.Is<string>(s => s == "gpt-4o-mini"), 
            It.Is<string>(prompt => prompt.Contains("Analyze this motorcycle-related content")), 
            It.Is<CancellationToken>(ct => ct == CancellationToken.None)))
            .ReturnsAsync("{\"qualityScore\": 0.8, \"isValid\": true, \"reasoning\": \"Good technical content\"}");
    }

    private void SetupMockOpenAIClientWithValidation()
    {
        // Setup search term generation
        _mockOpenAIClient.Setup(x => x.GetChatCompletionAsync(
            It.Is<string>(s => s == "gpt-4o-mini"), 
            It.IsAny<string>(), 
            It.Is<CancellationToken>(ct => ct == CancellationToken.None)))
            .ReturnsAsync("Triumph Street Triple specifications\nTriumph performance data\nStreet Triple engine specs");

        // Setup content validation with high quality response
        _mockOpenAIClient.Setup(x => x.GetChatCompletionAsync(
            It.Is<string>(s => s == "gpt-4o-mini"), 
            It.Is<string>(prompt => prompt.Contains("Analyze this motorcycle-related content")), 
            It.Is<CancellationToken>(ct => ct == CancellationToken.None)))
            .ReturnsAsync("{\"qualityScore\": 0.9, \"isValid\": true, \"reasoning\": \"Excellent technical specifications\"}");
    }

    private void SetupMockOpenAIClientFailure()
    {
        // Setup failure for search term generation only
        _mockOpenAIClient.Setup(x => x.GetChatCompletionAsync(
            It.Is<string>(s => s == "gpt-4o-mini"), 
            It.Is<string>(prompt => prompt.Contains("Generate 3-5 specific search terms")), 
            It.Is<CancellationToken>(ct => ct == CancellationToken.None)))
            .ThrowsAsync(new InvalidOperationException("OpenAI service unavailable"));

        // Setup success for content validation
        _mockOpenAIClient.Setup(x => x.GetChatCompletionAsync(
            It.Is<string>(s => s == "gpt-4o-mini"), 
            It.Is<string>(prompt => prompt.Contains("Analyze this motorcycle-related content")), 
            It.Is<CancellationToken>(ct => ct == CancellationToken.None)))
            .ReturnsAsync("{\"qualityScore\": 0.8, \"isValid\": true, \"reasoning\": \"Good technical content\"}");
    }

    #endregion

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}