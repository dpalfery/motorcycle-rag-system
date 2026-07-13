using MotorcycleRAG.Domain.Entities;
using MotorcycleRAG.Domain.Enums;

namespace MotorcycleRAG.Domian.Tests.Domain;

public sealed class QueryAndTrustPolicyTests
{
    [Fact]
    public void QueryPlan_WhenCreated_EnablesWebSearchAndParallelExecutionWithIsolatedSubQueries()
    {
        // Arrange
        var first = new QueryPlan();
        var second = new QueryPlan();

        // Act
        first.SubQueries.Add("What is the chain slack?");

        // Assert
        first.OriginalQuery.Should().BeEmpty();
        first.UseWebSearch.Should().BeTrue();
        first.RunParallel.Should().BeTrue();
        first.SubQueries.Should().ContainSingle().Which.Should().Be("What is the chain slack?");
        second.SubQueries.Should().BeEmpty();
    }

    [Fact]
    public void WebTrustPolicy_WhenConfigured_RetainsAllowlistAndBlockingDecision()
    {
        // Arrange
        var createdAt = DateTime.Parse("2026-07-12T12:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var policyId = Guid.NewGuid();

        // Act
        var policy = new WebTrustPolicy
        {
            Id = policyId,
            DomainPattern = "*.honda.com",
            Tier = WebTrustTier.TierA,
            IsBlocked = false,
            Reason = "Manufacturer technical documentation.",
            AllowSubdomains = true,
            CreatedAt = createdAt
        };

        // Assert
        policy.Should().BeEquivalentTo(new
        {
            Id = policyId,
            DomainPattern = "*.honda.com",
            Tier = WebTrustTier.TierA,
            IsBlocked = false,
            Reason = "Manufacturer technical documentation.",
            AllowSubdomains = true,
            CreatedAt = createdAt,
            UpdatedAt = (DateTime?)null
        });
    }
}
