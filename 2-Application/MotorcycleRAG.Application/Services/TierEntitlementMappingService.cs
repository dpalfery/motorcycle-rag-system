using Microsoft.Extensions.Logging;
using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Application.Services;

/// <summary>
/// Resolves onboarding tier labels to internal plan and role entitlements.
/// </summary>
public class TierEntitlementMappingService {
    private readonly ILogger<TierEntitlementMappingService> _logger;

    public TierEntitlementMappingService(ILogger<TierEntitlementMappingService> logger) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves the internal plan and app-role mapping for a user-facing onboarding tier.
    /// </summary>
    public (string PlanName, string AppRole) Resolve(TierLabel tier) {
        var mapping = tier switch {
            TierLabel.Trial => (PlanName: "Free", AppRole: "DemoUser"),
            TierLabel.RoadRunner => (PlanName: "Pro", AppRole: "Roadrunner"),
            TierLabel.Admin => (PlanName: "Pro", AppRole: "mcr-api-admin"),
            _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unsupported tier label")
        };

        _logger.LogInformation("Resolved tier {Tier} to plan {PlanName} and role {AppRole}", tier, mapping.PlanName, mapping.AppRole);
        return mapping;
    }
}