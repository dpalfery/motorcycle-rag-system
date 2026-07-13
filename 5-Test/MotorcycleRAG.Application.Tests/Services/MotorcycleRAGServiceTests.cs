using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
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
using Xunit;

namespace MotorcycleRAG.UnitTests.Services;

public class MotorcycleRagServiceTests
{
    private readonly Mock<IAgentOrchestrator> _orchestratorMock;
    private readonly Mock<ITelemetryService> _telemetryMock;
    private readonly Mock<IQueryCacheService> _cacheMock;
    private readonly Mock<QueryCostCalculator> _costCalculatorMock;
    private readonly Mock<ClaimCitationService> _citationMock;
    private readonly Mock<QueryRefinementService> _refinementMock;
    private readonly Mock<ResponseLimitationAnalyzer> _limitationMock;
    private readonly MotorcycleRagService _sut;
    private readonly MotorcycleRagServiceDependencies _dependencies;
    private readonly QuestionValidationState _validationState;

    public MotorcycleRagServiceTests()
    {
        _orchestratorMock = new Mock<IAgentOrchestrator>();
        _telemetryMock = new Mock<ITelemetryService>();
        _cacheMock = new Mock<IQueryCacheService>();
        _costCalculatorMock = new Mock<QueryCostCalculator>();
        
        var citationLogger = NullLogger<ClaimCitationService>.Instance;
        _citationMock = new Mock<ClaimCitationService>(citationLogger);
        
        _refinementMock = new Mock<QueryRefinementService>(NullLogger<QueryRefinementService>.Instance);
        _limitationMock = new Mock<ResponseLimitationAnalyzer>(NullLogger<ResponseLimitationAnalyzer>.Instance);
        _validationState = new QuestionValidationState();

        var cacheConfig = Options.Create(new CacheConfiguration { EnableCaching = true });

        _dependencies = new MotorcycleRagServiceDependencies(
            _telemetryMock.Object,
            _cacheMock.Object,
            cacheConfig,
            _citationMock.Object,
            _refinementMock.Object,
            _validationState,
            _limitationMock.Object,
            _costCalculatorMock.Object
        );

        _sut = new MotorcycleRagService(_orchestratorMock.Object, NullLogger<MotorcycleRagService>.Instance, _dependencies);
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MotorcycleRagService(null!, NullLogger<MotorcycleRagService>.Instance, _dependencies));
        Assert.Throws<ArgumentNullException>(() => new MotorcycleRagService(_orchestratorMock.Object, null!, _dependencies));
        Assert.Throws<ArgumentNullException>(() => new MotorcycleRagService(_orchestratorMock.Object, NullLogger<MotorcycleRagService>.Instance, null!));
    }

    [Fact]
    public async Task QueryAsync_InvalidQuery_ThrowsArgumentException()
    {
        var request = new MotorcycleQueryRequest { Query = "" };
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.QueryAsync(request));
    }

    [Fact]
    public async Task QueryAsync_CacheHit_ReturnsCachedResponse()
    {
        var request = new MotorcycleQueryRequest { Query = "How to adjust chain?" };
        var cachedResponse = new MotorcycleQueryResponse { QueryId = "old-id", Response = "Adjust bolts" };
        
        _cacheMock.Setup(x => x.GenerateCacheKey(request)).Returns("cache-key");
        _cacheMock.Setup(x => x.GetAsync("cache-key")).ReturnsAsync(cachedResponse);

        var result = await _sut.QueryAsync(request);

        result.Should().BeEquivalentTo(cachedResponse);
        result.QueryId.Should().NotBe("old-id");
        _telemetryMock.Verify(x => x.TrackQuery(It.IsAny<string>(), "How to adjust chain?", TimeSpan.Zero, 0, 0), Times.Once);
    }

    [Fact]
    public async Task QueryAsync_ValidationFails_ReturnsClarificationResponse()
    {
        var request = new MotorcycleQueryRequest { Query = "chain?" };
        _validationState.Initialize("chain?", null);
        _validationState.Record(new QuestionValidationResult { MaySearch = false, ClarificationQuestion = "Can you clarify?" });

        var results = Array.Empty<SearchResult>();
        _orchestratorMock.Setup(x => x.ExecuteSequentialSearchAsync("chain?", It.IsAny<SearchContext>()))
            .ReturnsAsync(results);
            
        var result = await _sut.QueryAsync(request);

        result.ResponseType.Should().Be("Clarification");
        result.Response.Should().Be("Can you clarify?");
        _telemetryMock.Verify(x => x.TrackQuery(It.IsAny<string>(), "chain?", It.IsAny<TimeSpan>(), 0, 0), Times.Once);
    }

    [Fact]
    public async Task GetHealthAsync_ReturnsHealthy()
    {
        _cacheMock.Setup(x => x.GetStatisticsAsync()).ReturnsAsync(new CacheStatistics { CacheHits = 5, TotalEntries = 10, TotalMemoryUsage = 2048 });

        var result = await _sut.GetHealthAsync();

        result.IsHealthy.Should().BeTrue();
        result.Status.Should().Be("OK");
        result.Details.Should().ContainKey("Cache.TotalEntries");
    }
}
