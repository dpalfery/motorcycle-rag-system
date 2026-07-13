using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Application.Services.Telemetry;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Services;

public sealed class SearchResultFusionServiceTests
{
    [Fact]
    public async Task FuseAndRankResultsAsync_WithNoResults_ReturnsEmptyCollection()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var results = await sut.FuseAndRankResultsAsync([], "Honda", new SearchParameters(), degradedMode: false);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task FuseAndRankResultsAsync_WithResults_RanksAndLimitsWithoutDegradedMetadata()
    {
        // Arrange
        var sut = CreateSut();
        var low = CreateResult("low", 0.2f);
        var high = CreateResult("high", 0.9f);
        var middle = CreateResult("middle", 0.5f);

        // Act
        var results = await sut.FuseAndRankResultsAsync(
            [low, high, middle],
            "Honda",
            new SearchParameters { MaxResults = 2 },
            degradedMode: false);

        // Assert
        results.Select(result => result.Id).Should().Equal("high", "middle");
        results.Should().OnlyContain(result => !result.Metadata.ContainsKey("degradedMode"));
    }

    [Fact]
    public async Task FuseAndRankResultsAsync_WhenDegraded_AddsDegradedMetadata()
    {
        // Arrange
        var sut = CreateSut();
        var result = CreateResult("result", 0.9f);

        // Act
        var results = await sut.FuseAndRankResultsAsync(
            [result],
            "Honda",
            new SearchParameters { MaxResults = 1 },
            degradedMode: true);

        // Assert
        results.Should().ContainSingle();
        results[0].Metadata["degradedMode"].Should().Be(true);
    }

    [Fact]
    public async Task FuseResultsWithSourceTrackingAsync_WhenSourceFails_TracksStatusAndMarksResultsDegraded()
    {
        // Arrange
        var sut = CreateSut();
        var high = CreateResult("high", 0.9f);
        var low = CreateResult("low", 0.2f);
        var sourceStatuses = new[]
        {
            new SourceExecutionStatus { AgentType = SearchAgentType.VectorSearch, Succeeded = true },
            new SourceExecutionStatus { AgentType = SearchAgentType.WebSearch, Succeeded = false },
        };

        // Act
        var results = await sut.FuseResultsWithSourceTrackingAsync(
            [low, high],
            "Honda",
            new SearchParameters { MaxResults = 1 },
            sourceStatuses);

        // Assert
        results.Should().ContainSingle().Which.Id.Should().Be("high");
        results[0].Metadata["degradedMode"].Should().Be(true);
        var tracking = results[0].Metadata["sourceExecutionStatus"];
        tracking.GetType().GetProperty("TotalSources")!.GetValue(tracking).Should().Be(2);
        tracking.GetType().GetProperty("SuccessfulSources")!.GetValue(tracking).Should().Be(1);
        tracking.GetType().GetProperty("FailedSources")!.GetValue(tracking).Should().Be(1);
        ((IEnumerable<string>)tracking.GetType().GetProperty("FailedSourceTypes")!.GetValue(tracking)!)
            .Should().Equal(SearchAgentType.WebSearch.ToString());
    }

    [Fact]
    public async Task ConstructorOrMethods_WithRequiredArgumentMissing_ThrowArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();
        var options = new SearchParameters();
        var result = CreateResult("result", 0.9f);

        // Act
        var nullLogger = () => new SearchResultFusionService(null!);
        var nullResults = () => sut.FuseAndRankResultsAsync(null!, "Honda", options, degradedMode: false);
        var nullOptions = () => sut.FuseAndRankResultsAsync([result], "Honda", null!, degradedMode: false);
        var nullSourceStatuses = () => sut.FuseResultsWithSourceTrackingAsync([result], "Honda", options, null!);

        // Assert
        nullLogger.Should().Throw<ArgumentNullException>();
        await nullResults.Should().ThrowAsync<ArgumentNullException>();
        await nullOptions.Should().ThrowAsync<ArgumentNullException>();
        await nullSourceStatuses.Should().ThrowAsync<ArgumentNullException>();
    }

    private static SearchResultFusionService CreateSut() =>
        new(NullLogger<SearchResultFusionService>.Instance);

    private static SearchResult CreateResult(string id, float relevanceScore) => new()
    {
        Id = id,
        Content = id,
        RelevanceScore = relevanceScore,
        Source = new SearchSource { SourceName = "unit" },
    };
}
