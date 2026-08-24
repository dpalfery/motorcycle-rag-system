using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Core.Utilities;
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

    // ---- Successful search path: ExecuteSearchAsync response-processing loop ----
    // These tests cover the await foreach loop that maps Azure SearchResult<T>
    // documents (with metadata, highlights, score) to the application DTO.

    [Fact]
    public async Task SearchAsync_WithValidCategoryAndSuccessfulResponse_ReturnsMappedResults()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-success");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");

        // Source DTO with non-empty Metadata and Highlights to exercise the copy loops
        var sourceDoc = new SearchResult
        {
            Id = "engine-001",
            Content = "Engine maintenance guide for V-twin motorcycles",
            RelevanceScore = 0.0f,
            Source = new SearchSource
            {
                AgentType = SearchAgentType.VectorSearch,
                SourceName = "Service Manual",
                DocumentId = "d1"
            },
            GeneratedAt = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Metadata = new Dictionary<string, object>
            {
                ["page"] = 42,
                ["section"] = "engine"
            },
            Highlights = new List<string> { "Engine", "V-twin" }
        };

        IDictionary<string, IList<string>> emptyHighlights =
            new Dictionary<string, IList<string>>();
        var azureResp = BuildAzureSearchResponse(
            new[] { (sourceDoc, 0.87d, emptyHighlights) });

        var searchClientMock = new Mock<SearchClient>();
        searchClientMock
            .Setup(c => c.SearchAsync<SearchResult>(
                It.IsAny<string>(),
                It.IsAny<AzureSearchOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(azureResp);

        _clientFactoryMock
            .Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>()))
            .Returns(searchClientMock.Object);

        SetupResilienceToInvokeOperation<SearchResult[]>("AzureSearch.Search");

        // Act
        var results = await sut.SearchAsync("engine maintenance",
            new Core.Options.SearchOptions { MaxSearchResults = 10, Category = "sport" });

        // Assert — verify the response-processing loop mapped all fields correctly
        results.Should().HaveCount(1);
        results[0].Id.Should().Be("engine-001");
        results[0].Content.Should().Be("Engine maintenance guide for V-twin motorcycles");
        results[0].RelevanceScore.Should().Be(0.87f);
        results[0].Source.SourceName.Should().Be("Service Manual");
        results[0].GeneratedAt.Should().Be(new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        results[0].Metadata.Should().HaveCount(2);
        results[0].Metadata["page"].Should().Be(42);
        results[0].Metadata["section"].Should().Be("engine");
        results[0].Highlights.Should().BeEquivalentTo(new[] { "Engine", "V-twin" });
    }

    [Fact]
    public async Task SearchAsync_FanOutWithSuccessfulResponses_ReturnsMergedByScore()
    {
        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-fo-ok");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.AllCategories).Returns(MotorcycleCategory.All);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns((MotorcycleCategory c) => $"motorcycle-{c.Value.ToLowerInvariant()}");

        // Create per-category responses with different scores
        var sportDoc = CreateResult("sport-1", 0.9f);
        sportDoc.Metadata["category"] = "sport";
        var touringDoc = CreateResult("touring-1", 0.7f);
        touringDoc.Metadata["category"] = "touring";
        var dirtDoc = CreateResult("dirt-1", 0.5f);
        dirtDoc.Metadata["category"] = "dirt";

        // Stub GetClient to return a distinct SearchClient per category
        var clients = new Dictionary<string, Mock<SearchClient>>();
        (SearchResult doc, double score)[] setup =
        {
            (sportDoc, 0.9d), (touringDoc, 0.7d), (dirtDoc, 0.5d)
        };
        var categories = MotorcycleCategory.All.Reverse().ToArray();
        for (int i = 0; i < Math.Min(setup.Length, categories.Length); i++)
        {
            var cat = categories[i];
            var (doc, score) = setup[i];
            IDictionary<string, IList<string>> emptyHl =
                new Dictionary<string, IList<string>>();
            var resp = BuildAzureSearchResponse(
                new[] { (doc, score, emptyHl) });
            var clientMock = new Mock<SearchClient>();
            clientMock
                .Setup(c => c.SearchAsync<SearchResult>(
                    It.IsAny<string>(),
                    It.IsAny<AzureSearchOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(resp);
            clients[cat.Value] = clientMock;
        }

        _clientFactoryMock
            .Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>()))
            .Returns((MotorcycleCategory c) => clients.TryGetValue(c.Value, out var m) ? m.Object : null!);

        SetupResilienceToInvokeOperation<SearchResult[]>("AzureSearch.Search");

        // Act
        var results = await sut.SearchAsync("motorcycle maintenance",
            new Core.Options.SearchOptions { MaxSearchResults = 10, Category = null });

        // Assert — fan-out merged results, ordered by score descending
        results.Should().HaveCount(3);
        results[0].Id.Should().Be("sport-1");    // 0.9
        results[1].Id.Should().Be("touring-1");  // 0.7
        results[2].Id.Should().Be("dirt-1");     // 0.5
    }

    // ---- Test infrastructure for Azure SDK response mocking ----

    /// <summary>
    /// Builds a mock <see cref="Response{SearchResults{SearchResult}}"/> that wraps the given
    /// document/score tuples, enabling the <c>ExecuteSearchAsync</c> response-processing loop
    /// to be exercised without hitting a real Azure Search instance.
    /// </summary>
    /// <remarks>
    /// Uses fully-qualified type names to disambiguate from the test-project namespace
    /// <c>MotorcycleRAG.Persistence.Tests.Azure.Search</c> which shadows
    /// <c>Azure.Search.Documents.Models</c>.
    /// </remarks>
    private static Response<
        global::Azure.Search.Documents.Models.SearchResults<SearchResult>
    > BuildAzureSearchResponse(
        (SearchResult Document, double Score,
         IDictionary<string, IList<string>> Highlights)[] items)
    {
        var azureResults = new List<
            global::Azure.Search.Documents.Models.SearchResult<SearchResult>>();
        foreach (var (doc, score, highlights) in items)
        {
            var readonlyHighlights =
                new System.Collections.ObjectModel.ReadOnlyDictionary<string, IList<string>>(highlights);
            var azureSr = global::Azure.Search.Documents.Models.SearchModelFactory.SearchResult<SearchResult>(
                doc, score, readonlyHighlights);
            azureResults.Add(azureSr);
        }

        var pageable = new FakeAsyncPageable<
            global::Azure.Search.Documents.Models.SearchResult<SearchResult>>(
            azureResults.ToArray());

        // SearchResults<T>.GetResultsAsync() is non-virtual; use SearchModelFactory
        // to create one whose GetResultsAsync() returns our FakeAsyncPageable.
        var searchResults = global::Azure.Search.Documents.Models.SearchModelFactory
            .SearchResults<SearchResult>(azureResults, azureResults.Count,
                new Dictionary<string, IList<global::Azure.Search.Documents.Models.FacetResult>>(),
                null, null, null);

        var responseMock = new Mock<
            global::Azure.Response<
                global::Azure.Search.Documents.Models.SearchResults<SearchResult>
            >>();
        responseMock.Setup(r => r.Value).Returns(searchResults);

        return responseMock.Object;
    }

    /// <summary>
    /// Minimal fake <see cref="AsyncPageable{T}"/> that synchronously yields
    /// a pre-built list of items, sufficient for unit-testing the
    /// <c>await foreach</c> in <c>ExecuteSearchAsync</c>.
    /// </summary>
    private sealed class FakeAsyncPageable<T> : AsyncPageable<T> where T : notnull
    {
        private readonly T[] _items;

        public FakeAsyncPageable(T[] items) => _items = items;

        public override async IAsyncEnumerator<T> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            foreach (var item in _items)
                yield return item;
        }

        public override AsyncPageable<Page<T>> AsPages(
            string? continuationToken = null, int? pageSizeHint = null)
        {
            var mockResp = new Mock<global::Azure.Response>();
            var page = Page<T>.FromValues(_items, continuationToken, mockResp.Object);
            return new FakeAsyncPageable<Page<T>>(new[] { page });
        }
    }

    // -------------------------------------------------------------------------
    // T10 proof: real Azure SDK JSON deserialization carries indexedArtifactId
    // -------------------------------------------------------------------------
    // Every test above feeds a hand-built SearchResult DTO through
    // SearchModelFactory, which never invokes the Azure SDK's STJ deserializer —
    // that is the mock blind-spot the plan flags. These two tests feed raw JSON
    // through a REAL SearchClient (a stub HttpMessageHandler wired via Azure.Core's
    // HttpClientTransport), so the SDK deserializes the index document itself and
    // ExecuteSearchAsync's real projection runs. The seam mocked is
    // ISearchClientFactory.GetClient() returning a real SearchClient backed by the
    // stub HTTP transport; nothing above the HTTP boundary is faked, so this proves
    // the actual JSON→SearchResult.Metadata["indexedArtifactId"] mapping.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_RealSdkDeserialization_ProjectsAnchorFieldsIntoMetadata()
    {
        const string indexedArtifactId = "550e8400-e29b-41d4-a716-446655440000";
        const string ingestionJobId = "11111111-1111-1111-1111-111111111111";
        const string sourceContentHash = "sha256:deadbeef";

        // Raw index-document JSON exactly as Azure AI Search would return it (OData
        // envelope + a value[] element carrying the three T9 anchor fields). The field
        // names match the index schema (camelCase), which the SDK's default serializer
        // maps to the PascalCase SearchResult properties.
        var json = "{\"@odata.context\":\"https://fake.search.windows.net/indexes('motorcycle-sport')/docs($count=false)\","
                 + "\"value\":[{"
                 + "\"@search.score\":0.92,"
                 + "\"id\":\"upload-pdf-0\","
                 + "\"content\":\"engine maintenance\","
                 + "\"indexedArtifactId\":\"" + indexedArtifactId + "\","
                 + "\"ingestionJobId\":\"" + ingestionJobId + "\","
                 + "\"sourceContentHash\":\"" + sourceContentHash + "\""
                 + "}]}";

        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-t10-anchor");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");
        _clientFactoryMock.Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>()))
            .Returns(BuildRealSearchClientReturningJson(json));
        SetupResilienceToInvokeOperation<SearchResult[]>("AzureSearch.Search");

        var results = await sut.SearchAsync("engine",
            new Core.Options.SearchOptions { MaxSearchResults = 10, Category = "sport" });

        // The SDK deserialized the real JSON, ExecuteSearchAsync projected the anchors.
        results.Should().HaveCount(1);
        // The three T9/T10 anchors survived the REAL Azure SDK deserializer + the
        // ExecuteSearchAsync projection into Metadata. (Id/Content mapping is a
        // pre-existing concern outside T10 scope; the anchor is the contract here.)
        results[0].Metadata.Should().ContainKey("indexedArtifactId");
        results[0].Metadata["indexedArtifactId"].Should().Be(indexedArtifactId);
        results[0].Metadata.Should().ContainKey("ingestionJobId");
        results[0].Metadata["ingestionJobId"].Should().Be(ingestionJobId);
        results[0].Metadata.Should().ContainKey("sourceContentHash");
        results[0].Metadata["sourceContentHash"].Should().Be(sourceContentHash);
    }

    [Fact]
    public async Task SearchAsync_RealSdkDeserialization_WithoutAnchorFields_OmitsMetadataKeys()
    {
        // An index document that predates the T9 schema change (no anchor fields)
        // must NOT have the keys synthesized — the hop emits indexed_artifact_id: null.
        var json = "{\"@odata.context\":\"https://fake.search.windows.net/indexes('motorcycle-sport')/docs($count=false)\","
                 + "\"value\":[{"
                 + "\"@search.score\":0.5,"
                 + "\"id\":\"legacy-chunk-0\","
                 + "\"content\":\"legacy content\""
                 + "}]}";

        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-t10-legacy");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");
        _clientFactoryMock.Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>()))
            .Returns(BuildRealSearchClientReturningJson(json));
        SetupResilienceToInvokeOperation<SearchResult[]>("AzureSearch.Search");

        var results = await sut.SearchAsync("legacy",
            new Core.Options.SearchOptions { MaxSearchResults = 10, Category = "sport" });

        results.Should().HaveCount(1);
        results[0].Metadata.Should().NotContainKey("indexedArtifactId");
        results[0].Metadata.Should().NotContainKey("ingestionJobId");
        results[0].Metadata.Should().NotContainKey("sourceContentHash");
    }

    // -------------------------------------------------------------------------
    // T9 fix proof: real Azure SDK JSON deserialization maps camelCase id/content
    // -------------------------------------------------------------------------
    // The Azure.Search.Documents document deserializer uses PLAIN System.Text.Json
    // (no camelCase/case-insensitive policy). The index schema stores these fields
    // as camelCase "id"/"content". Without an explicit [JsonPropertyName] on the
    // SearchResult properties, PascalCase Id/Content do not map and deserialize
    // EMPTY in production. This test feeds the real SDK deserializer camelCase JSON
    // and asserts the values survive. FAILS until [JsonPropertyName] is added.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_RealSdkDeserialization_MapsCamelCaseIdAndContentFields()
    {
        // Raw index-document JSON with camelCase field names exactly as the index
        // stores them. The SDK deserializer runs with its default (plain) STJ
        // options — no camelCase/case-insensitive policy.
        var json = "{\"@odata.context\":\"https://fake.search.windows.net/indexes('motorcycle-sport')/docs($count=false)\","
                 + "\"value\":[{"
                 + "\"@search.score\":0.88,"
                 + "\"id\":\"abc-123\","
                 + "\"content\":\"engine torque specifications\""
                 + "}]}";

        var sut = CreateSut();
        _correlationServiceMock.Setup(x => x.GetOrCreateCorrelationId()).Returns("corr-t9-idcontent");
        _correlationServiceMock.Setup(x => x.CreateLoggingScope(It.IsAny<Dictionary<string, object>>()))
            .Returns(new Mock<IDisposable>().Object);
        _clientFactoryMock.Setup(x => x.GetIndexName(It.IsAny<MotorcycleCategory>()))
            .Returns("motorcycle-sport");
        _clientFactoryMock.Setup(x => x.GetClient(It.IsAny<MotorcycleCategory>()))
            .Returns(BuildRealSearchClientReturningJson(json));
        SetupResilienceToInvokeOperation<SearchResult[]>("AzureSearch.Search");

        var results = await sut.SearchAsync("torque",
            new Core.Options.SearchOptions { MaxSearchResults = 10, Category = "sport" });

        results.Should().HaveCount(1);
        // These assertions FAIL without [JsonPropertyName("id")]/["content"]: the
        // SDK's plain STJ deserializer does not map camelCase index fields to the
        // PascalCase properties, so they deserialize as the default empty string.
        results[0].Id.Should().Be("abc-123");
        results[0].Content.Should().Be("engine torque specifications");
    }

    // -------------------------------------------------------------------------
    // T9 cache round-trip gate: [JsonPropertyName("id")]/["content"] must not break
    // the query-cache serialization path. Both cache services (MemoryQueryCacheService
    // and DistributedQueryCacheService) serialize MotorcycleQueryResponse (which
    // embeds SearchResult[]) with { PropertyNamingPolicy = CamelCase, WriteIndented =
    // false }. The API global policy (JsonSerializationConfiguration.DefaultOptions)
    // is camelCase + case-insensitive + WhenWritingNull. The explicit attribute pins
    // the wire name to "id"/"content" under BOTH policies, so the round-trip must hold.
    // If this gate FAILS after adding the attribute, the fix is a regression — STOP.
    // -------------------------------------------------------------------------

    [Fact]
    public void SearchResult_IdAndContent_RoundTripUnderBothCacheAndApiPolicies()
    {
        // Populate a SearchResult with non-default Id/Content (plus anchor fields that
        // carry WhenWritingNull so they exercise the DefaultOptions ignore condition).
        var original = new SearchResult
        {
            Id = "rt-id-42",
            Content = "round-trip body content",
            RelevanceScore = 0.91f,
            Source = new SearchSource
            {
                AgentType = SearchAgentType.VectorSearch,
                SourceName = "RoundTrip Source",
                DocumentId = "rt-doc-1"
            },
            IndexedArtifactId = "550e8400-e29b-41d4-a716-446655440000",
            Metadata = { ["page"] = 7 }
        };

        // (a) API/global policy — camelCase + case-insensitive + WhenWritingNull +
        //     JsonStringEnumConverter (what the HTTP boundary emits/consumes).
        var apiOptions = JsonSerializationConfiguration.DefaultOptions;

        // (b) Cache-services policy — exactly what MemoryQueryCacheService and
        //     DistributedQueryCacheService construct internally.
        var cacheOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        foreach (var options in new[] { apiOptions, cacheOptions })
        {
            var json = JsonSerializer.Serialize(original, options);
            var roundTripped = JsonSerializer.Deserialize<SearchResult>(json, options)!;

            roundTripped.Id.Should().Be("rt-id-42",
                "the [JsonPropertyName(\"id\")] attribute pins the wire name across both policies");
            roundTripped.Content.Should().Be("round-trip body content",
                "the [JsonPropertyName(\"content\")] attribute pins the wire name across both policies");
            roundTripped.IndexedArtifactId.Should().Be("550e8400-e29b-41d4-a716-446655440000");
            roundTripped.RelevanceScore.Should().Be(0.91f);
        }
    }

    /// <summary>
    /// Builds a real <see cref="SearchClient"/> that returns the given raw JSON body
    /// for every request, via a stub <see cref="HttpMessageHandler"/> wired through
    /// Azure.Core's <c>HttpClientTransport</c>. The SDK's full request → response →
    /// STJ-deserialization pipeline runs unmodified; only the network is faked.
    /// </summary>
    private static SearchClient BuildRealSearchClientReturningJson(string json)
    {
        var handler = new StubSearchHandler(json);
        var options = new global::Azure.Search.Documents.SearchClientOptions
        {
            Transport = new global::Azure.Core.Pipeline.HttpClientTransport(handler),
            Retry = { MaxRetries = 0 }
        };
        return new SearchClient(
            new Uri("https://fake.search.windows.net"),
            "motorcycle-sport",
            new AzureKeyCredential("fake-key"),
            options);
    }

    /// <summary>
    /// Returns a canned JSON response body for any Azure Search request, bypassing
    /// the network. Used to drive the real SDK deserializer with raw JSON so the
    /// JSON→SearchResult mapping is exercised (closing the mock blind-spot).
    /// </summary>
    private sealed class StubSearchHandler : HttpMessageHandler
    {
        private readonly byte[] _content;

        public StubSearchHandler(string json) => _content = Encoding.UTF8.GetBytes(json);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(response);
        }
    }
}
