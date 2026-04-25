using Microsoft.Extensions.Logging;
using Moq;
using MotorcycleRAG.Application.Services;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.UnitTests.Services;

public class TierEntitlementMappingServiceTests {
    [Theory]
    [InlineData(TierLabel.Trial, "Free", "DemoUser")]
    [InlineData(TierLabel.RoadRunner, "Pro", "Roadrunner")]
    [InlineData(TierLabel.Admin, "Pro", "mcr-api-admin")]
    public void Resolve_ReturnsCanonicalPlanAndRole(TierLabel tier, string expectedPlan, string expectedRole) {
        var service = new TierEntitlementMappingService(new Mock<ILogger<TierEntitlementMappingService>>().Object);

        var mapping = service.Resolve(tier);

        Assert.Equal(expectedPlan, mapping.PlanName);
        Assert.Equal(expectedRole, mapping.AppRole);
    }
}
