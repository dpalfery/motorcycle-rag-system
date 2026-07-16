using Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Persistence.Azure;
using MotorcycleRAG.Persistence.Azure.Search;
using MotorcycleRAG.Core.Options;

using MotorcycleRAG.Contracts.Models.DTOs.Search;
namespace MotorcycleRAG.UnitTests.Azure;

public class AzureSearchClientWrapperTests : IDisposable {
    private readonly Mock<ILogger<AzureSearchClientWrapper>> _mockLogger;
    private readonly Mock<IAzureSearchQueryService> _mockQueryService;
    private readonly Mock<IAzureSearchDocumentService> _mockDocumentService;
    private readonly Mock<IAzureSearchHealthService> _mockHealthService;
    private readonly AzureFoundryOptions _azureConfig;
    private readonly SearchOptions _searchConfig;
    private readonly IOptions<AzureFoundryOptions> _azureOptions;
    private readonly IOptions<SearchOptions> _searchOptions;

    public AzureSearchClientWrapperTests() {
        _mockLogger = new Mock<ILogger<AzureSearchClientWrapper>>();
        _mockQueryService = new Mock<IAzureSearchQueryService>();
        _mockDocumentService = new Mock<IAzureSearchDocumentService>();
        _mockHealthService = new Mock<IAzureSearchHealthService>();

        // Setup health service to return successful search results
        _mockHealthService
            .Setup(x => x.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] {
                new SearchResult {
                    Id = "test-1",
                    Content = "test content",
                    RelevanceScore = 0.95f,
                    Source = new SearchSource {
                        DocumentId = "chunk-1",
                        SourceName = "test source",
                        AgentType = SearchAgentType.VectorSearch
                    }
                }
            });

        _mockHealthService
            .Setup(x => x.IsHealthyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Setup document service
        _mockDocumentService
            .Setup(x => x.IndexDocumentsAsync(It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockDocumentService
            .Setup(x => x.CreateOrUpdateIndexAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _azureConfig = new AzureFoundryOptions {
            SearchServiceEndpoint = "https://test-search.search.windows.net/",
            Retry = new RetryOptions {
                MaxRetries = 3,
                BaseDelaySeconds = 2,
                MaxDelaySeconds = 60,
                UseExponentialBackoff = true
            }
        };

        _searchConfig = new SearchOptions {
            IndexName = "test-index",
            BatchSize = 100,
            MaxSearchResults = 50,
            EnableHybridSearch = true,
            EnableSemanticRanking = true
        };

        _azureOptions = Options.Create(_azureConfig);
        _searchOptions = Options.Create(_searchConfig);
    }

    // ---- Constructor ----

    [Fact]
    public void Constructor_WithValidConfiguration_ShouldInitializeSuccessfully() {
        // Act & Assert
        var exception = Record.Exception(() => new AzureSearchClientWrapper(
            _azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object));
        exception.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithNullAzureConfiguration_ShouldThrowArgumentNullException() {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AzureSearchClientWrapper(null!, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object));
        exception.ParamName.Should().Be("azureConfig");
    }

    [Fact]
    public void Constructor_WithNullSearchConfiguration_ShouldThrowArgumentNullException() {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AzureSearchClientWrapper(_azureOptions, null!, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object));
        exception.ParamName.Should().Be("searchConfig");
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException() {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AzureSearchClientWrapper(_azureOptions, _searchOptions, null!, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object));
        exception.ParamName.Should().Be("logger");
    }

    [Fact]
    public void Constructor_WithNullQueryService_ShouldThrowArgumentNullException() {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, null!, _mockDocumentService.Object, _mockHealthService.Object));
        exception.ParamName.Should().Be("queryService");
    }

    [Fact]
    public void Constructor_WithNullDocumentService_ShouldThrowArgumentNullException() {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, null!, _mockHealthService.Object));
        exception.ParamName.Should().Be("documentService");
    }

    [Fact]
    public void Constructor_WithNullHealthService_ShouldThrowArgumentNullException() {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, null!));
        exception.ParamName.Should().Be("healthService");
    }

    [Fact]
    public void Constructor_WithNullAzureConfigValue_ShouldThrowArgumentNullException() {
        var emptyOptions = Options.Create<AzureFoundryOptions>(null!);
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AzureSearchClientWrapper(emptyOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object));
        exception.ParamName.Should().Be("azureConfig");
    }

    [Fact]
    public void Constructor_WithNullSearchConfigValue_ShouldThrowArgumentNullException() {
        var emptyOptions = Options.Create<SearchOptions>(null!);
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new AzureSearchClientWrapper(_azureOptions, emptyOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object));
        exception.ParamName.Should().Be("searchConfig");
    }

    [Fact]
    public void Constructor_ShouldLogInitializationMessage() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);

        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Azure Search client initialized")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ---- SearchAsync (simple) ----

    [Fact]
    public async Task SearchAsync_SimpleOverload_ShouldPassDefaultMaxResults() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);

        await client.SearchAsync("test query");

        _mockHealthService.Verify(
            x => x.SearchAsync("test query", 50, CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithMaxResults_ShouldDelegateToHealthService() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);

        await client.SearchAsync("test query", 20);

        _mockHealthService.Verify(
            x => x.SearchAsync("test query", 20, CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithValidQuery_ShouldReturnResults() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);

        var results = await client.SearchAsync("test query", 10);

        results.Should().NotBeNull();
        results.Should().NotBeEmpty();
        results.Length.Should().BeLessThanOrEqualTo(10);
    }

    [Fact]
    public async Task SearchAsync_WithCancellationToken_ShouldPassToken() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);
        var cts = new CancellationTokenSource();

        await client.SearchAsync("test query", 10, cts.Token);

        _mockHealthService.Verify(
            x => x.SearchAsync("test query", 10, cts.Token),
            Times.Once);
    }

    // ---- IndexDocumentsAsync (generic) ----

    [Fact]
    public async Task IndexDocumentsAsync_ConvenienceOverload_ShouldCallWithDefaultCancellationToken() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);
        var documents = new[] { new { id = "1" } };

        await client.IndexDocumentsAsync(documents);

        _mockDocumentService.Verify(
            x => x.IndexDocumentsAsync(documents, CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task IndexDocumentsAsync_WithCancellationToken_ShouldPassToken() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);
        var cts = new CancellationTokenSource();
        var documents = new[] { new { id = "1" } };

        await client.IndexDocumentsAsync(documents, cts.Token);

        _mockDocumentService.Verify(
            x => x.IndexDocumentsAsync(documents, cts.Token),
            Times.Once);
    }

    // ---- IndexDocumentsAsync (MotorcycleDocumentDto enumerable) ----

    [Fact]
    public async Task IndexDocumentsAsync_MotorcycleDocument_ShouldDelegateToDocumentService() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);
        var documents = new List<MotorcycleDocumentDto> { new() { Id = "doc1" } };

        await client.IndexDocumentsAsync(documents);

        _mockDocumentService.Verify(
            x => x.IndexDocumentsAsync(documents),
            Times.Once);
    }

    // ---- DeleteDocumentsAsync ----

    [Fact]
    public async Task DeleteDocumentsAsync_ShouldDelegateToDocumentService() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);
        var documentIds = new[] { "id1", "id2" };

        await client.DeleteDocumentsAsync(documentIds);

        _mockDocumentService.Verify(
            x => x.DeleteDocumentsAsync(documentIds),
            Times.Once);
    }

    // ---- CreateOrUpdateIndexAsync ----

    [Fact]
    public async Task CreateOrUpdateIndexAsync_ConvenienceOverload_ShouldCallWithDefaultCancellationToken() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);

        await client.CreateOrUpdateIndexAsync("test-index");

        _mockDocumentService.Verify(
            x => x.CreateOrUpdateIndexAsync("test-index", CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task CreateOrUpdateIndexAsync_WithCancellationToken_ShouldPassToken() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);
        var cts = new CancellationTokenSource();

        await client.CreateOrUpdateIndexAsync("test-index", cts.Token);

        _mockDocumentService.Verify(
            x => x.CreateOrUpdateIndexAsync("test-index", cts.Token),
            Times.Once);
    }

    // ---- IsHealthyAsync ----

    [Fact]
    public async Task IsHealthyAsync_ConvenienceOverload_ShouldCallWithDefaultCancellationToken() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);

        await client.IsHealthyAsync();

        _mockHealthService.Verify(
            x => x.IsHealthyAsync(CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task IsHealthyAsync_WithCancellationToken_ShouldPassToken() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);
        var cts = new CancellationTokenSource();

        await client.IsHealthyAsync(cts.Token);

        _mockHealthService.Verify(
            x => x.IsHealthyAsync(cts.Token),
            Times.Once);
    }

    // ---- VectorSearchAsync ----

    [Fact]
    public async Task VectorSearchAsync_ShouldDelegateToQueryService() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);
        var options = new SearchOptions { MaxSearchResults = 10 };
        var expectedResults = new SearchResult[] { new() { Id = "v1" } };
        _mockQueryService.Setup(x => x.VectorSearchAsync("query", options))
            .ReturnsAsync(expectedResults);

        var results = await client.VectorSearchAsync("query", options);

        results.Should().BeSameAs(expectedResults);
        _mockQueryService.Verify(x => x.VectorSearchAsync("query", options), Times.Once);
    }

    // ---- HybridSearchAsync ----

    [Fact]
    public async Task HybridSearchAsync_ShouldDelegateToQueryService() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);
        var options = new SearchOptions { MaxSearchResults = 10 };
        var expectedResults = new SearchResult[] { new() { Id = "h1" } };
        _mockQueryService.Setup(x => x.HybridSearchAsync("query", options))
            .ReturnsAsync(expectedResults);

        var results = await client.HybridSearchAsync("query", options);

        results.Should().BeSameAs(expectedResults);
        _mockQueryService.Verify(x => x.HybridSearchAsync("query", options), Times.Once);
    }

    // ---- SearchAsync (Core.SearchOptions) ----

    [Fact]
    public async Task SearchAsync_WithCoreSearchOptions_ShouldDelegateToQueryService() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);
        var options = new SearchOptions { MaxSearchResults = 10 };
        var expectedResults = new SearchResult[] { new() { Id = "s1" } };
        _mockQueryService.Setup(x => x.SearchAsync("query", options))
            .ReturnsAsync(expectedResults);

        var results = await client.SearchAsync("query", options);

        results.Should().BeSameAs(expectedResults);
        _mockQueryService.Verify(x => x.SearchAsync("query", options), Times.Once);
    }

    // ---- Dispose ----

    [Fact]
    public void Dispose_ShouldDisposeResourcesGracefully() {
        using var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);

        var exception = Record.Exception(() => client.Dispose());
        exception.Should().BeNull();

        // Calling dispose again should not throw
        exception = Record.Exception(() => client.Dispose());
        exception.Should().BeNull();
    }

    [Fact]
    public void Dispose_CanBeDisposedMultipleTimes() {
        var client = new AzureSearchClientWrapper(_azureOptions, _searchOptions, _mockLogger.Object, _mockQueryService.Object, _mockDocumentService.Object, _mockHealthService.Object);

        client.Dispose();
        client.Dispose();
        client.Dispose();

        // No exception means success
    }

    // ---- Deprecated/legacy tests kept for historical reference ----

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void IsRetryableError_WithRetryableStatusCodes_ShouldReturnTrue(int statusCode) {
        var exception = new RequestFailedException(statusCode, "Test error");
        var result = IsRetryableErrorAccessor(exception);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public void IsRetryableError_WithNonRetryableStatusCodes_ShouldReturnFalse(int statusCode) {
        var exception = new RequestFailedException(statusCode, "Test error");
        var result = IsRetryableErrorAccessor(exception);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void IsCircuitBreakerError_WithServerErrors_ShouldReturnTrue(int statusCode) {
        var exception = new RequestFailedException(statusCode, "Test error");
        var result = IsCircuitBreakerErrorAccessor(exception);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    public void IsCircuitBreakerError_WithClientErrors_ShouldReturnFalse(int statusCode) {
        var exception = new RequestFailedException(statusCode, "Test error");
        var result = IsCircuitBreakerErrorAccessor(exception);
        result.Should().BeFalse();
    }

    // Helper methods
    private static bool IsRetryableErrorAccessor(RequestFailedException ex) {
        return ex.Status == 429 || ex.Status == 500 || ex.Status == 502 || ex.Status == 503 || ex.Status == 504;
    }

    private static bool IsCircuitBreakerErrorAccessor(RequestFailedException ex) {
        return ex.Status >= 500;
    }

    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) {
        if (disposing) { }
    }
}
