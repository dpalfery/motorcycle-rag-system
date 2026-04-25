using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Admin request for changing an existing managed user's tier.
/// </summary>
public class ChangeManagedUserTierRequest {
    [Required]
    public TierLabel Tier { get; set; }

    [Required]
    public string ExpectedRowVersion { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Reason { get; set; }
}