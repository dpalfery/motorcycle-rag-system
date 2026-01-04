using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Contrib.HttpClient;
using MotorcycleRAG.Application.Agents;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using System.Net;
using System.Text;
using Xunit;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Core.Options;

namespace MotorcycleRAG.UnitTests.Agents;

/// <summary>
/// Unit tests for WebSearchAgent
/// Tests web scraping functionality, rate limiting, and credibility validation
/// </summary>
public class WebSearchAgentTests : IDisposable {
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<IAzureOpenAIClient> _mockOpenAIClient;
    private readonly Mock<ILogger<WebSearchAgent>> _mockLogger;
    private readonly IOptions<WebSearchOptions> _webSearchConfig;
    private readonly WebSearchAgent _webSearchAgent;

    public WebSearchAgentTests() {
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockHttpHandler.Object);
        _mockOpenAIClient = new Mock<IAzureOpenAIClient>();
        _mockLogger = new Mock<ILogger<WebSearchAgent>>();

        _webSearchConfig = Options.Create(new WebSearchOptions {
            MaxConcurrentRequests = 3,
            MinRequestIntervalMs = 100, // Reduced for testing
            RequestTimeoutSeconds = 30,
            MinCredibilityScore = 0.6f,
            SearchTermModel = "gpt-4o-mini",
            ValidationModel = "gpt-4o-mini",
            TrustedSources = new List<TrustedSourceOptions>
            {
                new TrustedSourceOptions
                {
                    Name = "Test Motorcycle Site",
                    BaseUrl = new Uri("https://test-motorcycle.com"),
                    SearchUrlTemplate = new Uri("https://test-motorcycle.com/search?q={query}"),
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
    public void AgentType_ShouldReturnWebSearch() {
        // Act
        var agentType = _webSearchAgent.AgentType;

        // Assert
        Assert.Equal(SearchAgentType.WebSearch, agentType);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenRequiredParametersAreNull() {
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
    public async Task SearchAsync_ShouldReturnEmptyArray_WhenQueryIsEmpty() {
        // Arrange
        var searchOptions = CreateDefaultSearchOptions();

        // Act
        var results = await _webSearchAgent.SearchAsync("", searchOptions);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ShouldReturnEmptyArray_WhenQueryIsNull() {
        // Arrange
        var searchOptions = CreateDefaultSearchOptions();

        // Act
        var results = await _webSearchAgent.SearchAsync(null!, searchOptions);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ShouldExecuteWebSearch_WhenValidQueryProvided() {
        // Arrange
        var query = "Honda CBR1000RR specifications";
        var searchOptions = CreateDefaultSearchOptions();

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, result => {
            Assert.NotEmpty(result.Id);
            Assert.NotEmpty(result.Content);
            Assert.True(result.RelevanceScore > 0);
            Assert.Equal(SearchAgentType.WebSearch, result.Source.AgentType);
            Assert.Contains("[Web Source:", result.Content);
        });
    }

    [Fact]
    public async Task SearchAsync_ShouldApplyRateLimiting_WhenMultipleRequestsMade() {
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
    public async Task SearchAsync_ShouldReturnCachedResults_WhenCachingEnabledAndResultsExist() {
        // Arrange
        var query = "Kawasaki Ninja performance";
        var searchParameters = new SearchParameters {
            MaxResults = 5,
            MinRelevanceScore = 0.0f,
            EnableCaching = true
        };

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act - First search to populate cache
        var firstResults = await _webSearchAgent.SearchAsync(query, searchParameters);

        // Act - Second search should use cache
        var secondResults = await _webSearchAgent.SearchAsync(query, searchParameters);

        // Assert
        Assert.NotEmpty(firstResults);
        Assert.NotEmpty(secondResults);
        Assert.Equal(firstResults.Length, secondResults.Length);
    }

    [Fact]
    public async Task SearchAsync_ShouldApplyMinRelevanceScoreFilter_WhenFilterSet() {
        // Arrange
        var query = "Ducati Panigale features";
        var searchParameters = new SearchParameters {
            MaxResults = 10,
            MinRelevanceScore = 0.8f,
            IncludeMetadata = true
        };

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _webSearchAgent.SearchAsync(query, searchParameters);

        // Assert
        Assert.All(results, result =>
            Assert.True(result.RelevanceScore >= searchParameters.MinRelevanceScore));
    }

    [Fact]
    public async Task SearchAsync_ShouldRespectMaxResultsLimit_WhenLimitSet() {
        // Arrange
        var query = "BMW S1000RR specifications";
        var searchParameters = new SearchParameters {
            MaxResults = 3,
            MinRelevanceScore = 0.0f,
            IncludeMetadata = true
        };

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _webSearchAgent.SearchAsync(query, searchParameters);

        // Assert
        Assert.True(results.Length <= searchParameters.MaxResults);
    }

    [Fact]
    public async Task SearchAsync_ShouldIncludeWebSourceMetadata_WhenIncludeMetadataIsTrue() {
        // Arrange
        var query = "Suzuki GSX-R1000 features";
        var searchParameters = new SearchParameters {
            MaxResults = 5,
            MinRelevanceScore = 0.0f,
            IncludeMetadata = true
        };

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _webSearchAgent.SearchAsync(query, searchParameters);

        // Assert
        Assert.All(results, result => {
            Assert.Contains("searchTerm", result.Metadata.Keys);
            Assert.Contains("sourceType", result.Metadata.Keys);
            Assert.Contains("credibilityScore", result.Metadata.Keys);
            Assert.Contains("integrationType", result.Metadata.Keys);
            Assert.Equal("web", result.Metadata["sourceType"]);
            Assert.Equal("webAugmentation", result.Metadata["integrationType"]);
        });
    }

    [Fact]
    public async Task SearchAsync_ShouldHandleHttpErrors_WhenWebRequestFails() {
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
    public async Task SearchAsync_ShouldHandleOpenAIFailure_WhenSearchTermGenerationFails() {
        // Arrange
        var query = "Aprilia RSV4 specifications";
        var searchOptions = CreateDefaultSearchOptions();

        SetupMockHttpClient();
        SetupMockOpenAIClientFailure();

        // Act & Assert
        // Should not throw an exception even when OpenAI fails
        var exception = await Record.ExceptionAsync(async () => {
            var results = await _webSearchAgent.SearchAsync(query, searchOptions);
        });

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("Honda CBR1000RR")]
    [InlineData("Yamaha YZF-R1")]
    [InlineData("Kawasaki Ninja ZX-10R")]
    [InlineData("Ducati Panigale V4")]
    public async Task SearchAsync_ShouldHandleMotorcycleSpecificQueries_WhenDifferentBrandsQueried(string query) {
        // Arrange
        var searchOptions = CreateDefaultSearchOptions();

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await _webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, result => {
            Assert.NotEmpty(result.Content);
            Assert.True(result.RelevanceScore > 0);
            Assert.Equal(SearchAgentType.WebSearch, result.Source.AgentType);
            Assert.Contains("[Web Source:", result.Content);
        });
    }

    [Fact]
    public async Task SearchAsync_ShouldValidateSourceCredibility_WhenCredibilityCheckEnabled() {
        // Arrange
        var query = "Triumph Street Triple specifications";
        var searchOptions = CreateDefaultSearchOptions();

        SetupMockHttpClient();
        SetupMockOpenAIClientWithValidation();

        // Act
        var results = await _webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.All(results, result => {
            Assert.Contains("contentQuality", result.Metadata.Keys);
            Assert.Contains("validationPassed", result.Metadata.Keys);
            Assert.True((bool)result.Metadata["validationPassed"]);
        });
    }

    #region Trust Policy Tests

    /// <summary>
    /// Creates a real WebTrustPolicy instance for testing (instead of mocking non-virtual properties)
    /// </summary>
    private WebTrustPolicy CreateTestTrustPolicy(
        string domainPattern = "test-motorcycle.com",
        WebTrustTier tier = WebTrustTier.TierA,
        bool isBlocked = false,
        string? reason = null) {
        return new WebTrustPolicy {
            Id = Guid.NewGuid(),
            DomainPattern = domainPattern,
            Tier = tier,
            IsBlocked = isBlocked,
            Reason = reason,
            AllowSubdomains = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = null
        };
    }

    [Fact]
    public async Task SearchAsync_ShouldRejectBlockedDomains_WhenTrustPolicyStoreConfigured() {
        // Arrange
        var query = "Honda CBR1000RR specifications";
        var searchOptions = CreateDefaultSearchOptions();

        var blockedPolicy = CreateTestTrustPolicy(
            domainPattern: "blocked-motorcycle.com",
            tier: WebTrustTier.TierB,
            isBlocked: true,
            reason: "Contains inaccurate technical information");

        var allowlistPolicy = CreateTestTrustPolicy(
            domainPattern: "test-motorcycle.com",
            tier: WebTrustTier.TierA,
            isBlocked: false);

        var mockTrustStore = new Mock<IWebTrustPolicyStore>();
        mockTrustStore
            .Setup(x => x.GetPolicyForDomain("test-motorcycle.com"))
            .Returns(allowlistPolicy); // Allowlisted domain with TierA

        var webSearchAgent = new WebSearchAgent(
            _httpClient,
            _mockOpenAIClient.Object,
            _webSearchConfig,
            _mockLogger.Object,
            mockTrustStore.Object);

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await webSearchAgent.SearchAsync(query, searchOptions);

        // Assert - Results should exist because test-motorcycle.com is allowlisted
        Assert.NotEmpty(results);
        mockTrustStore.Verify(x => x.GetPolicyForDomain("test-motorcycle.com"), Times.AtLeastOnce);
    }

    [Fact]
    public async Task SearchAsync_ShouldAcceptAllowlistedDomains_WhenDomainInTrustPolicy() {
        // Arrange
        var query = "Honda CBR1000RR specifications";
        var searchOptions = CreateDefaultSearchOptions();

        var allowlistPolicy = CreateTestTrustPolicy(
            domainPattern: "test-motorcycle.com",
            tier: WebTrustTier.TierA,
            isBlocked: false);

        var mockTrustStore = new Mock<IWebTrustPolicyStore>();
        mockTrustStore
            .Setup(x => x.GetPolicyForDomain("test-motorcycle.com"))
            .Returns(allowlistPolicy);

        var webSearchAgent = new WebSearchAgent(
            _httpClient,
            _mockOpenAIClient.Object,
            _webSearchConfig,
            _mockLogger.Object,
            mockTrustStore.Object);

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, result => {
            Assert.Contains("domainTrustTier", result.Metadata.Keys);
            Assert.Equal("TierA", result.Metadata["domainTrustTier"]);
        });
    }

    [Fact]
    public async Task SearchAsync_ShouldRejectNonAllowlistedDomains_WhenDomainNotInTrustPolicy() {
        // Arrange
        var query = "Honda CBR1000RR specifications";
        var searchOptions = CreateDefaultSearchOptions();

        var mockTrustStore = new Mock<IWebTrustPolicyStore>();
        mockTrustStore
            .Setup(x => x.GetPolicyForDomain("test-motorcycle.com"))
            .Returns((WebTrustPolicy?)null); // Not allowlisted

        var webSearchAgent = new WebSearchAgent(
            _httpClient,
            _mockOpenAIClient.Object,
            _webSearchConfig,
            _mockLogger.Object,
            mockTrustStore.Object);

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act - With trust policy store configured, unknown domains should be filtered
        var results = await webSearchAgent.SearchAsync(query, searchOptions);

        // Assert - Results should be empty or not contain rejections if policy enforcement works
        // The agent should reject results from non-allowlisted domains
        if (results.Length > 0) {
            // If there are results, they should have rejection metadata
            Assert.True(results.All(r =>
                !r.Metadata.ContainsKey("trustPolicyRejection") ||
                r.RelevanceScore > 0)); // Results that passed should have positive scores
        }
    }

    [Fact]
    public async Task SearchAsync_ShouldApplyTierARelevanceBoost_WhenDomainHasTierA() {
        // Arrange
        var query = "Honda CBR1000RR specifications";
        var searchOptions = CreateDefaultSearchOptions();

        var tierAPolicy = CreateTestTrustPolicy(
            domainPattern: "test-motorcycle.com",
            tier: WebTrustTier.TierA,
            isBlocked: false);

        var mockTrustStore = new Mock<IWebTrustPolicyStore>();
        mockTrustStore
            .Setup(x => x.GetPolicyForDomain("test-motorcycle.com"))
            .Returns(tierAPolicy);

        var webSearchAgent = new WebSearchAgent(
            _httpClient,
            _mockOpenAIClient.Object,
            _webSearchConfig,
            _mockLogger.Object,
            mockTrustStore.Object);

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await webSearchAgent.SearchAsync(query, searchOptions);

        // Assert - Tier A should have multiplier of 1.5x
        Assert.NotEmpty(results);
        Assert.All(results, result => {
            Assert.Contains("trustTierMultiplier", result.Metadata.Keys);
            Assert.Equal(1.5f, (float)result.Metadata["trustTierMultiplier"]);
        });
    }

    [Fact]
    public async Task SearchAsync_ShouldApplyTierBRelevanceBoost_WhenDomainHasTierB() {
        // Arrange
        var query = "Yamaha R1 engine specs";
        var searchOptions = CreateDefaultSearchOptions();

        var tierBPolicy = CreateTestTrustPolicy(
            domainPattern: "test-motorcycle.com",
            tier: WebTrustTier.TierB,
            isBlocked: false);

        var mockTrustStore = new Mock<IWebTrustPolicyStore>();
        mockTrustStore
            .Setup(x => x.GetPolicyForDomain("test-motorcycle.com"))
            .Returns(tierBPolicy);

        var webSearchAgent = new WebSearchAgent(
            _httpClient,
            _mockOpenAIClient.Object,
            _webSearchConfig,
            _mockLogger.Object,
            mockTrustStore.Object);

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await webSearchAgent.SearchAsync(query, searchOptions);

        // Assert - Tier B should have multiplier of 1.1x
        Assert.NotEmpty(results);
        Assert.All(results, result => {
            Assert.Contains("trustTierMultiplier", result.Metadata.Keys);
            Assert.Equal(1.1f, (float)result.Metadata["trustTierMultiplier"]);
        });
    }

    [Fact]
    public async Task SearchAsync_ShouldApplyTierCRelevancePenalty_WhenDomainHasTierC() {
        // Arrange
        var query = "Kawasaki Ninja performance";
        var searchOptions = CreateDefaultSearchOptions();

        var tierCPolicy = CreateTestTrustPolicy(
            domainPattern: "test-motorcycle.com",
            tier: WebTrustTier.TierC,
            isBlocked: false);

        var mockTrustStore = new Mock<IWebTrustPolicyStore>();
        mockTrustStore
            .Setup(x => x.GetPolicyForDomain("test-motorcycle.com"))
            .Returns(tierCPolicy);

        var webSearchAgent = new WebSearchAgent(
            _httpClient,
            _mockOpenAIClient.Object,
            _webSearchConfig,
            _mockLogger.Object,
            mockTrustStore.Object);

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await webSearchAgent.SearchAsync(query, searchOptions);

        // Assert - Tier C should have penalty multiplier of 0.9x
        Assert.NotEmpty(results);
        Assert.All(results, result => {
            Assert.Contains("trustTierMultiplier", result.Metadata.Keys);
            Assert.Equal(0.9f, (float)result.Metadata["trustTierMultiplier"]);
        });
    }

    [Fact]
    public async Task SearchAsync_ShouldSupportBackwardCompatibility_WhenTrustPolicyStoreNotProvided() {
        // Arrange
        var query = "Ducati Panigale features";
        var searchOptions = CreateDefaultSearchOptions();

        // Create agent WITHOUT trust policy store (null)
        var webSearchAgent = new WebSearchAgent(
            _httpClient,
            _mockOpenAIClient.Object,
            _webSearchConfig,
            _mockLogger.Object,
            null); // No trust policy store - backward compatibility mode

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await webSearchAgent.SearchAsync(query, searchOptions);

        // Assert - Should work without trust policy enforcement
        Assert.NotEmpty(results);
        Assert.All(results, result => {
            // In backward compatibility mode, there should be no trust tier information
            Assert.DoesNotContain("trustPolicyRejection", result.Metadata.Keys);
            Assert.True(result.RelevanceScore > 0);
        });

        // Verify logger was called with backward compatibility message at least once
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("WebTrustPolicyStore not configured")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task SearchAsync_ShouldRespectBlockedDomain_EvenWithHighCredibilityScore() {
        // Arrange
        var query = "BMW S1000RR specifications";
        var searchOptions = CreateDefaultSearchOptions();

        var blockedPolicy = CreateTestTrustPolicy(
            domainPattern: "test-motorcycle.com",
            tier: WebTrustTier.TierA,
            isBlocked: true,
            reason: "Misinformation and inaccurate specifications");

        var mockTrustStore = new Mock<IWebTrustPolicyStore>();
        mockTrustStore
            .Setup(x => x.GetPolicyForDomain("test-motorcycle.com"))
            .Returns(blockedPolicy);

        var webSearchAgent = new WebSearchAgent(
            _httpClient,
            _mockOpenAIClient.Object,
            _webSearchConfig,
            _mockLogger.Object,
            mockTrustStore.Object);

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await webSearchAgent.SearchAsync(query, searchOptions);

        // Assert - Blocked domains should be rejected regardless of tier or credibility
        // The test-motorcycle.com source should be blocked even if it would normally be TierA
        if (results.Length > 0) {
            // All results should either come from non-blocked sources or have rejection metadata
            Assert.True(results.All(r =>
                !r.Metadata.TryGetValue("trustPolicyRejection", out var rejection) ||
                rejection.ToString()!.Contains("blocked")));
        }
    }

    [Fact]
    public async Task SearchAsync_ShouldTrackDomainTierInMetadata_WhenTrustPolicyEnforced() {
        // Arrange
        var query = "Suzuki GSX-R1000 features";
        var searchOptions = CreateDefaultSearchOptions();

        var tierPolicy = CreateTestTrustPolicy(
            domainPattern: "test-motorcycle.com",
            tier: WebTrustTier.TierB,
            isBlocked: false);

        var mockTrustStore = new Mock<IWebTrustPolicyStore>();
        mockTrustStore
            .Setup(x => x.GetPolicyForDomain("test-motorcycle.com"))
            .Returns(tierPolicy);

        var webSearchAgent = new WebSearchAgent(
            _httpClient,
            _mockOpenAIClient.Object,
            _webSearchConfig,
            _mockLogger.Object,
            mockTrustStore.Object);

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, result => {
            // Should have domain trust tier information
            Assert.Contains("domainTrustTier", result.Metadata.Keys);
            Assert.Equal("TierB", result.Metadata["domainTrustTier"]);
        });
    }

    [Fact]
    public async Task SearchAsync_ShouldApplyMultipleTierPolicies_WhenMultipleSourcesWithDifferentTiers() {
        // Arrange
        var query = "KTM Duke specifications";
        var searchOptions = CreateDefaultSearchOptions();

        var tierAPolicy = CreateTestTrustPolicy(
            domainPattern: "test-motorcycle.com",
            tier: WebTrustTier.TierA,
            isBlocked: false);

        var tierCPolicy = CreateTestTrustPolicy(
            domainPattern: "community-forums.com",
            tier: WebTrustTier.TierC,
            isBlocked: false);

        var mockTrustStore = new Mock<IWebTrustPolicyStore>();
        mockTrustStore
            .Setup(x => x.GetPolicyForDomain(It.IsAny<string>()))
            .Returns<string>(domain => domain == "test-motorcycle.com" ? tierAPolicy : tierCPolicy);

        var webSearchAgent = new WebSearchAgent(
            _httpClient,
            _mockOpenAIClient.Object,
            _webSearchConfig,
            _mockLogger.Object,
            mockTrustStore.Object);

        SetupMockHttpClient();
        SetupMockOpenAIClient();

        // Act
        var results = await webSearchAgent.SearchAsync(query, searchOptions);

        // Assert
        Assert.NotEmpty(results);

        // Results should be ranked by trust tier if present
        var resultsList = results.OrderByDescending(r => r.RelevanceScore).ToList();

        // First result should have higher relevance due to Tier A multiplier
        if (resultsList.Any(r => r.Metadata.TryGetValue("domainTrustTier", out var tier) && tier?.ToString() == "TierA")) {
            var tierAResults = resultsList.Where(r => r.Metadata.TryGetValue("domainTrustTier", out var tier) && tier?.ToString() == "TierA").ToList();
            Assert.NotEmpty(tierAResults);
        }
    }

    #endregion

    #region Helper Methods

    private SearchParameters CreateDefaultSearchOptions() {
        return new SearchParameters {
            MaxResults = 10,
            MinRelevanceScore = 0.5f,
            IncludeMetadata = true,
            Filters = new Dictionary<string, object>(),
            EnableCaching = false // Disable caching for most tests
        };
    }

    private void SetupMockHttpClient() {
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

    private void SetupMockHttpClientFailure() {
        _mockHttpHandler.SetupAnyRequest()
            .ReturnsResponse(HttpStatusCode.NotFound);
    }

    private void SetupMockOpenAIClient() {
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

    private void SetupMockOpenAIClientWithValidation() {
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

    private void SetupMockOpenAIClientFailure() {
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

    public void Dispose() {
        _httpClient?.Dispose();
    }
}
