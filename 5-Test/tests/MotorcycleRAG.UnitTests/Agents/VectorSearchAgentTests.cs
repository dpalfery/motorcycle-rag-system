using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Core.Agents;
using MotorcycleRAG.Core.Interfaces;
using MotorcycleRAG.Core.Models;
using Xunit;

namespace MotorcycleRAG.UnitTests.Agents;

/// <summary>
/// Unit tests for VectorSearchAgent
/// Tests hybrid search functionality, result ranking, and filtering logic
/// </summary>
public class VectorSearchAgentTests : IDisposable
{
    private readonly Mock<IAzureSearchClient> _mockSearchClient;
    private readonly Mock<IAzureOpenAIClient> _mockOpenAIClient;
    private readonly Mock<ILogger<VectorSearchAgent>> _mockLogger;
    private readonly IOptions<SearchConfiguration> _searchConfig;
    private readonly VectorSearchAgent _vectorSearchAgent;

    public VectorSearchAgentTests()
    {
        _mockSearchClient = new Mock<IAzureSearchClient>();
        _mockOpenAIClient = new Mock<IAzureOpenAIClient>();
        _mockLogger = new Mock<ILogger<VectorSearchAgent>>();
        
        _searchConfig = Options.Create(new SearchConfiguration
        {
            IndexName = "test-motorcycle-index",
            BatchSize = 100,
            MaxSearchResults = 50,
            EnableHybridSearch = true,
            EnableSemanticRanking = true
        });

        _vectorSearchAgent = new VectorSearchAgent(
            _mockSearchClient.Object,
            _mockOpenAIClient.Object,
            _searchConfig,
            _mockLogger.Object);
    }

    [Fact]
    public void AgentType_ShouldReturnVectorSearch()
    {
        // Act
        var agentType = _vectorSearchAgent.AgentType;

        // Assert
        Assert.Equal(SearchAgentType.VectorSearch, agentType);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenRequiredParametersAreNull()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new VectorSearchAgent(
            null!, _mockOpenAIClient.Object, _searchConfig, _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() => new VectorSearchAgent(
            _mockSearchClient.Object, null!, _searchConfig, _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() => new VectorSearchAgent(
            _mockSearchClient.Object, _mockOpenAIClient.Object, null!, _mockLogger.Object));

        Assert.Throws<ArgumentNullException>(() => new VectorSearchAgent(
            _mockSearchClient.Object, _mockOpenAIClient.Object, _searchConfig, null!));
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmptyArray_WhenQueryIsEmpty()
    {
        // Arrange
        var searchOptions = CreateDefaultSearchOptions();

        // Act
        var results = await _vectorSearchAgent.SearchAsync("", searchOptions);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmptyArray_WhenQueryIsNull()
    {
        // Arrange
        var searchOptions = CreateDefaultSearchOptions();

        // Act
        var results = await _vectorSearchAgent.SearchAsync(null!, searchOptions);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ShouldExecuteSearch_WhenValidQueryProvided()
    {
        // Arrange
        var query = "Honda CBR1000RR specifications";
        var searchOptions = CreateDefaultSearchOptions();
        
        SetupMockSearchClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _vectorSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, result => 
        {
            Assert.NotEmpty(result.Id);
            Assert.NotEmpty(result.Content);
            Assert.True(result.RelevanceScore > 0);
            Assert.Equal(SearchAgentType.VectorSearch, result.Source.AgentType);
        });

        // Verify search was called (provide all parameters explicitly)
        _mockSearchClient.Verify(x => x.SearchAsync(
            It.Is<string>(s => s == query), 
            It.IsAny<int>(), 
            It.Is<CancellationToken>(ct => ct == CancellationToken.None)
        ), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_ShouldApplyMinRelevanceScoreFilter_WhenFilterSet()
    {
        // Arrange
        var query = "Yamaha R1 engine specs";
        var searchOptions = new SearchOptions
        {
            MaxResults = 10,
            MinRelevanceScore = 0.8f,
            IncludeMetadata = true
        };
        
        SetupMockSearchClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _vectorSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.All(results, result => 
            Assert.True(result.RelevanceScore >= searchOptions.MinRelevanceScore));
    }

    [Fact]
    public async Task SearchAsync_ShouldRespectMaxResultsLimit_WhenLimitSet()
    {
        // Arrange
        var query = "Kawasaki Ninja performance";
        var searchOptions = new SearchOptions
        {
            MaxResults = 3,
            MinRelevanceScore = 0.0f,
            IncludeMetadata = true
        };
        
        SetupMockSearchClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _vectorSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.True(results.Length <= searchOptions.MaxResults);
    }

    [Fact]
    public async Task SearchAsync_ShouldIncludeMetadata_WhenIncludeMetadataIsTrue()
    {
        // Arrange
        var query = "Ducati Panigale features";
        var searchOptions = new SearchOptions
        {
            MaxResults = 5,
            MinRelevanceScore = 0.0f,
            IncludeMetadata = true
        };
        
        SetupMockSearchClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _vectorSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.All(results, result => 
        {
            Assert.Contains("searchQuery", result.Metadata.Keys);
            Assert.Contains("searchTimestamp", result.Metadata.Keys);
            Assert.Contains("agentType", result.Metadata.Keys);
            Assert.Equal(query, result.Metadata["searchQuery"]);
        });
    }

    [Fact]
    public async Task SearchAsync_ShouldHandleOpenAIFailure_WhenEmbeddingGenerationFails()
    {
        // Arrange
        var query = "BMW S1000RR specifications";
        var searchOptions = CreateDefaultSearchOptions();
        
        SetupMockSearchClient();
        SetupMockOpenAIClientFailure();

        // Act
        var results = await _vectorSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        // Should still return keyword search results
        Assert.NotEmpty(results);
        
        // Verify keyword search was still executed (provide all parameters explicitly)
        _mockSearchClient.Verify(x => x.SearchAsync(
            It.Is<string>(s => s == query), 
            It.IsAny<int>(), 
            It.Is<CancellationToken>(ct => ct == CancellationToken.None)
        ), Times.Once);
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
        
        SetupMockSearchClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _vectorSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, result => 
        {
            Assert.NotEmpty(result.Content);
            Assert.True(result.RelevanceScore > 0);
            Assert.Equal(SearchAgentType.VectorSearch, result.Source.AgentType);
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
            Filters = new Dictionary<string, object>()
        };
    }

    private void SetupMockSearchClient()
    {
        var mockResults = new[]
        {
            new SearchResult
            {
                Id = "doc1",
                Content = "Honda CBR1000RR specifications include 999cc engine with 217hp power output",
                RelevanceScore = 0.9f,
                Source = new SearchSource
                {
                    AgentType = SearchAgentType.VectorSearch,
                    SourceName = "Test Source",
                    DocumentId = "doc1"
                },
                Metadata = new Dictionary<string, object>
                {
                    ["make"] = "Honda",
                    ["model"] = "CBR1000RR"
                }
            },
            new SearchResult
            {
                Id = "doc2",
                Content = "Performance testing of Honda CBR1000RR on track conditions",
                RelevanceScore = 0.8f,
                Source = new SearchSource
                {
                    AgentType = SearchAgentType.VectorSearch,
                    SourceName = "Test Source",
                    DocumentId = "doc2"
                },
                Metadata = new Dictionary<string, object>
                {
                    ["make"] = "Honda",
                    ["category"] = "Performance"
                }
            }
        };

        // Setup with explicit parameters to avoid expression tree issues
        _mockSearchClient.Setup(x => x.SearchAsync(
            It.IsAny<string>(), 
            It.IsAny<int>(), 
            It.Is<CancellationToken>(ct => ct == CancellationToken.None)
        )).ReturnsAsync(mockResults);
    }

    private void SetupMockOpenAIClient()
    {
        var mockEmbedding = new float[1536];
        for (int i = 0; i < mockEmbedding.Length; i++)
        {
            mockEmbedding[i] = (float)(i * 0.001);
        }

        // Setup with specific overload to avoid expression tree issues
        _mockOpenAIClient.Setup(x => x.GetEmbeddingAsync(
            It.Is<string>(s => s == "text-embedding-3-large"), 
            It.IsAny<string>(), 
            It.Is<CancellationToken>(ct => ct == CancellationToken.None)))
            .ReturnsAsync(mockEmbedding);
    }

    private void SetupMockOpenAIClientFailure()
    {
        // Setup failure for any embedding call
        _mockOpenAIClient.Setup(x => x.GetEmbeddingAsync(
            It.Is<string>(s => s == "text-embedding-3-large"), 
            It.IsAny<string>(), 
            It.Is<CancellationToken>(ct => ct == CancellationToken.None)))
            .ThrowsAsync(new InvalidOperationException("OpenAI service unavailable"));
    }

    #endregion

    public void Dispose()
    {
        // No explicit cleanup needed for this test class
    }
} 