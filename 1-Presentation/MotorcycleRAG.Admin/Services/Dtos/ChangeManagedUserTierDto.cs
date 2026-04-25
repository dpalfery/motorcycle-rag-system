using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.Services.Dtos;

/// <summary>
/// Admin-app DTO for changing an existing managed user's onboarding tier.
/// </summary>
internal sealed class ChangeManagedUserTierDto {
    public TierLabel Tier { get; set; }

    public string ExpectedRowVersion { get; set; } = string.Empty;

    public string? Reason { get; set; }
}