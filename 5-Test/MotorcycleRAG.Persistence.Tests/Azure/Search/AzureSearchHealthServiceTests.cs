using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.ValueObjects;
using MotorcycleRAG.Persistence.Azure.Search;

namespace MotorcycleRAG.Persistence.Tests.Azure.Search;

public sealed class AzureSearchHealthServiceTests
{
    private readonly Mock<ISearchClientFactory> _clientFactoryMock = new();
    private readonly Mock<IResilienceService> _resilienceServiceMock = new();
    private readonly Mock<ICorrelationService> _correlationServiceMock = new();

    private AzureSearchHealthService CreateSut() =>
        new(
            _clientFactoryMock.Object,
            TestHelpers.CreateNullLogger<AzureSearchHealthService>(),
            _resilienceServiceMock.Object,
            _correlationServiceMock.Object);

    // ---- Constructor ----

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenClientFactoryIsNull()
    {
        var act = () => new AzureSearchHealthService(
            null!,
            TestHelpers.CreateNullLogger<AzureSearchHealthService>(),
            _resilienceServiceMock.Object,
            _correlationServiceMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var act = () => new AzureSearchHealthService(
            _clientFactoryMock.Object,
            null!,
            _resilienceServiceMock.Object,
            _correlationServiceMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenResilienceServiceIsNull()
    {
        var act = () => new AzureSearchHealthService(
            _clientFactoryMock.Object,
            TestHelpers.CreateNullLogger<AzureSearchHealthService>(),
            null!,
            _correlationServiceMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("resilienceService");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenCorrelationServiceIsNull()
    {
        var act = () => new AzureSearchHealthService(
            _clientFactoryMock.Object,
            TestHelpers.CreateNullLogger<AzureSearchHealthService>(),
            _resilienceServiceMock.Object,
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("correlationService");
    }

    // ---- IsHealthyAsync ----

    [Fact]
    public async Task IsHealthyAsync_ShouldDelegateToResilienceService()
    {
        var sut = CreateSut();
        var correlationId = "corr-health";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.HealthCheck",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");

        var result = await sut.IsHealthyAsync();

        result.Should().BeTrue();
        _resilienceServiceMock.Verify(
            x => x.ExecuteAsync<bool>(
                "AzureSearch.HealthCheck",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task IsHealthyAsync_WhenResilienceReturnsFalse_ShouldReturnFalse()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-false");
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.HealthCheck",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");

        var result = await sut.IsHealthyAsync();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsHealthyAsync_WithCancellationToken_ShouldPassTokenToResilience()
    {
        var sut = CreateSut();
        var correlationId = "corr-ct-health";
        var cts = new CancellationTokenSource();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.HealthCheck",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                cts.Token))
            .ReturnsAsync(true);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");

        var result = await sut.IsHealthyAsync(cts.Token);

        result.Should().BeTrue();
    }

    // ---- IsHealthyAsync: ExecuteHealthCheckAsync (operation delegate) ----

    [Fact]
    public async Task IsHealthyAsync_WhenOperationDelegateRuns_ShouldReturnTrue()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-op");
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");

        // Resilience invokes the operation delegate (which simulates health check)
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.HealthCheck",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, Func<Task<bool>> op, Func<Task<bool>> fb, string cid, CancellationToken ct) => op());

        var result = await sut.IsHealthyAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsHealthyAsync_WhenOperationThrows_ShouldReturnFalse()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-throw");
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        // Don't set up GetIndexName — the error happens before that

        // GetIndexName on default throws
        _clientFactoryMock
            .Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Throws<InvalidOperationException>();

        // Resilience invokes the operation, which throws, fallback returns false
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.HealthCheck",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string key, Func<Task<bool>> op, Func<Task<bool>> fb, string cid, CancellationToken ct) =>
                {
                    try { return await op(); }
                    catch { return await fb(); }
                });

        var result = await sut.IsHealthyAsync();

        result.Should().BeFalse();
    }

    // ---- SearchAsync ----

    [Fact]
    public async Task SearchAsync_ShouldDelegateToResilienceService()
    {
        var sut = CreateSut();
        var correlationId = "corr-search";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.BasicSearch",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new[]
            {
                new SearchResult
                {
                    Id = "doc_0",
                    Content = "Search result 0 for query: test",
                    RelevanceScore = 1.0f,
                    Source = new SearchSource
                    {
                        AgentType = SearchAgentType.VectorSearch,
                        SourceName = "Azure AI Search",
                        DocumentId = "doc_0"
                    }
                }
            });

        var results = await sut.SearchAsync("test", 5, CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].Id.Should().Be("doc_0");
        _resilienceServiceMock.Verify(
            x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.BasicSearch",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---- SearchAsync: ExecuteBasicSearchAsync (operation delegate) ----

    [Fact]
    public async Task SearchAsync_WithMaxResultsZero_ShouldReturnEmptyResults()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-zero");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);

        // Resilience invokes the operation delegate (which simulates basic search)
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.BasicSearch",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, Func<Task<SearchResult[]>> op, Func<Task<SearchResult[]>> fb, string cid, CancellationToken ct) => op());

        var results = await sut.SearchAsync("test", 0, CancellationToken.None);

        // Math.Min(0, 5) = 0, so no results generated
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithMaxResultsGreaterThanFive_ShouldReturnMaxFiveResults()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-many");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.BasicSearch",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, Func<Task<SearchResult[]>> op, Func<Task<SearchResult[]>> fb, string cid, CancellationToken ct) => op());

        var results = await sut.SearchAsync("test", 25, CancellationToken.None);

        // Math.Min(25, 5) = 5, so max 5 results
        results.Should().HaveCount(5);
        results[0].Id.Should().Be("doc_0");
        results[4].Id.Should().Be("doc_4");
    }

    [Fact]
    public async Task SearchAsync_WithMaxResultsOne_ShouldReturnSingleResult()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-one");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.BasicSearch",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, Func<Task<SearchResult[]>> op, Func<Task<SearchResult[]>> fb, string cid, CancellationToken ct) => op());

        var results = await sut.SearchAsync("test", 1, CancellationToken.None);

        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchAsync_SimulatedResultsHaveCorrectStructure()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-struct");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.BasicSearch",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, Func<Task<SearchResult[]>> op, Func<Task<SearchResult[]>> fb, string cid, CancellationToken ct) => op());

        var results = await sut.SearchAsync("my query", 5, CancellationToken.None);

        results.Should().HaveCount(5);
        results[0].Metadata["query"].Should().Be("my query");
        results[0].Metadata["index"].Should().Be(0);
        results[0].Source.AgentType.Should().Be(SearchAgentType.VectorSearch);
        results[0].Source.SourceName.Should().Be("Azure AI Search");
    }

    // ---- SearchAsync fallback ----

    [Fact]
    public async Task SearchAsync_ReturnsFallbackResult_WhenResilienceInvokesFallback()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-fallback");
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.BasicSearch",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, Func<Task<SearchResult[]>> op, Func<Task<SearchResult[]>> fb, string cid, CancellationToken ct) => fb());

        var results = await sut.SearchAsync("test query", 5, CancellationToken.None);

        results.Should().HaveCount(1);
        results[0].Id.Should().Be("fallback_result");
        results[0].Content.Should().Contain("Fallback");
        results[0].Metadata.Should().ContainKey("fallback");
        results[0].Metadata.Should().ContainKey("query");
        results[0].RelevanceScore.Should().Be(0.5f);
    }

    [Fact]
    public async Task SearchAsync_FallbackResultContainsOriginalQueryText()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-fb2");
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.BasicSearch",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, Func<Task<SearchResult[]>> op, Func<Task<SearchResult[]>> fb, string cid, CancellationToken ct) => fb());

        var results = await sut.SearchAsync("unique fallback query", 10, CancellationToken.None);

        results[0].Metadata["query"].Should().Be("unique fallback query");
    }

    // ---- IsHealthyAsync - already cancelled token ----

    [Fact]
    public async Task IsHealthyAsync_WhenTokenAlreadyCancelled_StillInvokesResilience()
    {
        var sut = CreateSut();
        var correlationId = "corr-pre-cancel";
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<bool>(
                "AzureSearch.HealthCheck",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                cts.Token))
            .ReturnsAsync(false);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");

        var result = await sut.IsHealthyAsync(cts.Token);

        result.Should().BeFalse();
        _resilienceServiceMock.Verify(
            x => x.ExecuteAsync<bool>(
                "AzureSearch.HealthCheck",
                It.IsAny<Func<Task<bool>>>(),
                It.IsAny<Func<Task<bool>>>(),
                correlationId,
                cts.Token),
            Times.Once,
            "the pre-cancelled token should be passed through to the resilience service");
    }

    // ---- SearchAsync - empty text ----

    [Fact]
    public async Task SearchAsync_WithEmptySearchText_ShouldReturnResultsWithEmptyQuery()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-empty-text");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);

        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.BasicSearch",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, Func<Task<SearchResult[]>> op, Func<Task<SearchResult[]>> fb, string cid, CancellationToken ct) => op());

        var results = await sut.SearchAsync("", 5, CancellationToken.None);

        results.Should().HaveCount(5);
        results[0].Metadata["query"].Should().Be("");
    }
}
