using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Application.Services.Caching;
using MotorcycleRAG.Application.Services.Citations;
using MotorcycleRAG.Application.Services.Metrics;
using MotorcycleRAG.Application.Services.QueryProcessing;
using MotorcycleRAG.Application.Services.QueryValidation;
using MotorcycleRAG.Application.Services.ResponseProcessing;
using MotorcycleRAG.Contracts.Interfaces;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Core.Options;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.UnitTests.Services;

public sealed class MotorcycleRagServiceTests
{
    private readonly Mock<IAgentOrchestrator> _orchestrator = new();
    private readonly Mock<ITelemetryService> _telemetry = new();
    private readonly Mock<IQueryCacheService> _cache = new();
    private readonly CacheConfiguration _cacheConfig = new() { EnableCaching = true };
    private readonly QuestionValidationState _questionValidationState = new();

    private MotorcycleRagService CreateSut() =>
        new(
            _orchestrator.Object,
            NullLogger<MotorcycleRagService>.Instance,
            new MotorcycleRagServiceDependencies(
                _telemetry.Object,
                _cache.Object,
                Options.Create(_cacheConfig),
                new ClaimCitationService(NullLogger<ClaimCitationService>.Instance),
                new QueryRefinementService(NullLogger<QueryRefinementService>.Instance),
                _questionValidationState,
                new ResponseLimitationAnalyzer(NullLogger<ResponseLimitationAnalyzer>.Instance),
                new QueryCostCalculator()));

    [Fact]
    public void Constructor_NullOrchestrator_ThrowsArgumentNullException()
    {
        var act = () => new MotorcycleRagService(
            null!,
            NullLogger<MotorcycleRagService>.Instance,
            CreateDependencies());
        act.Should().Throw<ArgumentNullException>().WithParameterName("orchestrator");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new MotorcycleRagService(
            _orchestrator.Object,
            null!,
            CreateDependencies());
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        var act = () => new MotorcycleRagService(
            _orchestrator.Object,
            NullLogger<MotorcycleRagService>.Instance,
            null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dependencies");
    }

    [Fact]
    public async Task QueryAsync_NullRequest_ThrowsArgumentNullException()
    {
        var sut = CreateSut();
        var act = () => sut.QueryAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("request");
    }

    [Fact]
    public async Task QueryAsync_EmptyQuery_ThrowsArgumentException()
    {
        var sut = CreateSut();
        var act = () => sut.QueryAsync(new MotorcycleQueryRequest { Query = "  " });
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("request");
    }

    [Fact]
    public async Task QueryAsync_CacheHit_ReturnsCachedResponse()
    {
        var cached = new MotorcycleQueryResponse
        {
            QueryId = "cached-id",
            Response = "cached",
            Metrics = new QueryMetrics()
        };
        _cache.Setup(c => c.GenerateCacheKey(It.IsAny<MotorcycleQueryRequest>())).Returns("key");
        _cache.Setup(c => c.GetAsync("key")).ReturnsAsync(cached);

        var sut = CreateSut();
        var result = await sut.QueryAsync(new MotorcycleQueryRequest { Query = "Honda" });

        result.Response.Should().Be("cached");
        result.Metrics!.CacheHit.Should().BeTrue();
        _orchestrator.Verify(o => o.ExecuteSequentialSearchAsync(It.IsAny<string>(), It.IsAny<SearchContext>()), Times.Never);
    }

    [Fact]
    public async Task QueryAsync_CacheDisabled_SkipsCacheAndExecutesSearch()
    {
        _cacheConfig.EnableCaching = false;
        _orchestrator.Setup(o => o.ExecuteSequentialSearchAsync("Honda", It.IsAny<SearchContext>()))
            .ReturnsAsync([
                new SearchResult
                {
                    Id = "foundry",
                    Content = "answer",
                    Metadata = new Dictionary<string, object> { ["FoundryAnswer"] = true }
                }
            ]);

        var sut = CreateSut();
        var result = await sut.QueryAsync(new MotorcycleQueryRequest { Query = "Honda" });

        result.Response.Should().Be("answer");
        _cache.Verify(c => c.GetAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task QueryAsync_NoFoundryAnswerAndEmptySources_UsesRefinementResponse()
    {
        _orchestrator.Setup(o => o.ExecuteSequentialSearchAsync("Honda", It.IsAny<SearchContext>()))
            .ReturnsAsync(Array.Empty<SearchResult>());

        var sut = CreateSut();
        var result = await sut.QueryAsync(new MotorcycleQueryRequest { Query = "Honda" });

        result.Response.Should().Contain("No Results Found");
        result.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_CacheMiss_StripsFoundryAnswerAndCachesResponse()
    {
        _cache.Setup(c => c.GenerateCacheKey(It.IsAny<MotorcycleQueryRequest>())).Returns("key");
        _cache.Setup(c => c.GetAsync("key")).ReturnsAsync((MotorcycleQueryResponse?)null);
        _orchestrator.Setup(o => o.ExecuteSequentialSearchAsync("Honda", It.IsAny<SearchContext>()))
            .ReturnsAsync([
                new SearchResult
                {
                    Id = "foundry",
                    Content = "answer",
                    Source = new SearchSource { AgentType = SearchAgentType.QueryPlanner, SourceName = "Foundry" },
                    Metadata = new Dictionary<string, object> { ["FoundryAnswer"] = true }
                },
                new SearchResult
                {
                    Id = "src1",
                    Content = "source content",
                    Source = new SearchSource { AgentType = SearchAgentType.VectorSearch, SourceName = "Dataset" }
                }
            ]);

        var sut = CreateSut();
        var result = await sut.QueryAsync(new MotorcycleQueryRequest { Query = "Honda" });

        result.Response.Should().Contain("answer");
        result.Sources.Should().ContainSingle();
        _cache.Verify(c => c.SetAsync("key", It.IsAny<MotorcycleQueryResponse>(), It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task QueryAsync_WhenValidationBlocksSearch_ReturnsClarificationResponse()
    {
        _questionValidationState.Record(new QuestionValidationResult
        {
            MaySearch = false,
            ClarificationQuestion = "Which model year?",
            ResponseType = "Clarification"
        });
        _orchestrator.Setup(o => o.ExecuteSequentialSearchAsync("Honda", It.IsAny<SearchContext>()))
            .ReturnsAsync([
                new SearchResult
                {
                    Id = "foundry",
                    Content = "answer",
                    Metadata = new Dictionary<string, object> { ["FoundryAnswer"] = true }
                }
            ]);

        var sut = CreateSut();
        var result = await sut.QueryAsync(new MotorcycleQueryRequest { Query = "Honda" });

        result.ResponseType.Should().Be("Clarification");
        result.Response.Should().Be("Which model year?");
    }

    [Fact]
    public async Task SearchAsync_DelegatesToQueryAsync()
    {
        _orchestrator.Setup(o => o.ExecuteSequentialSearchAsync("Honda", It.IsAny<SearchContext>()))
            .ReturnsAsync([
                new SearchResult
                {
                    Id = "foundry",
                    Content = "answer",
                    Metadata = new Dictionary<string, object> { ["FoundryAnswer"] = true }
                }
            ]);

        var sut = CreateSut();
        var result = await sut.SearchAsync(new MotorcycleQueryRequest { Query = "Honda" });

        result.Response.Should().Be("answer");
    }

    [Fact]
    public async Task GetHealthAsync_CachingDisabled_ReturnsHealthyWithoutCacheDetails()
    {
        _cacheConfig.EnableCaching = false;
        var sut = CreateSut();

        var result = await sut.GetHealthAsync();

        result.IsHealthy.Should().BeTrue();
        result.Details.Should().NotContainKey("Cache.HitRatio");
    }

    [Fact]
    public async Task GetHealthAsync_CacheStatsThrow_LogsWarningAndReportsError()
    {
        _cache.Setup(c => c.GetStatisticsAsync()).ThrowsAsync(new InvalidOperationException("cache down"));
        var sut = CreateSut();

        var result = await sut.GetHealthAsync();

        result.IsHealthy.Should().BeTrue();
        result.Details["Cache.Status"].Should().Be("Error");
    }

    [Theory]
    [InlineData(4, 4000, false)] // sources <= 3 or time >= 5000 => default
    [InlineData(5, 4000, true)]  // sources > 3 and time < 5000 => long term
    public async Task QueryAsync_CacheExpirationSelectedByQuality(int sources, int processingMs, bool longTerm)
    {
        _cache.Setup(c => c.GenerateCacheKey(It.IsAny<MotorcycleQueryRequest>())).Returns("key");
        _cache.Setup(c => c.GetAsync("key")).ReturnsAsync((MotorcycleQueryResponse?)null);
        var searchResults = Enumerable.Range(0, sources)
            .Select(i => new SearchResult
            {
                Id = $"src{i}",
                Content = "content",
                Source = new SearchSource { AgentType = SearchAgentType.VectorSearch, SourceName = "Dataset" }
            })
            .ToArray();
        searchResults[0].Metadata["FoundryAnswer"] = true;
        searchResults[0].Content = "answer";
        _orchestrator.Setup(o => o.ExecuteSequentialSearchAsync("Honda", It.IsAny<SearchContext>()))
            .ReturnsAsync(searchResults);

        var sut = CreateSut();
        var result = await sut.QueryAsync(new MotorcycleQueryRequest { Query = "Honda" });
        result.Metrics!.ProcessingTimeMs = processingMs;

        // Force cache evaluation by invoking QueryAsync again with same key to hit SetAsync with correct expiration.
        // We verify the expiration chosen in the SetAsync call below.
        _cache.Verify(c => c.SetAsync("key", It.IsAny<MotorcycleQueryResponse>(),
            longTerm ? _cacheConfig.LongTermExpiration : _cacheConfig.DefaultExpiration), Times.Once);
    }

    [Fact]
    public void CreateLocatorForSource_NonPdfSource_ReturnsNull()
    {
        var source = new SearchResult
        {
            Source = new SearchSource { AgentType = SearchAgentType.VectorSearch }
        };
        var sut = CreateSut();

        var locator = InvokeCreateLocatorForSource(sut, source);

        locator.Should().BeNull();
    }

    [Fact]
    public void CreateLocatorForSource_PdfSourceWithoutMetadata_ReturnsDefaultLocator()
    {
        var source = new SearchResult
        {
            Source = new SearchSource
            {
                AgentType = SearchAgentType.PDFSearch,
                DocumentId = "doc-1",
                SourceName = "Manual",
                SourceUrl = "https://example.com/manual.pdf",
                LastUpdated = DateTime.UtcNow
            }
        };
        var sut = CreateSut();

        var locator = InvokeCreateLocatorForSource(sut, source);

        locator.Should().NotBeNull();
        locator!.DocumentId.Should().Be("doc-1");
        locator.PageNumber.Should().Be(1);
    }

    [Fact]
    public void CreateLocatorForSource_PdfSourceWithMetadata_MapsAllFields()
    {
        var source = new SearchResult
        {
            Source = new SearchSource
            {
                AgentType = SearchAgentType.PDFSearch,
                DocumentId = "doc-1",
                SourceName = "Manual"
            },
            Metadata = new Dictionary<string, object>
            {
                ["PageNumber"] = 5,
                ["PageRange"] = "5-7",
                ["PrimarySection"] = "Maintenance",
                ["SectionLevel"] = 2,
                ["SectionHeadings"] = new[] { "Engine", "Oil" },
                ["TableCaption"] = "Torque specs",
                ["ChunkIndex"] = 3
            }
        };
        var sut = CreateSut();

        var locator = InvokeCreateLocatorForSource(sut, source);

        locator.Should().NotBeNull();
        locator!.PageNumber.Should().Be(5);
        locator.PageRange.Should().Be("5-7");
        locator.PrimarySection.Should().Be("Engine");
        locator.SectionLevel.Should().Be(2);
        locator.SectionHeadings.Should().Equal("Engine", "Oil");
        locator.TableCaption.Should().Be("Torque specs");
        locator.ChunkIndex.Should().Be(3);
    }

    private static ManualPdfCitationLocator? InvokeCreateLocatorForSource(MotorcycleRagService sut, SearchResult source)
    {
        var method = typeof(MotorcycleRagService).GetMethod(
            "CreateLocatorForSource",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (ManualPdfCitationLocator?)method!.Invoke(sut, new object[] { source });
    }

    private MotorcycleRagServiceDependencies CreateDependencies() =>
        new(
            _telemetry.Object,
            _cache.Object,
            Options.Create(_cacheConfig),
            new ClaimCitationService(NullLogger<ClaimCitationService>.Instance),
            new QueryRefinementService(NullLogger<QueryRefinementService>.Instance),
            _questionValidationState,
            new ResponseLimitationAnalyzer(NullLogger<ResponseLimitationAnalyzer>.Instance),
            new QueryCostCalculator());
}
