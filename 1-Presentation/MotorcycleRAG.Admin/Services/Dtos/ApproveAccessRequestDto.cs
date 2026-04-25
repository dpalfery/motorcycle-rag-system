using MotorcycleRAG.Contracts.Models.DTOs;

namespace MotorcycleRAG.Admin.Services.Dtos;

/// <summary>
/// Admin-app DTO for approving a pending access request.
/// </summary>
internal sealed class ApproveAccessRequestDto {
    public TierLabel Tier { get; set; }

    public string ExpectedRowVersion { get; set; } = string.Empty;
}