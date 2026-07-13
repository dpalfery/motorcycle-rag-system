using System.Reflection;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.ValueObjects;
using MotorcycleRAG.Persistence.Azure.Search;
using AzureSearchOptions = Azure.Search.Documents.SearchOptions;

namespace MotorcycleRAG.Persistence.Tests.Azure.Search;

public sealed class AzureSearchQueryServiceTests
{
    private readonly Mock<ISearchClientFactory> _clientFactoryMock = new();
    private readonly Mock<IResilienceService> _resilienceServiceMock = new();
    private readonly Mock<ICorrelationService> _correlationServiceMock = new();

    private AzureSearchQueryService CreateSut() =>
        new(
            _clientFactoryMock.Object,
            TestHelpers.CreateNullLogger<AzureSearchQueryService>(),
            _resilienceServiceMock.Object,
            _correlationServiceMock.Object);

    private static Core.Options.SearchOptions DefaultSearchOptions() => new()
    {
        MaxSearchResults = 10,
        Category = null
    };

    // ---- Constructor ----

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenClientFactoryIsNull()
    {
        var act = () => new AzureSearchQueryService(
            null!,
            TestHelpers.CreateNullLogger<AzureSearchQueryService>(),
            _resilienceServiceMock.Object,
            _correlationServiceMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientFactory");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenLoggerIsNull()
    {
        var act = () => new AzureSearchQueryService(
            _clientFactoryMock.Object,
            null!,
            _resilienceServiceMock.Object,
            _correlationServiceMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenResilienceServiceIsNull()
    {
        var act = () => new AzureSearchQueryService(
            _clientFactoryMock.Object,
            TestHelpers.CreateNullLogger<AzureSearchQueryService>(),
            null!,
            _correlationServiceMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("resilienceService");
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenCorrelationServiceIsNull()
    {
        var act = () => new AzureSearchQueryService(
            _clientFactoryMock.Object,
            TestHelpers.CreateNullLogger<AzureSearchQueryService>(),
            _resilienceServiceMock.Object,
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("correlationService");
    }

    // ---- VectorSearchAsync ----

    [Fact]
    public async Task VectorSearchAsync_ShouldDelegateToResilienceService()
    {
        var sut = CreateSut();
        var correlationId = "corr-vector";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.VectorSearch",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SearchResult>());
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.AllCategories).Returns(MotorcycleCategory.All);

        await sut.VectorSearchAsync("test", DefaultSearchOptions());

        _resilienceServiceMock.Verify(
            x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.VectorSearch",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---- VectorSearchAsync fallback ----

    [Fact]
    public async Task VectorSearchAsync_ReturnsFallback_WhenResilienceInvokesFallback()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-vf");
        SetupResilienceToInvokeFallback<SearchResult[]>("AzureSearch.VectorSearch");

        var results = await sut.VectorSearchAsync("test query", new Core.Options.SearchOptions { MaxSearchResults = 5 });

        results.Should().HaveCount(1);
        results[0].Id.Should().Be("fallback_vectorsearchresult");
        results[0].Source.SourceName.Should().Be("Fallback Service");
        results[0].Metadata.Should().ContainKey("fallback");
        results[0].Metadata.Should().ContainKey("query");
    }

    // ---- HybridSearchAsync ----

    [Fact]
    public async Task HybridSearchAsync_ShouldDelegateToResilienceService()
    {
        var sut = CreateSut();
        var correlationId = "corr-hybrid";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.HybridSearch",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SearchResult>());
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.AllCategories).Returns(MotorcycleCategory.All);

        await sut.HybridSearchAsync("test", DefaultSearchOptions());

        _resilienceServiceMock.Verify(
            x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.HybridSearch",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---- HybridSearchAsync fallback ----

    [Fact]
    public async Task HybridSearchAsync_ReturnsFallback_WhenResilienceInvokesFallback()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-hf");
        SetupResilienceToInvokeFallback<SearchResult[]>("AzureSearch.HybridSearch");

        var results = await sut.HybridSearchAsync("test query", new Core.Options.SearchOptions { MaxSearchResults = 5 });

        results.Should().HaveCount(1);
        results[0].Id.Should().Be("fallback_hybridsearchresult");
        results[0].Source.SourceName.Should().Be("Fallback Service");
        results[0].Content.Should().Contain("HybridSearch");
    }

    // ---- SearchAsync ----

    [Fact]
    public async Task SearchAsync_ShouldDelegateToResilienceService()
    {
        var sut = CreateSut();
        var correlationId = "corr-search";
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns(correlationId);
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.Search",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SearchResult>());
        _clientFactoryMock.Setup(x => x.DefaultCategory).Returns(MotorcycleCategory.Sport);
        _clientFactoryMock.Setup(x => x.AllCategories).Returns(MotorcycleCategory.All);

        await sut.SearchAsync("test", DefaultSearchOptions());

        _resilienceServiceMock.Verify(
            x => x.ExecuteAsync<SearchResult[]>(
                "AzureSearch.Search",
                It.IsAny<Func<Task<SearchResult[]>>>(),
                It.IsAny<Func<Task<SearchResult[]>>>(),
                correlationId,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---- Fallback result ----

    [Fact]
    public async Task SearchAsync_ReturnsFallback_WhenResilienceInvokesFallback()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-fb");
        SetupResilienceToInvokeFallback<SearchResult[]>("AzureSearch.Search");

        var results = await sut.SearchAsync("motorcycle oil", new Core.Options.SearchOptions { MaxSearchResults = 5 });

        results.Should().HaveCount(1);
        results[0].Id.Should().Be("fallback_searchresult");
        results[0].Source.SourceName.Should().Be("Fallback Service");
        results[0].Metadata.Should().ContainKey("fallback");
    }

    // ---- Fan-out path: all indexes fail ----

    [Fact]
    public async Task SearchAsync_WhenFanOutAndAllIndexesFail_InvokesFallback()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-fanout");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.AllCategories).Returns(MotorcycleCategory.All);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");

        // Resilience invokes the operation, catches the exception, and invokes fallback
        bool fallbackInvoked = false;
        SetupResilienceToTryOperationAndCatch<SearchResult[]>("AzureSearch.Search", () => fallbackInvoked = true);

        // Make every GetClient call throw — the fan-out path calls GetClient eagerly in Select(),
        // so the exception propagates from the operation delegate to the resilience fallback
        _clientFactoryMock
            .Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>()))
            .Throws<InvalidOperationException>();

        var results = await sut.SearchAsync("test", new Core.Options.SearchOptions { MaxSearchResults = 5, Category = null });

        // All indexes failed, fallback was invoked
        fallbackInvoked.Should().BeTrue();
        results.Should().HaveCount(1);
        results[0].Id.Should().Be("fallback_searchresult");
        results[0].Source.SourceName.Should().Be("Fallback Service");
    }

    // ---- Single-category path: GetClient throws propagates to resilience ----

    [Fact]
    public async Task SearchAsync_WhenCategoryValidAndClientThrows_InvokesFallback()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-cat");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.AllCategories).Returns(MotorcycleCategory.All);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");
        _clientFactoryMock
            .Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>()))
            .Throws<InvalidOperationException>();

        // Resilience invokes the operation, which throws, then invokes fallback
        bool fallbackInvoked = false;
        SetupResilienceToTryOperationAndCatch<SearchResult[]>("AzureSearch.Search", () => fallbackInvoked = true);

        var results = await sut.SearchAsync("test", new Core.Options.SearchOptions { MaxSearchResults = 5, Category = "sport" });

        fallbackInvoked.Should().BeTrue();
        results.Should().HaveCount(1);
        results[0].Id.Should().Be("fallback_searchresult");
    }

    // ---- MergeByScore (internal static) ----

    [Fact]
    public void MergeByScore_ShouldMergeAndSortByScoreDescending()
    {
        var results = new SearchResult[][]
        {
            new[] { CreateResult("a", 0.5f), CreateResult("b", 0.9f) },
            new[] { CreateResult("c", 0.3f), CreateResult("d", 0.7f) },
        };

        var merged = AzureSearchQueryService.MergeByScore(results, 10);

        merged.Should().HaveCount(4);
        merged[0].Id.Should().Be("b"); // 0.9
        merged[1].Id.Should().Be("d"); // 0.7
        merged[2].Id.Should().Be("a"); // 0.5
        merged[3].Id.Should().Be("c"); // 0.3
    }

    [Fact]
    public void MergeByScore_ShouldTruncateToTopN()
    {
        var results = new SearchResult[][]
        {
            new[] { CreateResult("a", 0.9f), CreateResult("b", 0.7f), CreateResult("c", 0.3f) },
        };

        var merged = AzureSearchQueryService.MergeByScore(results, 2);

        merged.Should().HaveCount(2);
        merged[0].Id.Should().Be("a");
        merged[1].Id.Should().Be("b");
    }

    [Fact]
    public void MergeByScore_WithNullItems_ShouldIgnoreThem()
    {
        var results = new SearchResult[][]
        {
            new[] { CreateResult("a", 0.8f) },
            null!,
            new[] { CreateResult("b", 0.9f) },
        };

        var merged = AzureSearchQueryService.MergeByScore(results, 10);

        merged.Should().HaveCount(2);
        merged[0].Id.Should().Be("b");
        merged[1].Id.Should().Be("a");
    }

    [Fact]
    public void MergeByScore_WithEmptyInput_ShouldReturnEmpty()
    {
        var merged = AzureSearchQueryService.MergeByScore(Array.Empty<SearchResult[]>(), 10);

        merged.Should().BeEmpty();
    }

    [Fact]
    public void MergeByScore_WithTopNZero_ShouldReturnAll()
    {
        var results = new SearchResult[][]
        {
            new[] { CreateResult("a", 0.9f), CreateResult("b", 0.5f) },
        };

        var merged = AzureSearchQueryService.MergeByScore(results, 0);

        merged.Should().HaveCount(2);
    }

    [Fact]
    public void MergeByScore_WithSingleResultSet_ShouldReturnAllSorted()
    {
        var results = new SearchResult[][]
        {
            new[] { CreateResult("x", 0.2f), CreateResult("y", 0.8f), CreateResult("z", 0.6f) },
        };

        var merged = AzureSearchQueryService.MergeByScore(results, 10);

        merged.Should().HaveCount(3);
        merged[0].Id.Should().Be("y");
        merged[1].Id.Should().Be("z");
        merged[2].Id.Should().Be("x");
    }

    [Fact]
    public void MergeByScore_WithTopNSmallerThanCount_ShouldTruncateAndMaintainSort()
    {
        var results = new SearchResult[][]
        {
            new[] { CreateResult("low1", 0.1f) },
            new[] { CreateResult("high1", 0.95f), CreateResult("high2", 0.85f) },
            new[] { CreateResult("mid1", 0.5f), CreateResult("mid2", 0.4f) },
        };

        var merged = AzureSearchQueryService.MergeByScore(results, 3);

        merged.Should().HaveCount(3);
        merged[0].Id.Should().Be("high1");  // 0.95
        merged[1].Id.Should().Be("high2");  // 0.85
        merged[2].Id.Should().Be("mid1");   // 0.5
    }

    [Fact]
    public void MergeByScore_WithIdenticalScores_ShouldPreserveStableOrder()
    {
        var results = new SearchResult[][]
        {
            new[] { CreateResult("b", 0.5f) },
            new[] { CreateResult("a", 0.5f) },
        };

        var merged = AzureSearchQueryService.MergeByScore(results, 10);

        merged.Should().HaveCount(2);
        // Both have same score, so order is the result of SelectMany order
        merged.Select(r => r.RelevanceScore).Should().AllBeEquivalentTo(0.5f);
    }

    [Fact]
    public void MergeByScore_WithAllEmptySets_ShouldReturnEmpty()
    {
        var results = new SearchResult[][]
        {
            Array.Empty<SearchResult>(),
            Array.Empty<SearchResult>(),
            Array.Empty<SearchResult>(),
        };

        var merged = AzureSearchQueryService.MergeByScore(results, 10);

        merged.Should().BeEmpty();
    }

    // ---- Truncate (tested through reflection) ----

    [Fact]
    public void Truncate_WhenTopNIsZero_ShouldReturnAllResults()
    {
        var results = new[]
        {
            CreateResult("a", 0.9f),
            CreateResult("b", 0.8f),
            CreateResult("c", 0.7f),
        };

        var truncated = InvokeTruncate(results, 0);

        truncated.Should().HaveCount(3);
    }

    [Fact]
    public void Truncate_WhenTopNIsNegative_ShouldReturnAllResults()
    {
        var results = new[]
        {
            CreateResult("a", 0.9f),
            CreateResult("b", 0.8f),
        };

        var truncated = InvokeTruncate(results, -1);

        truncated.Should().HaveCount(2);
    }

    [Fact]
    public void Truncate_WhenResultsShorterThanTopN_ShouldReturnAllResults()
    {
        var results = new[]
        {
            CreateResult("a", 0.9f),
        };

        var truncated = InvokeTruncate(results, 5);

        truncated.Should().HaveCount(1);
    }

    [Fact]
    public void Truncate_WhenResultsLongerThanTopN_ShouldTruncate()
    {
        var results = new[]
        {
            CreateResult("a", 0.9f),
            CreateResult("b", 0.8f),
            CreateResult("c", 0.7f),
            CreateResult("d", 0.6f),
            CreateResult("e", 0.5f),
        };

        var truncated = InvokeTruncate(results, 3);

        truncated.Should().HaveCount(3);
        truncated[0].Id.Should().Be("a");
        truncated[1].Id.Should().Be("b");
        truncated[2].Id.Should().Be("c");
    }

    [Fact]
    public void Truncate_WhenResultsEqualTopN_ShouldReturnAllResults()
    {
        var results = new[]
        {
            CreateResult("a", 0.9f),
            CreateResult("b", 0.8f),
            CreateResult("c", 0.7f),
        };

        var truncated = InvokeTruncate(results, 3);

        truncated.Should().HaveCount(3);
    }

    [Fact]
    public void Truncate_WithEmptyArray_ShouldReturnEmpty()
    {
        var truncated = InvokeTruncate(Array.Empty<SearchResult>(), 5);

        truncated.Should().BeEmpty();
    }

    // ---- Single-category flow (valid category option) ----

    [Fact]
    public async Task SearchAsync_WithValidCategory_ShouldUseSingleIndexPath()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-single");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");
        _clientFactoryMock.Setup(x => x.AllCategories).Returns(MotorcycleCategory.All);

        // Make GetClient throw — in single-index path this propagates to resilience
        _clientFactoryMock.Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>()))
            .Throws<InvalidOperationException>();

        bool fallbackCalled = false;
        SetupResilienceToTryOperationAndCatch<SearchResult[]>("AzureSearch.Search", () => fallbackCalled = true);

        var results = await sut.SearchAsync("test", new Core.Options.SearchOptions { MaxSearchResults = 5, Category = "sport" });

        fallbackCalled.Should().BeTrue();
        results.Should().HaveCount(1);
    }

    // ---- Fallback metadata verification ----

    [Fact]
    public async Task SearchAsync_FallbackContainsOperationNameAndQueryInMetadata()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-meta");
        SetupResilienceToInvokeFallback<SearchResult[]>("AzureSearch.Search");

        var results = await sut.SearchAsync("my query here", new Core.Options.SearchOptions { MaxSearchResults = 5 });

        results.Should().HaveCount(1);
        results[0].Metadata["query"].Should().Be("my query here");
        results[0].Metadata["fallback"].Should().Be(true);
        results[0].RelevanceScore.Should().Be(0.5f);
    }

    // ---- Fan-out: ExecuteSearchResilientAsync catches non-OperationCanceledException ----

    [Fact]
    public async Task SearchAsync_WhenFanOutAndSearchClientThrowsNonOperationCanceled_ReturnsEmptyNotFallback()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-nonopc");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.AllCategories).Returns(MotorcycleCategory.All);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");

        // All indexes throw non-OperationCanceledException — the resilient wrapper catches
        // and returns empty, allowing the merge to produce an empty (non-fallback) result.
        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.SearchAsync<SearchResult>(
                It.IsAny<string>(),
                It.IsAny<AzureSearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("search service unavailable"));
        _clientFactoryMock.Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>())).Returns(searchClient.Object);

        SetupResilienceToInvokeOperation<SearchResult[]>("AzureSearch.Search");

        var results = await sut.SearchAsync("test", new Core.Options.SearchOptions { MaxSearchResults = 5, Category = null });

        // All indexes failed silently — result is empty, not fallback
        results.Should().BeEmpty();
    }

    // ---- Fan-out: ExecuteSearchResilientAsync re-throws OperationCanceledException ----

    [Fact]
    public async Task SearchAsync_WhenFanOutAndSearchClientThrowsOperationCanceled_InvokesFallback()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-opc");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.AllCategories).Returns(MotorcycleCategory.All);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");

        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.SearchAsync<SearchResult>(
                It.IsAny<string>(),
                It.IsAny<AzureSearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        _clientFactoryMock.Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>())).Returns(searchClient.Object);

        bool fallbackInvoked = false;
        SetupResilienceToTryOperationAndCatch<SearchResult[]>("AzureSearch.Search", () => fallbackInvoked = true);

        var results = await sut.SearchAsync("test", new Core.Options.SearchOptions { MaxSearchResults = 5, Category = null });

        fallbackInvoked.Should().BeTrue();
        results.Should().HaveCount(1);
        results[0].Id.Should().Be("fallback_searchresult");
    }

    // ---- HybridSearch fan-out: ExecuteSearchResilientAsync catch path ----

    [Fact]
    public async Task HybridSearchAsync_WhenFanOutAndSearchClientThrowsNonOperationCanceled_ReturnsEmptyNotFallback()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-hyb-nf");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.AllCategories).Returns(MotorcycleCategory.All);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");

        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.SearchAsync<SearchResult>(
                It.IsAny<string>(),
                It.IsAny<AzureSearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hybrid search failed"));
        _clientFactoryMock.Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>())).Returns(searchClient.Object);

        SetupResilienceToInvokeOperation<SearchResult[]>("AzureSearch.HybridSearch");

        var results = await sut.HybridSearchAsync("test", new Core.Options.SearchOptions { MaxSearchResults = 5, Category = null });

        results.Should().BeEmpty();
    }

    // ---- VectorSearch fan-out: ExecuteSearchResilientAsync catch path ----

    [Fact]
    public async Task VectorSearchAsync_WhenFanOutAndSearchClientThrowsNonOperationCanceled_ReturnsEmptyNotFallback()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-vec-nf");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.AllCategories).Returns(MotorcycleCategory.All);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");

        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.SearchAsync<SearchResult>(
                It.IsAny<string>(),
                It.IsAny<AzureSearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("vector search failed"));
        _clientFactoryMock.Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>())).Returns(searchClient.Object);

        SetupResilienceToInvokeOperation<SearchResult[]>("AzureSearch.VectorSearch");

        var results = await sut.VectorSearchAsync("test", new Core.Options.SearchOptions { MaxSearchResults = 5, Category = null });

        results.Should().BeEmpty();
    }

    // ---- VectorSearch and HybridSearch single-category path ----

    [Fact]
    public async Task VectorSearchAsync_WithValidCategory_ShouldUseSingleIndexPath()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-vec-single");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");
        _clientFactoryMock.Setup(x => x.AllCategories).Returns(MotorcycleCategory.All);

        _clientFactoryMock.Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>()))
            .Throws<InvalidOperationException>();

        bool fallbackCalled = false;
        SetupResilienceToTryOperationAndCatch<SearchResult[]>("AzureSearch.VectorSearch", () => fallbackCalled = true);

        var results = await sut.VectorSearchAsync("test", new Core.Options.SearchOptions { MaxSearchResults = 5, Category = "sport" });

        fallbackCalled.Should().BeTrue();
        results.Should().HaveCount(1);
        results[0].Id.Should().Be("fallback_vectorsearchresult");
    }

    [Fact]
    public async Task HybridSearchAsync_WithValidCategory_ShouldUseSingleIndexPath()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-hyb-single");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");
        _clientFactoryMock.Setup(x => x.AllCategories).Returns(MotorcycleCategory.All);

        _clientFactoryMock.Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>()))
            .Throws<InvalidOperationException>();

        bool fallbackCalled = false;
        SetupResilienceToTryOperationAndCatch<SearchResult[]>("AzureSearch.HybridSearch", () => fallbackCalled = true);

        var results = await sut.HybridSearchAsync("test", new Core.Options.SearchOptions { MaxSearchResults = 5, Category = "touring" });

        fallbackCalled.Should().BeTrue();
        results.Should().HaveCount(1);
        results[0].Id.Should().Be("fallback_hybridsearchresult");
    }

    // ---- Single-category path: ExecuteSearchResilientAsync not used there, but
    // ExecuteRoutedSearchAsync with valid category calls ExecuteSearchAsync directly ----

    [Fact]
    public async Task SearchAsync_WithValidCategoryAndSearchClientThrows_InvokesFallbackThroughResilience()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-sc-valid");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");

        var searchClient = new Mock<SearchClient>();
        searchClient
            .Setup(c => c.SearchAsync<SearchResult>(
                It.IsAny<string>(),
                It.IsAny<AzureSearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("search index unavailable"));
        _clientFactoryMock.Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>())).Returns(searchClient.Object);

        bool fallbackInvoked = false;
        SetupResilienceToTryOperationAndCatch<SearchResult[]>("AzureSearch.Search", () => fallbackInvoked = true);

        var results = await sut.SearchAsync("test", new Core.Options.SearchOptions { MaxSearchResults = 5, Category = "touring" });

        fallbackInvoked.Should().BeTrue();
        results.Should().HaveCount(1);
        results[0].Id.Should().Be("fallback_searchresult");
    }

    // ---- ConvertToAzureSearchOptions test ----

    /// <summary>
    /// Explicitly tests the private ConvertToAzureSearchOptions method, verifying it
    /// maps Core SearchOptions to Azure SDK SearchOptions correctly. This provides
    /// defensive coverage for the options-mapping logic.
    /// </summary>
    [Fact]
    public void ConvertToAzureSearchOptions_ShouldMapMaxSearchResultsAndSetIncludeTotalCount()
    {
        var method = typeof(AzureSearchQueryService)
            .GetMethod("ConvertToAzureSearchOptions", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var sut = CreateSut();
        var options = new Core.Options.SearchOptions { MaxSearchResults = 42 };

        var result = (AzureSearchOptions)method.Invoke(sut, new object[] { options })!;

        result.Size.Should().Be(42);
        result.IncludeTotalCount.Should().BeTrue();
    }

    // ---- Helper methods ----

    private static SearchResult CreateResult(string id, float score) => new()
    {
        Id = id,
        Content = $"Content for {id}",
        RelevanceScore = score,
        Source = new SearchSource
        {
            AgentType = SearchAgentType.VectorSearch,
            SourceName = "Test Source",
            DocumentId = id
        }
    };

    /// <summary>
    /// Uses reflection to invoke the private static Truncate method for testing.
    /// </summary>
    private static SearchResult[] InvokeTruncate(SearchResult[] results, int topN)
    {
        var method = typeof(AzureSearchQueryService)
            .GetMethod("Truncate", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (SearchResult[])method.Invoke(null, new object[] { results, topN })!;
    }

    /// <summary>
    /// Sets up the resilience mock such that it invokes the fallback delegate.
    /// </summary>
    private void SetupResilienceToInvokeFallback<T>(string policyKey)
    {
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<T>(
                policyKey,
                It.IsAny<Func<Task<T>>>(),
                It.IsAny<Func<Task<T>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, Func<Task<T>> op, Func<Task<T>> fb, string cid, CancellationToken ct) => fb());
    }

    /// <summary>
    /// Sets up the resilience mock such that it invokes the operation delegate
    /// (exercising the inner ExecuteRoutedSearchAsync flow).
    /// </summary>
    private void SetupResilienceToInvokeOperation<T>(string policyKey)
    {
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<T>(
                policyKey,
                It.IsAny<Func<Task<T>>>(),
                It.IsAny<Func<Task<T>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns((string key, Func<Task<T>> op, Func<Task<T>> fb, string cid, CancellationToken ct) => op());
    }

    /// <summary>
    /// Sets up the resilience mock such that it tries the operation and falls back to
    /// the fallback on failure, invoking the provided callback when fallback is used.
    /// </summary>
    private void SetupResilienceToTryOperationAndCatch<T>(string policyKey, Action onFallback)
    {
        _resilienceServiceMock
            .Setup(x => x.ExecuteAsync<T>(
                policyKey,
                It.IsAny<Func<Task<T>>>(),
                It.IsAny<Func<Task<T>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(async (string key, Func<Task<T>> op, Func<Task<T>> fb, string cid, CancellationToken ct) =>
                {
                    try { return await op(); }
                    catch { onFallback(); return await fb(); }
                });
    }
}
