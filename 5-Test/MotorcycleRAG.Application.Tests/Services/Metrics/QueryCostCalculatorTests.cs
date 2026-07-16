using System;
using FluentAssertions;
using MotorcycleRAG.Application.Services.Metrics;
using MotorcycleRAG.Contracts.Models.DTOs;
using Xunit;

namespace MotorcycleRAG.UnitTests.Services.Metrics;

public class QueryCostCalculatorTests
{
    private readonly QueryCostCalculator _sut;

    public QueryCostCalculatorTests()
    {
        _sut = new QueryCostCalculator();
    }

    [Fact]
    public void CalculateEstimatedCost_NullResponse_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => 
            _sut.CalculateEstimatedCost(Array.Empty<SearchResult>(), null!));
    }

    [Fact]
    public void CalculateEstimatedCost_EmptyResultsAndResponse_ReturnsZero()
    {
        var results = Array.Empty<SearchResult>();
        var response = string.Empty;

        var cost = _sut.CalculateEstimatedCost(results, response);

        cost.Should().Be(0m);
    }

    [Fact]
    public void CalculateEstimatedCost_WithValues_CalculatesCorrectCost()
    {
        var results = new[]
        {
            new SearchResult { Content = new string('a', 2000) },
            new SearchResult { Content = new string('b', 2000) }
        };
        // Total input content length = 4000. inputTokens = 4000 / 4 = 1000.
        // inputCost = (1000 / 1000) * 0.0015 = 0.0015

        var response = new string('c', 4000);
        // outputTokens = 4000 / 4 = 1000.
        // outputCost = (1000 / 1000) * 0.002 = 0.002

        // Total cost should be 0.0035

        var cost = _sut.CalculateEstimatedCost(results, response);

        cost.Should().Be(0.0035m);
    }

    [Fact]
    public void CalculateEstimatedCost_NullOrEmptyContentInResults_HandlesGracefully()
    {
        var results = new[]
        {
            new SearchResult { Content = null! },
            new SearchResult { Content = string.Empty },
            new SearchResult { Content = "abcd" } // 4 chars => 1 token => 1/1000 * 0.0015 = 0.0000015
        };

        var response = string.Empty;

        var cost = _sut.CalculateEstimatedCost(results, response);

        cost.Should().Be(0.0000015m);
    }
}
