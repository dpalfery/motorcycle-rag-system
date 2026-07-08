using FluentAssertions;
using MotorcycleRAG.Contracts.Models.DTOs;
using MotorcycleRAG.Domain.Enums;
using MotorcycleRAG.Persistence.Azure.Search;

namespace MotorcycleRAG.UnitTests.Azure;

/// <summary>
/// Unit tests for the fan-out merge that combines per-category-index results by relevance
/// score (T4 acceptance criterion #1: "querying without category merges all 4").
/// </summary>
/// <remarks>
/// <see cref="AzureSearchQueryService.MergeByScore"/> is the pure, deterministic merge used
/// when no category is supplied. Testing it directly locks the merge order and truncation
/// without any Azure dependency.
/// </remarks>
public class AzureSearchQueryServiceFanOutMergeTests
{
    private static SearchResult Result(string id, float score) => new()
    {
        Id = id,
        Content = id,
        RelevanceScore = score,
        Source = new SearchSource
        {
            AgentType = SearchAgentType.VectorSearch,
            SourceName = id,
            DocumentId = id
        }
    };

    [Fact]
    public void MergeByScore_OrdersAcrossIndexesByScoreDescending()
    {
        var dirt = new[] { Result("dirt-0.9", 0.9f), Result("dirt-0.3", 0.3f) };
        var sport = new[] { Result("sport-0.8", 0.8f), Result("sport-0.7", 0.7f) };

        var merged = AzureSearchQueryService.MergeByScore(new[] { dirt, sport }, topN: 10);

        merged.Select(r => r.Id)
            .Should().Equal("dirt-0.9", "sport-0.8", "sport-0.7", "dirt-0.3");
    }

    [Fact]
    public void MergeByScore_TruncatesToTopN()
    {
        var indexes = new[]
        {
            new[] { Result("a", 0.95f), Result("b", 0.90f) },
            new[] { Result("c", 0.85f), Result("d", 0.80f), Result("e", 0.10f) }
        };

        var merged = AzureSearchQueryService.MergeByScore(indexes, topN: 3);

        merged.Should().HaveCount(3);
        merged.Select(r => r.Id).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void MergeByScore_EmptyInputs_YieldsEmpty()
    {
        AzureSearchQueryService.MergeByScore(Array.Empty<SearchResult[]>(), topN: 5).Should().BeEmpty();
        AzureSearchQueryService.MergeByScore(new[] { Array.Empty<SearchResult>() }, topN: 5).Should().BeEmpty();
    }

    [Fact]
    public void MergeByScore_FourIndexes_AllPartitionsContribute()
    {
        var partitions = new[]
        {
            new[] { Result("dirt", 0.6f) },
            new[] { Result("touring", 0.7f) },
            new[] { Result("sport", 0.5f) },
            new[] { Result("cruiser", 0.8f) }
        };

        var merged = AzureSearchQueryService.MergeByScore(partitions, topN: 10);

        merged.Should().HaveCount(4);
        merged.First().Id.Should().Be("cruiser"); // highest score
        merged.Select(r => r.RelevanceScore)
            .Should().BeInDescendingOrder();
    }

    [Fact]
    public void MergeByScore_TopNZeroOrNegative_ReturnsAll()
    {
        var indexes = new[] { new[] { Result("a", 0.9f), Result("b", 0.1f) } };

        AzureSearchQueryService.MergeByScore(indexes, topN: 0).Should().HaveCount(2);
    }
}
