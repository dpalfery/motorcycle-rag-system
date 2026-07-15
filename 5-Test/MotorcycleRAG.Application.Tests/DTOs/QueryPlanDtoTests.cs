using MotorcycleRAG.Application.DTOs;

namespace MotorcycleRAG.Application.Tests.DTOs;

public sealed class QueryPlanDtoTests
{
    [Fact]
    public void QueryPlan_WhenCreated_EnablesWebSearchAndParallelExecutionWithIsolatedSubQueries()
    {
        // Arrange
        var first = new QueryPlanDto();
        var second = new QueryPlanDto();

        // Act
        first.SubQueries.Add("What is the chain slack?");

        // Assert
        first.OriginalQuery.Should().BeEmpty();
        first.UseWebSearch.Should().BeTrue();
        first.RunParallel.Should().BeTrue();
        first.SubQueries.Should().ContainSingle().Which.Should().Be("What is the chain slack?");
        second.SubQueries.Should().BeEmpty();
    }
}
