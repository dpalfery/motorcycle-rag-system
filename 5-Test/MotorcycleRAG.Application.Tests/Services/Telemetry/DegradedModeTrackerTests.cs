using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Services.Telemetry;
using MotorcycleRAG.Contracts.Models.DTOs;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Telemetry;

public class DegradedModeTrackerTests
{
    private readonly Mock<ILogger<DegradedModeTracker>> _loggerMock;
    private readonly DegradedModeTracker _sut;

    public DegradedModeTrackerTests()
    {
        _loggerMock = new Mock<ILogger<DegradedModeTracker>>();
        _sut = new DegradedModeTracker(_loggerMock.Object);
    }

    [Fact]
    public void TrackSearchExecution_NullStatuses_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => 
            _sut.TrackSearchExecution(null!, TimeSpan.FromSeconds(1), 0));
    }

    [Fact]
    public void TrackSearchExecution_AllSuccessful_DoesNotLogWarning()
    {
        var statuses = new[]
        {
            new SourceExecutionStatus { AgentType = SearchAgentType.VectorSearch, Succeeded = true },
            new SourceExecutionStatus { AgentType = SearchAgentType.WebSearch, Succeeded = true }
        };

        _sut.TrackSearchExecution(statuses, TimeSpan.FromSeconds(1), 10);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public void TrackSearchExecution_AnyFailed_LogsWarning()
    {
        var statuses = new[]
        {
            new SourceExecutionStatus { AgentType = SearchAgentType.VectorSearch, Succeeded = true },
            new SourceExecutionStatus { AgentType = SearchAgentType.WebSearch, Succeeded = false, ErrorMessage = "Timeout" }
        };

        _sut.TrackSearchExecution(statuses, TimeSpan.FromSeconds(1), 10);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Degraded mode: 1 sources failed, 1 succeeded")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void UpdateQueryContextMetrics_NullParams_ThrowsArgumentNullException()
    {
        var context = new SearchContext();
        var metrics = new Dictionary<SearchAgentType, (TimeSpan Duration, int ResultsFound)>();
        var failed = Array.Empty<SourceExecutionStatus>();
        var successful = Array.Empty<SourceExecutionStatus>();

        Assert.Throws<ArgumentNullException>(() => _sut.UpdateQueryContextMetrics(null!, metrics, false, failed, successful));
        Assert.Throws<ArgumentNullException>(() => _sut.UpdateQueryContextMetrics(context, null!, false, failed, successful));
        Assert.Throws<ArgumentNullException>(() => _sut.UpdateQueryContextMetrics(context, metrics, false, null!, successful));
        Assert.Throws<ArgumentNullException>(() => _sut.UpdateQueryContextMetrics(context, metrics, false, failed, null!));
    }

    [Fact]
    public void UpdateQueryContextMetrics_NullQueryContext_ReturnsEarly()
    {
        // SearchContext has a QueryContext property which is null by default if not set/initialized
        var context = new SearchContext()
        {
            QueryContext = null!
        };
        var metrics = new Dictionary<SearchAgentType, (TimeSpan Duration, int ResultsFound)>();
        var failed = Array.Empty<SourceExecutionStatus>();
        var successful = Array.Empty<SourceExecutionStatus>();

        // Should not throw or do anything
        _sut.UpdateQueryContextMetrics(context, metrics, false, failed, successful);
    }

    [Fact]
    public void UpdateQueryContextMetrics_DegradedModeFalse_PopulatesProperties()
    {
        var context = new SearchContext()
        {
            QueryContext = new QueryContext()
        };
        var metrics = new Dictionary<SearchAgentType, (TimeSpan Duration, int ResultsFound)>
        {
            [SearchAgentType.VectorSearch] = (TimeSpan.FromMilliseconds(100), 5)
        };
        var failed = Array.Empty<SourceExecutionStatus>();
        var successful = new[] { new SourceExecutionStatus { AgentType = SearchAgentType.VectorSearch, ResultsCount = 5, Succeeded = true } };

        _sut.UpdateQueryContextMetrics(context, metrics, false, failed, successful);

        context.QueryContext.AdditionalProperties.Should().ContainKey("SearchPatternMetrics");
        context.QueryContext.AdditionalProperties.Should().ContainKey("DegradedMode");
        context.QueryContext.AdditionalProperties["DegradedMode"].Should().Be(false);
        context.QueryContext.AdditionalProperties.Should().NotContainKey("FailedSources");
        context.QueryContext.AdditionalProperties.Should().NotContainKey("AvailableSources");

        var patternMetrics = context.QueryContext.AdditionalProperties["SearchPatternMetrics"] as SearchPatternMetrics;
        patternMetrics.Should().NotBeNull();
        patternMetrics!.VectorSearchExecuted.Should().BeTrue();
        patternMetrics.WebSearchExecuted.Should().BeFalse();
        patternMetrics.VectorResultsFound.Should().Be(5);
    }

    [Fact]
    public void UpdateQueryContextMetrics_DegradedModeTrue_PopulatesPropertiesIncludingSources()
    {
        var context = new SearchContext()
        {
            QueryContext = new QueryContext()
        };
        var metrics = new Dictionary<SearchAgentType, (TimeSpan Duration, int ResultsFound)>
        {
            [SearchAgentType.VectorSearch] = (TimeSpan.FromMilliseconds(100), 5),
            [SearchAgentType.WebSearch] = (TimeSpan.FromMilliseconds(200), 0)
        };
        var failed = new[] { new SourceExecutionStatus { AgentType = SearchAgentType.WebSearch, Succeeded = false, ErrorMessage = "Failed" } };
        var successful = new[] { new SourceExecutionStatus { AgentType = SearchAgentType.VectorSearch, ResultsCount = 5, Succeeded = true } };

        _sut.UpdateQueryContextMetrics(context, metrics, true, failed, successful);

        context.QueryContext.AdditionalProperties.Should().ContainKey("SearchPatternMetrics");
        context.QueryContext.AdditionalProperties.Should().ContainKey("DegradedMode");
        context.QueryContext.AdditionalProperties["DegradedMode"].Should().Be(true);
        context.QueryContext.AdditionalProperties.Should().ContainKey("FailedSources");
        context.QueryContext.AdditionalProperties.Should().ContainKey("AvailableSources");

        var failedList = context.QueryContext.AdditionalProperties["FailedSources"] as System.Collections.IEnumerable;
        failedList.Should().NotBeNull();
        failedList!.Cast<object>().Should().HaveCount(1);

        var availableList = context.QueryContext.AdditionalProperties["AvailableSources"] as System.Collections.IEnumerable;
        availableList.Should().NotBeNull();
        availableList!.Cast<object>().Should().HaveCount(1);
    }

    [Theory]
    [InlineData("vector_search", SearchAgentType.VectorSearch, true)]
    [InlineData("web_search", SearchAgentType.WebSearch, true)]
    [InlineData("pdf_search", SearchAgentType.PDFSearch, true)]
    [InlineData("graph_query", SearchAgentType.VectorSearch, true)] // Default fallback
    [InlineData("vector_search", SearchAgentType.VectorSearch, false)]
    public void TrackFoundrySubRunResult_MapsAndTracksCorrectly(string toolName, SearchAgentType expectedType, bool succeeded)
    {
        if (succeeded)
        {
            _sut.TrackFoundrySubRunResult(toolName, true, null, 42);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Foundry sub-run succeeded: tool={toolName} agentType={expectedType} resultSize=42")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
        else
        {
            _sut.TrackFoundrySubRunResult(toolName, false, "Connection error", 0);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Foundry sub-run degraded: tool={toolName} agentType={expectedType} error=Connection error")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Degraded mode: 1 sources failed, 0 succeeded")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}
