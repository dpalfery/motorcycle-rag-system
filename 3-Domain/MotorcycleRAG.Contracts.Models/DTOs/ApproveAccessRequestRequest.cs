using System.ComponentModel.DataAnnotations;

namespace MotorcycleRAG.Contracts.Models.DTOs;

/// <summary>
/// Admin request for approving a pending access request.
/// </summary>
public class ApproveAccessRequestRequest {
    [Required]
    public TierLabel Tier { get; set; }

    [Required]
    public string ExpectedRowVersion { get; set; } = string.Empty;
}